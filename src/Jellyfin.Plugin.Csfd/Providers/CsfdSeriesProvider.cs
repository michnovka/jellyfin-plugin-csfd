using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>
/// Same as <see cref="CsfdMovieProvider"/>, for TV series (ČSFD "seriály").
/// </summary>
public class CsfdSeriesProvider : ICustomMetadataProvider<Series>, IHasOrder
{
    private readonly CsfdUpdater _updater;

    public CsfdSeriesProvider(CsfdUpdater updater)
    {
        _updater = updater;
    }

    public string Name => "ČSFD Rating";

    public int Order => 100;

    public async Task<ItemUpdateType> FetchAsync(Series item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.SetCriticRating)
        {
            return ItemUpdateType.None;
        }

        var result = await _updater.UpdateItemAsync(item, series: true, config, cancellationToken).ConfigureAwait(false);
        return result.Changed ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
    }
}
