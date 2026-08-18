using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Picks the best ČSFD search result for a title + production year.
/// </summary>
public static class CsfdMatcher
{
    private static readonly HashSet<string> Stopwords = ["the", "a", "an", "and"];

    public static CsfdSearchResult? FindBestMatch(IReadOnlyList<CsfdSearchResult> candidates, IEnumerable<string> titles, int? year, bool series = false)
    {
        var titleList = titles.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var exactTitles = titleList.Select(Normalize).Distinct().ToList();
        var looseTitles = titleList.Select(NormalizeAggressive).Distinct().ToList();

        CsfdSearchResult? best = null;
        var bestScore = -1;

        foreach (var candidate in candidates.Where(c => series ? c.IsSeries : c.IsFilm))
        {
            var candidateNames = new[] { candidate.Title, candidate.OriginalName }
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            // Title match is mandatory; a year match alone is not evidence enough.
            // An exact-normalized match strongly outranks the article/conjunction-
            // tolerant one, and matching MORE of the item's titles (localized AND
            // original) outranks matching fewer — so for an item known as
            // "Otec" / "The Father", the candidate whose original is "The Father"
            // beats the same-year candidate whose original is "Father".
            var exactCount = exactTitles.Count(t => candidateNames.Any(n => Normalize(n) == t));
            var loose = exactCount > 0 || candidateNames.Any(n => looseTitles.Contains(NormalizeAggressive(n)));
            if (!loose)
            {
                continue;
            }

            var score = exactCount > 0 ? 3 + exactCount : 1;
            if (year.HasValue && candidate.Year.HasValue)
            {
                var diff = Math.Abs((long)year.Value - candidate.Year.Value);
                if (diff > 1)
                {
                    continue; // same title, wrong year (remakes) is disqualifying
                }

                score += diff == 0 ? 2 : 1;
            }

            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Lowercase, strip diacritics and punctuation, collapse whitespace.
    /// "&amp;" becomes the word "and"; compatibility decomposition turns
    /// superscripts/fractions into plain digits ("The Accountant²" → "the accountant 2").</summary>
    public static string Normalize(string value) => NormalizeCore(value, dropStopwords: false);

    /// <summary>Like <see cref="Normalize"/> but additionally drops article/conjunction
    /// stopwords, absorbing "The"/"A"/"and" differences. Lossy — use only where a
    /// year gate or page verification constrains the candidates.</summary>
    public static string NormalizeAggressive(string value) => NormalizeCore(value, dropStopwords: true);

    private static string NormalizeCore(string value, bool dropStopwords)
    {
        // Pre-expand superscripts/vulgar fractions (², ⅓ …) with spaces so they
        // become standalone digits ("33⅓" → "33 1 3", not "331 3"), and treat
        // "&" as the word "and".
        var expanded = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            if (ch == '&')
            {
                expanded.Append(" and ");
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.OtherNumber)
            {
                expanded.Append(' ').Append(ch.ToString().Normalize(NormalizationForm.FormKD)).Append(' ');
            }
            else
            {
                expanded.Append(ch);
            }
        }

        var formD = expanded.ToString().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(formD.Length);
        var lastWasSpace = true;
        foreach (var c in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !dropStopwords || !Stopwords.Contains(w));
        return string.Join(' ', words);
    }
}
