using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Resolves a movie (title + original title + year) to a ČSFD film id.
/// Two passes: strict search-result matching first; then, because ČSFD omits
/// the "(original name)" line on search results whose original name equals the
/// query, exact-year candidates are verified by fetching their film page and
/// comparing all listed names.
/// </summary>
public sealed class CsfdResolver
{
    private readonly CsfdClient _client;
    private readonly ILogger<CsfdResolver> _logger;

    public CsfdResolver(CsfdClient client, ILogger<CsfdResolver> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string?> ResolveAsync(string? name, string? originalTitle, int? year, CancellationToken cancellationToken)
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

        var normalizedTitles = titles.Select(CsfdMatcher.Normalize).Distinct().ToList();
        var fallback = new List<CsfdSearchResult>();

        foreach (var query in titles)
        {
            var results = await _client.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var match = CsfdMatcher.FindBestMatch(results, titles, year);
            if (match is not null)
            {
                _logger.LogInformation("Matched {Name} ({Year}) to ČSFD {CsfdId}", name, year, match.Id);
                return match.Id;
            }

            if (year.HasValue)
            {
                fallback.AddRange(results.Where(c => c.IsFilm && c.Year == year.Value));
            }
        }

        // Verification pass: only exact-year candidates, capped to keep traffic bounded.
        foreach (var candidate in fallback.DistinctBy(c => c.Id).Take(3))
        {
            var film = await _client.GetFilmNamesAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
            if (film.Success && film.Names.Any(n => normalizedTitles.Contains(CsfdMatcher.Normalize(n))))
            {
                _logger.LogInformation("Matched {Name} ({Year}) to ČSFD {CsfdId} via film-page verification", name, year, candidate.Id);
                return candidate.Id;
            }
        }

        _logger.LogInformation("No ČSFD match for {Name} ({Year})", name, year);
        return null;
    }
}
