using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>The outcome of a successful resolution: the ČSFD id plus the rating
/// data that was already on the film page we fetched to confirm it.</summary>
public sealed record CsfdResolution(string Id, int? Percent, int? Votes);

/// <summary>
/// Resolves an item (title + original title + year) to a ČSFD film id.
/// Strict search-result matching runs first; then, because ČSFD omits the
/// "(original name)" line on search results whose original name equals the
/// query, near-year candidates are verified by fetching their film page and
/// comparing all listed names. Every accepted match has its film page checked,
/// which also yields the rating in the same request.
/// </summary>
public sealed class CsfdResolver
{
    private const int MaxPageVerifications = 4;

    private readonly CsfdClient _client;
    private readonly ILogger<CsfdResolver> _logger;

    public CsfdResolver(CsfdClient client, ILogger<CsfdResolver> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<CsfdResolution?> ResolveAsync(string? name, string? originalTitle, int? year, bool series, CancellationToken cancellationToken)
    {
        var titles = new[] { name, originalTitle }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (titles.Count == 0)
        {
            return null;
        }

        // ČSFD's search handles long queries poorly; retry with the pre-colon
        // part ("Pirates of the Caribbean: The Curse…" → "Pirates of the Caribbean").
        var queries = titles
            .Concat(titles.Select(t => t.Split(':')[0].Trim()).Where(p => p.Length >= 3))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedTitles = titles.Select(CsfdMatcher.NormalizeAggressive).Distinct().ToList();
        var candidates = new List<(CsfdSearchResult Result, bool Strict)>();

        foreach (var query in queries)
        {
            var results = await _client.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var match = CsfdMatcher.FindBestMatch(results, titles, year, series);
            if (match is not null && !candidates.Any(c => c.Result.Id == match.Id))
            {
                candidates.Add((match, Strict: true));
            }

            if (year.HasValue)
            {
                foreach (var c in results.Where(c => (series ? c.IsSeries : c.IsFilm)
                             && c.Year.HasValue && Math.Abs((long)c.Year.Value - year.Value) <= 1))
                {
                    if (!candidates.Any(x => x.Result.Id == c.Id))
                    {
                        candidates.Add((c, Strict: false));
                    }
                }
            }
        }

        // Strict matches first, then exact-year fallbacks before ±1 ones.
        var ordered = candidates
            .OrderByDescending(c => c.Strict)
            .ThenBy(c => c.Result.Year.HasValue && year.HasValue ? Math.Abs(c.Result.Year.Value - year.Value) : 2)
            .Take(MaxPageVerifications);

        foreach (var (candidate, strict) in ordered)
        {
            // Never persist an id whose film page we could not confirm: a transient
            // fetch failure must not permanently attach a wrong or unchecked match.
            var details = await _client.GetFilmDetailsAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
            if (!details.Success || details.IsSeries != series)
            {
                continue;
            }

            // The film page's own year outranks the search-context guess.
            if (year.HasValue && details.Year.HasValue && Math.Abs((long)details.Year.Value - year.Value) > 1)
            {
                continue;
            }

            // The film page's own names must confirm the match even for strict
            // search hits — a listing/page disagreement means we picked wrong.
            var nameVerified = details.Names.Any(n => normalizedTitles.Contains(CsfdMatcher.NormalizeAggressive(n)));
            if (nameVerified)
            {
                _logger.LogInformation(
                    strict
                        ? "Matched {Name} ({Year}) to ČSFD {CsfdId}"
                        : "Matched {Name} ({Year}) to ČSFD {CsfdId} via film-page verification",
                    name,
                    year,
                    candidate.Id);
                return new CsfdResolution(candidate.Id, details.Percent, details.Votes);
            }
        }

        _logger.LogInformation("No ČSFD match for {Name} ({Year})", name, year);
        return null;
    }
}
