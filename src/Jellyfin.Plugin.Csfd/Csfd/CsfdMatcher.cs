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

    /// <summary>Lowercase, strip diacritics and punctuation, collapse whitespace.</summary>
    public static string Normalize(string value)
    {
        var formD = value.Normalize(NormalizationForm.FormD);
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
