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
    private readonly CsfdResolver _resolver;
    private readonly ILogger<CsfdMovieProvider> _logger;

    public CsfdMovieProvider(CsfdClient client, CsfdResolver resolver, ILogger<CsfdMovieProvider> logger)
    {
        _client = client;
        _resolver = resolver;
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

            csfdId = await _resolver.ResolveAsync(item.Name, item.OriginalTitle, item.ProductionYear, cancellationToken).ConfigureAwait(false);
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
}
