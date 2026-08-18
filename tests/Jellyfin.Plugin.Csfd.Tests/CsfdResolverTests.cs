using System.Net;
using System.Text;
using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Csfd.Tests;

/// <summary>
/// End-to-end resolver tests over a fake HTTP layer serving synthetic ČSFD pages.
/// </summary>
public class CsfdResolverTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(string UrlContains, Func<HttpResponseMessage> Response)> Routes { get; } = [];

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            Requests.Add(url);
            foreach (var (needle, response) in Routes)
            {
                if (url.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(response());
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) });
        }
    }

    private static HttpResponseMessage Html(string html)
        => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage ServerError()
        => new(HttpStatusCode.InternalServerError) { Content = new StringContent(string.Empty) };

    private static string SearchEntry(string id, string title, int year, string? originalName = null, string? tag = null)
    {
        var tagHtml = tag is null ? string.Empty : $" <span class=\"info\">({tag})</span>";
        var entry = $"""
            <h3 class="film-title-nooverflow"><a href="/film/{id}/prehled/" class="film-title-name">{title}</a> <span class="film-title-info"><span class="info">({year})</span>{tagHtml}</span></h3>
            """;
        if (originalName is not null)
        {
            entry += $"\n<p class=\"search-name\">({originalName})</p>";
        }

        return entry;
    }

    private static string SearchPage(params string[] entries) => "<html><body>" + string.Join('\n', entries) + "</body></html>";

    private static string FilmPage(string name, string type, int year, double? rating, int? votes, params string[] altNames)
    {
        var namesHtml = altNames.Length == 0
            ? string.Empty
            : "<ul class=\"film-names\">" + string.Join(string.Empty, altNames.Select(n => $"<li ><img src=\"x.svg\" class=\"flag\"/>{n}\n</li>")) + "</ul>";
        var agg = rating.HasValue && votes.HasValue
            ? $",\"aggregateRating\":{{\"worstRating\":0,\"bestRating\":100,\"ratingValue\":{rating.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"ratingCount\":{votes.Value}}}"
            : string.Empty;
        var visibleRating = rating.HasValue ? $"<div class=\"film-rating-average\">\n{(int)Math.Round(rating.Value)}%\n</div>" : string.Empty;
        return $$"""
            <html><head><script type="application/ld+json">{"@context":"https://schema.org/","@type":"{{type}}","name":"{{name}}","dateCreated":"{{year}}"{{agg}}}</script></head>
            <body><div class="film-info"><div class="film-header-name"><h1>
            {{name}}
            </h1>{{namesHtml}}</div>{{visibleRating}}</body></html>
            """;
    }

    private static (CsfdResolver Resolver, FakeHandler Handler) CreateResolver()
    {
        var handler = new FakeHandler();
        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(Path.Combine(Path.GetTempPath(), "csfd-tests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.DataPath);
        var client = new CsfdClient(paths, NullLogger<CsfdClient>.Instance, handler) { FallbackDelayMs = 0 };
        return (new CsfdResolver(client, NullLogger<CsfdResolver>.Instance), handler);
    }

    [Fact]
    public async Task Strict_Match_Returns_Rating_From_Film_Page()
    {
        var (resolver, handler) = CreateResolver();
        handler.Routes.Add(("/hledat/", () => Html(SearchPage(SearchEntry("4570-pelisky", "Pelíšky", 1999)))));
        handler.Routes.Add(("/film/4570-pelisky/", () => Html(FilmPage("Pelíšky", "Movie", 1999, 91.2, 40000))));

        var resolution = await resolver.ResolveAsync("Pelíšky", null, 1999, series: false, CancellationToken.None);

        Assert.NotNull(resolution);
        Assert.Equal("4570-pelisky", resolution.Id);
        Assert.Equal(91, resolution.Percent);
        Assert.Equal(40000, resolution.Votes);
    }

    [Fact]
    public async Task Failed_Film_Page_Fetch_Yields_No_Match()
    {
        var (resolver, handler) = CreateResolver();
        handler.Routes.Add(("/hledat/", () => Html(SearchPage(SearchEntry("1-x", "Pelíšky", 1999)))));
        handler.Routes.Add(("/film/1-x/", ServerError));

        Assert.Null(await resolver.ResolveAsync("Pelíšky", null, 1999, series: false, CancellationToken.None));
    }

    [Fact]
    public async Task Movie_Resolution_Rejects_Series_Film_Page()
    {
        var (resolver, handler) = CreateResolver();
        handler.Routes.Add(("/hledat/", () => Html(SearchPage(SearchEntry("2-serial", "Pelíšky", 1999)))));
        handler.Routes.Add(("/film/2-serial/", () => Html(FilmPage("Pelíšky", "TVSeries", 1999, 80, 5000))));

        Assert.Null(await resolver.ResolveAsync("Pelíšky", null, 1999, series: false, CancellationToken.None));
    }

    [Fact]
    public async Task Verifies_Candidate_Without_OriginalName_Line_Via_Film_Page()
    {
        // ČSFD omits the "(original name)" line when the query equals it; the
        // film page's names list must recover the match.
        var (resolver, handler) = CreateResolver();
        handler.Routes.Add(("/hledat/", () => Html(SearchPage(SearchEntry("300761-toy-story-4", "Toy Story 4: Příběh hraček", 2019)))));
        handler.Routes.Add(("/film/300761-toy-story-4/", () => Html(FilmPage("Toy Story 4: Příběh hraček", "Movie", 2019, 78, 20000, "Toy Story 4"))));

        var resolution = await resolver.ResolveAsync("Toy Story 4", null, 2019, series: false, CancellationToken.None);

        Assert.Equal("300761-toy-story-4", resolution?.Id);
    }

    [Fact]
    public async Task Rejects_Candidate_Whose_Film_Page_Year_Contradicts()
    {
        var (resolver, handler) = CreateResolver();
        handler.Routes.Add(("/hledat/", () => Html(SearchPage(SearchEntry("3-y", "Duna", 2021)))));
        handler.Routes.Add(("/film/3-y/", () => Html(FilmPage("Duna", "Movie", 1984, 70, 9000))));

        Assert.Null(await resolver.ResolveAsync("Duna", null, 2021, series: false, CancellationToken.None));
    }

    [Fact]
    public async Task Long_Query_Falls_Back_To_PreColon_Search()
    {
        var (resolver, handler) = CreateResolver();
        var longTitle = "Pirates of the Caribbean: The Curse of the Black Pearl";
        // Full-title search finds nothing; the pre-colon query does.
        handler.Routes.Add(("/hledat/?q=" + longTitle, () => Html(SearchPage())));
        handler.Routes.Add(("/hledat/?q=Pirates of the Caribbean", () => Html(SearchPage(SearchEntry("10135-pirati", "Piráti z Karibiku: Prokletí Černé perly", 2003)))));
        handler.Routes.Add(("/film/10135-pirati/", () => Html(FilmPage("Piráti z Karibiku: Prokletí Černé perly", "Movie", 2003, 86, 60000, longTitle))));

        var resolution = await resolver.ResolveAsync(longTitle, null, 2003, series: false, CancellationToken.None);

        Assert.Equal("10135-pirati", resolution?.Id);
    }

    [Fact]
    public async Task Series_Resolution_Accepts_Porad_Typed_Search_Results()
    {
        var (resolver, handler) = CreateResolver();
        handler.Routes.Add(("/hledat/", () => Html(SearchPage(SearchEntry("301083-gn", "The Graham Norton Show", 2007, tag: "pořad")))));
        handler.Routes.Add(("/film/301083-gn/", () => Html(FilmPage("The Graham Norton Show", "TVSeries", 2007, 93.7, 2473))));

        var resolution = await resolver.ResolveAsync("The Graham Norton Show", null, 2007, series: true, CancellationToken.None);

        Assert.Equal("301083-gn", resolution?.Id);
        Assert.Equal(94, resolution?.Percent);
    }
}
