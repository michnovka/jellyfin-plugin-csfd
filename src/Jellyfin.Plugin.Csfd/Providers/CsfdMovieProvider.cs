using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>
/// Custom metadata provider that runs during metadata refresh, resolves the
/// movie on ČSFD (stored as the "Csfd" provider id) and writes the ČSFD
/// percentage into CriticRating.
/// </summary>
public class CsfdMovieProvider : ICustomMetadataProvider<Movie>, IHasOrder
{
    private readonly CsfdClient _client;
    private readonly ILogger<CsfdMovieProvider> _logger;

    public CsfdMovieProvider(CsfdClient client, ILogger<CsfdMovieProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => "ČSFD Rating";

    // Run after the regular remote providers (TMDb etc.).
    public int Order => 100;

    public async Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.SetCriticRating)
        {
            return ItemUpdateType.None;
        }

        var changed = false;
        var csfdId = item.GetProviderId("Csfd");
        var hadCsfdId = !string.IsNullOrEmpty(csfdId);
        if (!hadCsfdId)
        {
            // A rating set by someone else and overwriting disabled: leave the item alone.
            if (!config.OverwriteExistingCriticRating && item.CriticRating.HasValue)
            {
                return ItemUpdateType.None;
            }

            csfdId = await ResolveCsfdIdAsync(item, cancellationToken).ConfigureAwait(false);
            if (csfdId is null)
            {
                return ItemUpdateType.None;
            }

            item.SetProviderId("Csfd", csfdId);
            changed = true;
        }

        var rating = await _client.GetRatingPercentAsync(csfdId!, cancellationToken).ConfigureAwait(false);
        if (rating.Success && rating.Percent.HasValue && item.CriticRating != rating.Percent.Value)
        {
            _logger.LogInformation("ČSFD rating for {Name}: {Percent}%", item.Name, rating.Percent.Value);
            item.CriticRating = rating.Percent.Value;
            changed = true;
        }

        return changed ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
    }

    private async Task<string?> ResolveCsfdIdAsync(Movie item, CancellationToken cancellationToken)
    {
        var titles = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            titles.Add(item.Name);
        }

        if (!string.IsNullOrWhiteSpace(item.OriginalTitle))
        {
            titles.Add(item.OriginalTitle);
        }

        foreach (var query in titles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var results = await _client.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var match = CsfdMatcher.FindBestMatch(results, titles, item.ProductionYear);
            if (match is not null)
            {
                _logger.LogInformation("Matched {Name} ({Year}) to ČSFD {CsfdId}", item.Name, item.ProductionYear, match.Id);
                return match.Id;
            }
        }

        _logger.LogInformation("No ČSFD match for {Name} ({Year})", item.Name, item.ProductionYear);
        return null;
    }
}
