using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

public sealed record CsfdSearchResult(string Id, string Title, string? OriginalName, int? Year, bool IsFilm, bool IsSeries);

/// <summary>Everything we can read from one film page: all titles (Czech + originals),
/// year, rating percent, vote count and whether it is a TV series.</summary>
public sealed record CsfdFilmDetails(bool Success, IReadOnlyList<string> Names, int? Year, int? Percent, int? Votes, bool IsSeries);

/// <summary>
/// Scraping client for csfd.cz. Owns a cookie jar so the Anubis anti-bot cookie
/// survives between requests (persisted to disk across restarts), serializes all
/// traffic through a rate limiter and backs off on throttling responses.
/// Registered as a singleton.
/// </summary>
public sealed partial class CsfdClient : IDisposable
{
    private const string BaseUrl = "https://www.csfd.cz";
    private const string Host = "www.csfd.cz";
    private const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0";
    private const int MaxResponseBytes = 5 * 1024 * 1024;
    private const int MaxRedirects = 5;
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookies;
    private readonly ILogger<CsfdClient> _logger;
    private readonly string _cookieFile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;

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
    [GeneratedRegex(@"film-rating-average[^>]*>\s*(?<percent>\d{1,3})\s*%", RegexOptions.Singleline, 2000)]
    private static partial Regex RatingRegex();

    // Czech title on the film page: <div class="film-header-name"> <h1> Title
    [GeneratedRegex("""film-header-name">\s*<h1>\s*(?<title>[^<]+)""", RegexOptions.Singleline, 2000)]
    private static partial Regex FilmHeaderRegex();

    // Alternate names: <ul class="film-names"><li><img title="USA".../>Name ...
    [GeneratedRegex("""<ul class="film-names">(?<block>.*?)</ul>""", RegexOptions.Singleline, 2000)]
    private static partial Regex FilmNamesBlockRegex();

    [GeneratedRegex("""<li[^>]*>\s*(?:<img[^>]*/?>)?\s*(?<name>[^<]+)""", RegexOptions.Singleline, 2000)]
    private static partial Regex FilmNamesItemRegex();

    [GeneratedRegex("""<script type="application/ld\+json">(?<json>.*?)</script>""", RegexOptions.Singleline, 2000)]
    private static partial Regex JsonLdRegex();

    public CsfdClient(IApplicationPaths applicationPaths, ILogger<CsfdClient> logger)
    {
        _logger = logger;
        _cookieFile = Path.Combine(applicationPaths.DataPath, "csfd-anubis-cookies.json");
        _cookies = new CookieContainer();
        LoadCookies();

        // Redirects are followed manually so they can be pinned to csfd.cz over
        // HTTPS — the server must not be able to point Jellyfin at internal hosts.
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            AllowAutoRedirect = false,
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
        return html is null ? [] : ParseSearchResults(html);
    }

    [GeneratedRegex(@"^\d+(-[a-z0-9-]+)?$")]
    private static partial Regex CsfdIdRegex();

    public async Task<CsfdFilmDetails> GetFilmDetailsAsync(string csfdId, CancellationToken cancellationToken)
    {
        // Ids can come from the metadata editor; refuse anything that is not a
        // plain ČSFD slug before building a URL from it.
        if (!CsfdIdRegex().IsMatch(csfdId))
        {
            _logger.LogWarning("Ignoring malformed ČSFD id {CsfdId}", csfdId);
            return new CsfdFilmDetails(false, [], null, null, null, false);
        }

        var html = await GetPageAsync($"{BaseUrl}/film/{csfdId}/prehled/", cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return new CsfdFilmDetails(false, [], null, null, null, false);
        }

        var details = ParseFilmDetails(html);
        if (!details.Success)
        {
            _logger.LogWarning("ČSFD page for {CsfdId} has unexpected structure", csfdId);
        }

        return details;
    }

