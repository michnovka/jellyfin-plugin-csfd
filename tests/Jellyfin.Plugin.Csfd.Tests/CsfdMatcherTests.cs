using Jellyfin.Plugin.Csfd.Csfd;
using Xunit;

namespace Jellyfin.Plugin.Csfd.Tests;

public class CsfdMatcherTests
{
    private static CsfdSearchResult Film(string id, string title, int? year, string? originalName = null)
        => new(id, title, originalName, year, IsFilm: true, IsSeries: false);

    private static CsfdSearchResult Series(string id, string title, int? year, string? originalName = null)
        => new(id, title, originalName, year, IsFilm: false, IsSeries: true);

    private static CsfdSearchResult Show(string id, string title, int? year)
        => new(id, title, null, year, IsFilm: false, IsSeries: false);

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
    public void Matches_Ampersand_As_And()
    {
        var candidates = new[] { Film("269425-tucker", "Tucker & Dale vs. Zlo", 2010, originalName: "Tucker & Dale vs Evil") };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Tucker and Dale vs. Evil"], 2010);
        Assert.Equal("269425-tucker", match?.Id);
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
    public void Movie_Search_Ignores_Series_And_Shows()
    {
        var candidates = new[]
        {
            Series("1-serial", "Pelíšky", 1999),
            Show("2-porad", "Pelíšky", 1999),
            Film("4570-pelisky", "Pelíšky", 1999),
        };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Pelíšky"], 1999);
        Assert.Equal("4570-pelisky", match?.Id);
    }

    [Fact]
    public void Series_Search_Ignores_Films()
    {
        var candidates = new[]
        {
            Film("1-film", "Vyprávěj", 2009),
            Series("2-serial", "Vyprávěj", 2009),
        };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Vyprávěj"], 2009, series: true);
        Assert.Equal("2-serial", match?.Id);
    }

    [Fact]
    public void Matches_On_Title_Alone_When_Item_Year_Unknown()
    {
        var candidates = new[] { Film("4570-pelisky", "Pelíšky", 1999) };

        var match = CsfdMatcher.FindBestMatch(candidates, ["Pelíšky"], null);
        Assert.Equal("4570-pelisky", match?.Id);
    }

    [Fact]
    public void Prefers_Exact_Title_Over_Stopword_Tolerant_Match()
    {
        // "Father" and "The Father" are different 2020 films; the aggressive
        // normalization collides them, the exact one must win.
        var candidates = new[]
        {
            Film("1-otec", "Otec", 2020, originalName: "Father"),
            Film("2-otec", "Otec", 2020, originalName: "The Father"),
        };

        Assert.Equal("2-otec", CsfdMatcher.FindBestMatch(candidates, ["The Father"], 2020)?.Id);
        Assert.Equal("1-otec", CsfdMatcher.FindBestMatch(candidates, ["Father"], 2020)?.Id);
    }

    [Theory]
    [InlineData("Pelíšky", "pelisky")]
    [InlineData("The Shawshank Redemption", "the shawshank redemption")]
    [InlineData("Vesničko má, středisková!", "vesnicko ma strediskova")]
    [InlineData("  Samotáři  ", "samotari")]
    [InlineData("The Accountant²", "the accountant 2")]
    [InlineData("Naked Gun 33⅓: The Final Insult", "naked gun 33 1 3 the final insult")]
    [InlineData("Tucker & Dale vs Evil", "tucker and dale vs evil")]
    [InlineData("Tucker and Dale vs. Evil", "tucker and dale vs evil")]
    public void Normalize_Strips_Diacritics_And_Punctuation(string input, string expected)
    {
        Assert.Equal(expected, CsfdMatcher.Normalize(input));
    }

    [Theory]
    [InlineData("The Accountant²", "accountant 2")]
    [InlineData("Naked Gun 33⅓: The Final Insult", "naked gun 33 1 3 final insult")]
    [InlineData("Tucker and Dale vs. Evil", "tucker dale vs evil")]
    public void NormalizeAggressive_Additionally_Drops_Stopwords(string input, string expected)
    {
        Assert.Equal(expected, CsfdMatcher.NormalizeAggressive(input));
    }
}
