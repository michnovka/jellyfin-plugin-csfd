using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

public sealed record CsfdSearchResult(string Id, string Title, string? OriginalName, int? Year, bool IsFilm);

public sealed record CsfdRatingResult(bool Success, int? Percent);

/// <summary>
/// Scraping client for csfd.cz. Owns a cookie jar so the Anubis anti-bot cookie
/// survives between requests, and serializes all traffic through a rate limiter.
/// Registered as a singleton.
/// </summary>
public sealed partial class CsfdClient : IDisposable
{
    private const string BaseUrl = "https://www.csfd.cz";
    private const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0";

    private readonly HttpClient _httpClient;
    private readonly ILogger<CsfdClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    // Matches one search result: film link + title, e.g.
    // <a href="/film/4570-pelisky/prehled/" class="film-title-name">Pelíšky</a>
    [GeneratedRegex("""<a href="/film/(?<id>\d+-[^/"]+)/[^"]*"\s+class="film-title-name">(?<title>[^<]+)</a>""")]
    private static partial Regex SearchCandidateRegex();

    // Original title shown beneath a search result: <p class="search-name">(The Matrix)</p>
    [GeneratedRegex("""<p class="search-name">\s*\((?<name>[^)]*)\)""")]
    private static partial Regex SearchNameRegex();

    [GeneratedRegex(@"\((?<year>(?:19|20)\d{2})\)")]
    private static partial Regex YearRegex();

    // <div class="film-rating-average"> 95% </div>
    [GeneratedRegex(@"film-rating-average[^>]*>\s*(?<percent>\d{1,3})\s*%", RegexOptions.Singleline)]
    private static partial Regex RatingRegex();

    public CsfdClient(ILogger<CsfdClient> logger)
    {
        _logger = logger;
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "cs,sk;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml");
    }

    public async Task<IReadOnlyList<CsfdSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var html = await GetPageAsync($"{BaseUrl}/hledat/?q={Uri.EscapeDataString(query)}", cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return [];
        }

        var matches = SearchCandidateRegex().Matches(html);
        var results = new List<CsfdSearchResult>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];

            // Context for year/original-name/type: from this candidate up to the next one.
            var contextEnd = i + 1 < matches.Count ? matches[i + 1].Index : Math.Min(html.Length, match.Index + 1500);
            var context = html[match.Index..contextEnd];

            var yearMatch = YearRegex().Match(context);
            var nameMatch = SearchNameRegex().Match(context);
            var isFilm = !context.Contains("(seriál)", StringComparison.Ordinal)
                && !context.Contains("(pořad)", StringComparison.Ordinal)
                && !context.Contains("(epizoda)", StringComparison.Ordinal);

            results.Add(new CsfdSearchResult(
                match.Groups["id"].Value,
                WebUtility.HtmlDecode(match.Groups["title"].Value).Trim(),
                nameMatch.Success ? WebUtility.HtmlDecode(nameMatch.Groups["name"].Value).Trim() : null,
                yearMatch.Success ? int.Parse(yearMatch.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
                isFilm));
        }

        return results;
    }

    public async Task<CsfdRatingResult> GetRatingPercentAsync(string csfdId, CancellationToken cancellationToken)
    {
        var html = await GetPageAsync($"{BaseUrl}/film/{csfdId}/prehled/", cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return new CsfdRatingResult(false, null);
        }

        var match = RatingRegex().Match(html);
        if (match.Success)
        {
            var percent = int.Parse(match.Groups["percent"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return new CsfdRatingResult(true, Math.Min(percent, 100));
        }

        if (html.Contains("film-rating-average", StringComparison.Ordinal) || html.Contains("film-header-name", StringComparison.Ordinal))
        {
            // Film page loaded but has no numeric rating (too few votes: shown as "? %").
            return new CsfdRatingResult(true, null);
        }

        _logger.LogWarning("ČSFD page for {CsfdId} has unexpected structure", csfdId);
        return new CsfdRatingResult(false, null);
    }

    private async Task<string?> GetPageAsync(string url, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delayMs = Plugin.Instance?.Configuration.RequestDelayMs ?? 1500;
            var wait = _lastRequest + TimeSpan.FromMilliseconds(delayMs) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastRequest = DateTimeOffset.UtcNow;
            var html = await FetchStringAsync(url, cancellationToken).ConfigureAwait(false);
            if (html is not null && AnubisSolver.IsChallengePage(html))
            {
                html = await PassAnubisChallengeAsync(url, html, cancellationToken).ConfigureAwait(false);
            }

            return html;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> FetchStringAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ČSFD request {Url} returned {Status}", url, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ČSFD request {Url} failed", url);
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("ČSFD request {Url} timed out", url);
            return null;
        }
    }

    private async Task<string?> PassAnubisChallengeAsync(string originalUrl, string challengeHtml, CancellationToken cancellationToken)
    {
        var challenge = AnubisSolver.Parse(challengeHtml);
        if (challenge is null)
        {
            _logger.LogError("Failed to parse Anubis challenge from csfd.cz");
            return null;
        }

        var solution = AnubisSolver.Solve(challenge);
        if (solution is null)
        {
            _logger.LogError("Failed to solve Anubis proof-of-work (difficulty {Difficulty})", challenge.Difficulty);
            return null;
        }

        _logger.LogInformation("Solved Anubis challenge for csfd.cz (difficulty {Difficulty}, nonce {Nonce})", challenge.Difficulty, solution.Nonce);

        // A sub-100ms solve looks suspicious; report a plausible elapsed time.
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);

        var redir = new Uri(originalUrl).PathAndQuery;
        var passUrl = $"{BaseUrl}/.within.website/x/cmd/anubis/api/pass-challenge"
            + $"?id={Uri.EscapeDataString(challenge.Id)}"
            + $"&response={solution.Hash}"
            + $"&nonce={solution.Nonce}"
            + $"&redir={Uri.EscapeDataString(redir)}"
            + "&elapsedTime=300";

        // pass-challenge sets the auth cookie and redirects back to the original page.
        var html = await FetchStringAsync(passUrl, cancellationToken).ConfigureAwait(false);
        if (html is null || AnubisSolver.IsChallengePage(html))
        {
            _logger.LogError("Anubis challenge was not accepted by csfd.cz");
            return null;
        }

        return html;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _gate.Dispose();
    }
}
