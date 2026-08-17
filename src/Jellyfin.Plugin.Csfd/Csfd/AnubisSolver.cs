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
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("challenge", out var challenge) || challenge.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = challenge.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : null;
            var randomData = challenge.TryGetProperty("randomData", out var rdProp) && rdProp.ValueKind == JsonValueKind.String ? rdProp.GetString() : null;
            var difficulty = challenge.TryGetProperty("difficulty", out var dProp) && dProp.TryGetInt32(out var d) ? d : -1;

            // The page content is untrusted: bound everything before burning CPU on it.
            if (string.IsNullOrEmpty(id) || id.Length > 128
                || string.IsNullOrEmpty(randomData) || randomData.Length > 4096
                || difficulty is < 0 or > 8)
            {
                return null;
            }

            return new Challenge(id, randomData, difficulty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Solution? Solve(Challenge challenge, CancellationToken cancellationToken = default, long maxIterations = 5_000_000)
    {
        var randomData = Encoding.UTF8.GetBytes(challenge.RandomData);
        var prefix = new string('0', challenge.Difficulty);

        for (long nonce = 0; nonce < maxIterations; nonce++)
        {
            if ((nonce & 0xFFFF) == 0 && cancellationToken.IsCancellationRequested)
            {
                return null;
            }

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
