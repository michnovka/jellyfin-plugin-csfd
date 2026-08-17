using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Picks the best ČSFD search result for a movie title + production year.
/// </summary>
public static class CsfdMatcher
{
    public static CsfdSearchResult? FindBestMatch(IReadOnlyList<CsfdSearchResult> candidates, IEnumerable<string> titles, int? year)
    {
        var normalizedTitles = titles
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(Normalize)
            .Distinct()
            .ToList();

        CsfdSearchResult? best = null;
        var bestScore = -1;

        foreach (var candidate in candidates.Where(c => c.IsFilm))
        {
            // Title match (Czech title or ČSFD's original name) is mandatory;
            // a year match alone is not evidence enough.
            var titleMatches = normalizedTitles.Contains(Normalize(candidate.Title))
                || (candidate.OriginalName is not null && normalizedTitles.Contains(Normalize(candidate.OriginalName)));
            if (!titleMatches)
            {
                continue;
            }

            var score = 1;
            if (year.HasValue && candidate.Year.HasValue)
            {
                var diff = Math.Abs(year.Value - candidate.Year.Value);
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
    /// Uses compatibility decomposition so superscripts/fractions become plain digits
    /// ("The Accountant²" → "the accountant 2").</summary>
    public static string Normalize(string value)
    {
        // Pre-expand superscripts/vulgar fractions (², ⅓ …) with spaces so they
        // become standalone digits ("33⅓" → "33 1 3", not "331 3").
        var expanded = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.OtherNumber)
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

        return sb.ToString().Trim();
    }
}
