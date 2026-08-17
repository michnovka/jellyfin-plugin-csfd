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
        var bestScore = 0;

        foreach (var candidate in candidates.Where(c => c.IsFilm))
        {
            var score = 0;
            var titleMatches = normalizedTitles.Contains(Normalize(candidate.Title))
                || (candidate.OriginalName is not null && normalizedTitles.Contains(Normalize(candidate.OriginalName)));
            if (titleMatches)
            {
                score += 2;
            }

            if (year.HasValue && candidate.Year.HasValue)
            {
                var diff = Math.Abs(year.Value - candidate.Year.Value);
                if (diff == 0)
                {
                    score += 2;
                }
                else if (diff == 1)
                {
                    score += 1;
                }
                else
                {
                    continue; // wrong year is disqualifying
                }
            }

            // Require at least title match, or exact year when the title differs
            // (e.g. localized Jellyfin name vs. ČSFD Czech name).
            var threshold = year.HasValue ? 2 : 2;
            if (score >= threshold && score > bestScore)
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
