using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>
/// Custom metadata provider that runs during metadata refresh, resolves the
/// movie on ČSFD (stored as the "Csfd" provider id) and writes the ČSFD
/// percentage into CriticRating.
/// </summary>
public class CsfdMovieProvider : ICustomMetadataProvider<Movie>, IHasOrder
{
    private readonly CsfdUpdater _updater;

    public CsfdMovieProvider(CsfdUpdater updater)
    {
        _updater = updater;
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

        var result = await _updater.UpdateItemAsync(item, series: false, config, cancellationToken).ConfigureAwait(false);
        return result.Changed ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
    }
}