    internal static IReadOnlyList<CsfdSearchResult> ParseSearchResults(string html)
    {
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
            var isSeries = context.Contains("seriál)", StringComparison.Ordinal);
            var isFilm = !isSeries
                && !context.Contains("(pořad)", StringComparison.Ordinal)
                && !context.Contains("(epizoda)", StringComparison.Ordinal);

            results.Add(new CsfdSearchResult(
                match.Groups["id"].Value,
                WebUtility.HtmlDecode(match.Groups["title"].Value).Trim(),
                nameMatch.Success ? WebUtility.HtmlDecode(nameMatch.Groups["name"].Value).Trim() : null,
                yearMatch.Success ? int.Parse(yearMatch.Groups["year"].Value, CultureInfo.InvariantCulture) : null,
                isFilm,
                isSeries));
        }

        return results;
    }

    internal static CsfdFilmDetails ParseFilmDetails(string html)
    {
        try
        {
            return ParseFilmDetailsCore(html);
        }
        catch (RegexMatchTimeoutException)
        {
            return new CsfdFilmDetails(false, [], null, null, null, false);
        }
    }

    private static CsfdFilmDetails ParseFilmDetailsCore(string html)
    {
        var names = new List<string>();
        int? year = null;
        int? votes = null;
        int? percent = null;
        var isSeries = false;
        var recognized = false;

        var header = FilmHeaderRegex().Match(html);
        if (header.Success)
        {
            names.Add(WebUtility.HtmlDecode(header.Groups["title"].Value).Trim());
            recognized = true;
        }

        var block = FilmNamesBlockRegex().Match(html);
        if (block.Success)
        {
            foreach (Match item in FilmNamesItemRegex().Matches(block.Groups["block"].Value))
            {
                var value = WebUtility.HtmlDecode(item.Groups["name"].Value).Trim();
                if (value.Length > 0)
                {
                    names.Add(value);
                }
            }
        }

        foreach (Match script in JsonLdRegex().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(script.Groups["json"].Value);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("@type", out var type)
                    || type.GetString() is not ("Movie" or "TVSeries"))
                {
                    continue;
                }

                recognized = true;
                isSeries = type.GetString() == "TVSeries";
                if (root.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n)
                {
                    names.Add(n);
                }

                if (root.TryGetProperty("dateCreated", out var created)
                    && created.GetString() is { } dateStr
                    && Regex.Match(dateStr, @"(?:19|20)\d{2}") is { Success: true } ym)
                {
                    year = int.Parse(ym.Value, CultureInfo.InvariantCulture);
                }

                if (root.TryGetProperty("aggregateRating", out var agg))
                {
                    if (agg.TryGetProperty("ratingCount", out var count) && count.TryGetInt32(out var v))
                    {
                        votes = v;
                    }

                    // ratingValue is fractional (e.g. 95.365…) on a 0–100 scale.
                    if (agg.TryGetProperty("ratingValue", out var val) && val.TryGetDouble(out var p))
                    {
                        percent = Math.Clamp((int)Math.Round(p), 0, 100);
                    }
                }

                break;
            }
            catch (JsonException)
            {
                // ignore malformed blocks
            }
        }

        // The visible rating element is authoritative; JSON-LD is the fallback.
        var ratingMatch = RatingRegex().Match(html);
        if (ratingMatch.Success)
        {
            percent = Math.Min(int.Parse(ratingMatch.Groups["percent"].Value, CultureInfo.InvariantCulture), 100);
        }

        return new CsfdFilmDetails(recognized, names.Distinct().ToList(), year, percent, votes, isSeries);
    }

    private async Task<string?> GetPageAsync(string url, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Checked under the gate so queued callers see a cooldown set by the
            // caller that established it.
            if (DateTimeOffset.UtcNow < _cooldownUntil)
            {
                _logger.LogDebug("ČSFD client in cooldown, skipping {Url}", url);
                return null;
            }

            var delayMs = Math.Clamp(Plugin.Instance?.Configuration.RequestDelayMs ?? 1500, 250, 60_000);
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
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await SendFollowingRedirectsAsync(url, cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    return null;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt >= 2)
                    {
                        _cooldownUntil = DateTimeOffset.UtcNow + CooldownDuration;
                        _logger.LogWarning("ČSFD keeps returning {Status}; backing off for {Cooldown}", (int)response.StatusCode, CooldownDuration);
                        return null;
                    }

                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt == 0 ? 20 : 60);
                    if (delay < TimeSpan.Zero || delay > MaxRetryAfter)
                    {
                        delay = MaxRetryAfter;
                    }

                    _logger.LogInformation("ČSFD returned {Status}, retrying in {Delay}", (int)response.StatusCode, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ČSFD request {Url} returned {Status}", url, (int)response.StatusCode);
                    return null;
                }

                return await ReadBoundedAsync(response, url, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < 2)
            {
                _logger.LogInformation(ex, "ČSFD request {Url} failed, retrying", url);
                await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 20 : 60), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _cooldownUntil = DateTimeOffset.UtcNow + CooldownDuration;
                _logger.LogWarning(ex, "ČSFD request {Url} failed repeatedly; backing off for {Cooldown}", url, CooldownDuration);
                return null;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("ČSFD request {Url} timed out", url);
                return null;
            }
        }
    }

    /// <summary>Follows redirects manually, only over HTTPS to www.csfd.cz.</summary>
    private async Task<HttpResponseMessage?> SendFollowingRedirectsAsync(string url, CancellationToken cancellationToken)
    {
        var current = new Uri(url);
        for (var redirects = 0; ; redirects++)
        {
            var response = await _httpClient.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirects >= MaxRedirects)
            {
                _logger.LogWarning("ČSFD redirect chain for {Url} could not be followed", url);
                return null;
            }

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (next.Scheme != Uri.UriSchemeHttps || !string.Equals(next.Host, Host, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Refusing ČSFD redirect to {Target}", next);
                return null;
            }

            current = next;
        }
    }

    private async Task<string?> ReadBoundedAsync(HttpResponseMessage response, string url, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            _logger.LogWarning("ČSFD response for {Url} exceeds size limit", url);
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffered = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffered.Length + read > MaxResponseBytes)
                {
                    _logger.LogWarning("ČSFD response for {Url} exceeds size limit", url);
                    return null;
                }

                buffered.Write(buffer, 0, read);
            }

            return System.Text.Encoding.UTF8.GetString(buffered.GetBuffer(), 0, (int)buffered.Length);
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

        var solution = AnubisSolver.Solve(challenge, cancellationToken);
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

        SaveCookies();
        return html;
    }

    private sealed record StoredCookie(string Name, string Value, string Path, string Domain, DateTime Expires);

    private void LoadCookies()
    {
        try
        {
            if (!File.Exists(_cookieFile) || new FileInfo(_cookieFile).Length > 64 * 1024)
            {
                return;
            }

            var stored = JsonSerializer.Deserialize<List<StoredCookie>>(File.ReadAllText(_cookieFile)) ?? [];
            foreach (var c in stored.Where(c => c.Expires == DateTime.MinValue || c.Expires > DateTime.UtcNow))
            {
                try
                {
                    if (string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Domain))
                    {
                        continue;
                    }

                    _cookies.Add(new Cookie(c.Name, c.Value ?? string.Empty, c.Path ?? "/", c.Domain) { Expires = c.Expires });
                }
                catch (CookieException)
                {
                    // skip malformed records individually
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or CookieException)
        {
            _logger.LogDebug(ex, "Could not load persisted ČSFD cookies");
        }
    }

    private void SaveCookies()
    {
        try
        {
            var cookies = _cookies.GetCookies(new Uri(BaseUrl))
                .Select(c => new StoredCookie(c.Name, c.Value, c.Path, c.Domain, c.Expires))
                .ToList();
            var tmp = _cookieFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cookies));
            File.Move(tmp, _cookieFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not persist ČSFD cookies");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _gate.Dispose();
    }
}
