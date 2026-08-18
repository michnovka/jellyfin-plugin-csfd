using Jellyfin.Plugin.Csfd.Csfd;
using Xunit;

namespace Jellyfin.Plugin.Csfd.Tests;

/// <summary>
/// Parser regression tests against real captured ČSFD pages. When ČSFD changes
/// its markup, these fail before the library silently stops updating.
/// </summary>
public class CsfdParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    [Fact]
    public void ParseSearchResults_Finds_Candidates_With_Year_And_Type()
    {
        var results = CsfdClient.ParseSearchResults(Fixture("search-pelisky.html"));

        Assert.NotEmpty(results);
        var pelisky = results[0];
        Assert.Equal("4570-pelisky", pelisky.Id);
        Assert.Equal("Pelíšky", pelisky.Title);
        Assert.Equal(1999, pelisky.Year);
        Assert.True(pelisky.IsFilm);
        Assert.False(pelisky.IsSeries);

        // "Pelíšky slavných (pořad)" must not be a film, but IS series-eligible
        // (ČSFD types panel/talk shows as pořad).
        var porad = results.FirstOrDefault(r => r.Id.StartsWith("267763", StringComparison.Ordinal));
        Assert.NotNull(porad);
        Assert.False(porad.IsFilm);
        Assert.True(porad.IsSeries);
    }

    [Fact]
    public void ParseFilmDetails_Reads_Rating_Votes_Names_And_Type()
    {
        var details = CsfdClient.ParseFilmDetails(Fixture("film-shawshank.html"));

        Assert.True(details.Success);
        Assert.Equal(95, details.Percent);
        Assert.True(details.Votes > 100_000);
        Assert.Equal(1994, details.Year);
        Assert.False(details.IsSeries);
        Assert.Contains("Vykoupení z věznice Shawshank", details.Names);
        Assert.Contains("The Shawshank Redemption", details.Names);
    }

    [Fact]
    public void ParseFilmDetails_Rounds_Fractional_JsonLd_RatingValue()
    {
        // ČSFD's JSON-LD ratingValue is fractional; without the visible rating
        // element it must still parse (regression: TryGetInt32 rejected it).
        const string html = """
            <html><head><script type="application/ld+json">{"@context":"https://schema.org/","@type":"Movie","name":"Testovací film","dateCreated":"2001","aggregateRating":{"worstRating":0,"bestRating":100,"ratingValue":77.4921,"ratingCount":523}}</script></head><body></body></html>
            """;

        var details = CsfdClient.ParseFilmDetails(html);

        Assert.True(details.Success);
        Assert.Equal(77, details.Percent);
        Assert.Equal(523, details.Votes);
        Assert.Equal(2001, details.Year);
        Assert.Contains("Testovací film", details.Names);
    }

    [Fact]
    public void ParseFilmDetails_Fails_On_Challenge_Page()
    {
        var details = CsfdClient.ParseFilmDetails(Fixture("anubis-challenge.html"));

        Assert.False(details.Success);
    }

    [Fact]
    public void AnubisSolver_Detects_Parses_And_Solves_Challenge()
    {
        var html = Fixture("anubis-challenge.html");

        Assert.True(AnubisSolver.IsChallengePage(html));
        Assert.False(AnubisSolver.IsChallengePage(Fixture("film-shawshank.html")));

        var challenge = AnubisSolver.Parse(html);
        Assert.NotNull(challenge);
        Assert.Equal(1, challenge.Difficulty);
        Assert.NotEmpty(challenge.RandomData);
        Assert.NotEmpty(challenge.Id);

        var solution = AnubisSolver.Solve(challenge);
        Assert.NotNull(solution);
        Assert.StartsWith(new string('0', challenge.Difficulty), solution.Hash, StringComparison.Ordinal);
    }
}
