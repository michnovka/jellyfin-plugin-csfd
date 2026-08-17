using Jellyfin.Plugin.Csfd.Csfd;
using Xunit;

namespace Jellyfin.Plugin.Csfd.Tests;

public class CsfdMatcherTests
{
    private static CsfdSearchResult Film(string id, string title, int? year, string? originalName = null, bool isFilm = true)
        => new(id, title, originalName, year, isFilm);

    [Fact]
    public void Matches_Czech_Title_And_Year()
    {
        var candidates = new[]
        {
            Film("1-jine", "Jiný film", 1999),
            Film("4570-pelisky", "Pelíšky", 1999),
        };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Pelíšky"], 1999);
        Assert.Equal("4570-pelisky", match?.Id);
    }

    [Fact]
    public void Matches_Ignoring_Diacritics_And_Case()
    {
        var candidates = new[] { Film("7064-samotari", "Samotáři", 2000) };

        var match = CsfdMatcher.FindBestMatch(candidates, ["SAMOTARI"], 2000);
        Assert.Equal("7064-samotari", match?.Id);
    }

    [Fact]
    public void Matches_Via_Original_Name()
    {
        // Jellyfin knows the English title; ČSFD lists the Czech title with the
        // original name alongside.
        var candidates = new[] { Film("9499-matrix", "Matrix", 1999, originalName: "The Matrix") };

        var match = CsfdMatcher.FindBestMatch(candidates, ["The Matrix"], 1999);
        Assert.Equal("9499-matrix", match?.Id);
    }

    [Fact]
    public void Disambiguates_Remakes_By_Year()
    {
        var candidates = new[]
        {
            Film("6648-duna", "Duna", 1984, originalName: "Dune"),
            Film("270527-duna", "Duna", 2021, originalName: "Dune"),
        };

        Assert.Equal("270527-duna", CsfdMatcher.FindBestMatch(candidates, ["Dune"], 2021)?.Id);
        Assert.Equal("6648-duna", CsfdMatcher.FindBestMatch(candidates, ["Dune"], 1984)?.Id);
    }

    [Fact]
    public void Tolerates_One_Year_Difference()
    {
        var candidates = new[] { Film("1-a", "Nějaký film", 2001) };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Nějaký film"], 2000);
        Assert.Equal("1-a", match?.Id);
    }

    [Fact]
    public void Rejects_Same_Title_Wrong_Year()
    {
        var candidates = new[] { Film("1-a", "Duna", 1984) };

        Assert.Null(CsfdMatcher.FindBestMatch(candidates, ["Duna"], 2021));
    }

    [Fact]
    public void Rejects_Year_Match_Without_Title_Match()
    {
        // A year hit alone must never be accepted — this caused false positives.
        var candidates = new[] { Film("1-a", "Úplně jiný název", 2015) };

        Assert.Null(CsfdMatcher.FindBestMatch(candidates, ["Neexistující film XYZ"], 2015));
    }

    [Fact]
    public void Prefers_Exact_Year_Over_Adjacent()
    {
        var candidates = new[]
        {
            Film("1-a", "Stejný název", 2000),
            Film("2-b", "Stejný název", 1999),
        };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Stejný název"], 1999);
        Assert.Equal("2-b", match?.Id);
    }

    [Fact]
    public void Ignores_Series_And_Shows()
    {
        var candidates = new[]
        {
            Film("1-serial", "Pelíšky", 1999, isFilm: false),
            Film("4570-pelisky", "Pelíšky", 1999),
        };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Pelíšky"], 1999);
        Assert.Equal("4570-pelisky", match?.Id);
    }

    [Fact]
    public void Matches_On_Title_Alone_When_Item_Year_Unknown()
    {
        var candidates = new[] { Film("4570-pelisky", "Pelíšky", 1999) };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Pelíšky"], null);
        Assert.Equal("4570-pelisky", match?.Id);
    }

    [Theory]
    [InlineData("Pelíšky", "pelisky")]
    [InlineData("The Shawshank Redemption", "the shawshank redemption")]
    [InlineData("Vesničko má, středisková!", "vesnicko ma strediskova")]
    [InlineData("  Samotáři  ", "samotari")]
    public void Normalize_Strips_Diacritics_And_Punctuation(string input, string expected)
    {
        Assert.Equal(expected, CsfdMatcher.Normalize(input));
    }
}
