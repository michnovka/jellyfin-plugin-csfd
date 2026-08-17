using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.ScheduledTasks;

/// <summary>
/// Walks the movie and series library: resolves missing ČSFD ids and refreshes
/// CriticRating from ČSFD. Items refreshed within the configured interval are
/// skipped, so reruns are cheap. This is the backfill path for libraries that
/// existed before the plugin was installed, and keeps ratings current.
/// </summary>
public class CsfdRefreshTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly CsfdUpdater _updater;
    private readonly CsfdStateStore _stateStore;
    private readonly ILogger<CsfdRefreshTask> _logger;

    public CsfdRefreshTask(ILibraryManager libraryManager, CsfdUpdater updater, CsfdStateStore stateStore, ILogger<CsfdRefreshTask> logger)
    {
        _libraryManager = libraryManager;
        _updater = updater;
        _stateStore = stateStore;
        _logger = logger;
    }

    public string Name => "Refresh ČSFD ratings";

    public string Key => "CsfdRefreshRatings";

    public string Description => "Fetches ČSFD ratings for movies and series and stores them as critic rating.";

    public string Category => "ČSFD Rating";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(30).Ticks,
        },
    ];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.SetCriticRating)
        {
            return;
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
        });

        _logger.LogInformation("ČSFD refresh: processing {Count} movies and series", items.Count);
        if (items.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var staleBefore = DateTimeOffset.UtcNow - TimeSpan.FromDays(Math.Clamp(config.RefreshIntervalDays, 1, 3650));
        var processed = 0;
        var updated = 0;
        var skipped = 0;
        try
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;
                try
                {
                    // Recently refreshed and already matched: nothing to do.
                    if (!string.IsNullOrEmpty(item.GetProviderId("Csfd"))
                        && _stateStore.GetFetchedAt(item.Id) is { } fetchedAt
                        && fetchedAt > staleBefore)
                    {
                        skipped++;
                        continue;
                    }

                    var result = await _updater.UpdateItemAsync(item, item is Series, config, cancellationToken).ConfigureAwait(false);
                    if (result.Changed)
                    {
                        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        updated++;
                    }

                    // Record freshness only once the item is safely persisted, so an
                    // interrupted save is retried by the next run.
                    if (result.RatingFetched)
                    {
                        _stateStore.SetFetchedAt(item.Id, DateTimeOffset.UtcNow);
                    }
                }
                finally
                {
                    progress.Report(processed * 100.0 / items.Count);
                }
            }
        }
        finally
        {
            _stateStore.Flush();
        }

        _logger.LogInformation(
            "ČSFD refresh finished: {Updated} of {Count} items updated, {Skipped} skipped as fresh",
            updated,
            items.Count,
            skipped);
    }
}
