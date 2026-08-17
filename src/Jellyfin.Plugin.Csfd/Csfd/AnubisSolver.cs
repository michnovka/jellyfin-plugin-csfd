using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Solves the Anubis (BotStopper) proof-of-work challenge that protects csfd.cz.
/// The challenge page embeds a JSON blob; the proof is a nonce N such that
/// hex(SHA256(randomData + N)) starts with <c>difficulty</c> zero characters.
/// </summary>
internal static partial class AnubisSolver
{
    [GeneratedRegex("""<script id="anubis_challenge" type="application/json">(.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex ChallengeJsonRegex();

    internal sealed record Challenge(string Id, string RandomData, int Difficulty);

    internal sealed record Solution(long Nonce, string Hash);

    public static bool IsChallengePage(string html)
        => html.Contains("anubis_challenge", StringComparison.Ordinal);

    public static Challenge? Parse(string html)
    {
        var match = ChallengeJsonRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            var challenge = doc.RootElement.GetProperty("challenge");
            return new Challenge(
                challenge.GetProperty("id").GetString() ?? string.Empty,
                challenge.GetProperty("randomData").GetString() ?? string.Empty,
                challenge.GetProperty("difficulty").GetInt32());
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Solution? Solve(Challenge challenge, long maxIterations = 5_000_000)
    {
        var randomData = Encoding.UTF8.GetBytes(challenge.RandomData);
        var prefix = new string('0', challenge.Difficulty);

        for (long nonce = 0; nonce < maxIterations; nonce++)
        {
            var input = new byte[randomData.Length + 20];
            randomData.CopyTo(input, 0);
            var written = Encoding.UTF8.GetBytes(nonce.ToString(System.Globalization.CultureInfo.InvariantCulture), input.AsSpan(randomData.Length));
            var hash = Convert.ToHexStringLower(SHA256.HashData(input.AsSpan(0, randomData.Length + written)));
            if (hash.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new Solution(nonce, hash);
            }
        }

        return null;
    }
}
