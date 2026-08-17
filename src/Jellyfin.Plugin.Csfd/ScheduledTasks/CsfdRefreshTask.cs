using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Csfd.Csfd;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.ScheduledTasks;

/// <summary>
/// Walks the whole movie library: resolves missing ČSFD ids and refreshes
/// CriticRating from ČSFD. This is the backfill path for libraries that
/// existed before the plugin was installed, and keeps ratings current.
/// </summary>
public class CsfdRefreshTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly CsfdClient _client;
    private readonly CsfdResolver _resolver;
    private readonly ILogger<CsfdRefreshTask> _logger;

    public CsfdRefreshTask(ILibraryManager libraryManager, CsfdClient client, CsfdResolver resolver, ILogger<CsfdRefreshTask> logger)
    {
        _libraryManager = libraryManager;
        _client = client;
        _resolver = resolver;
        _logger = logger;
    }

    public string Name => "Refresh ČSFD ratings";

    public string Key => "CsfdRefreshRatings";

    public string Description => "Fetches ČSFD ratings for all movies and stores them as critic rating.";

    public string Category => "ČSFD Rating";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.SetCriticRating)
        {
            return;
        }

        var movies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsVirtualItem = false,
            Recursive = true,
        }).OfType<Movie>().ToList();

        _logger.LogInformation("ČSFD refresh: processing {Count} movies", movies.Count);

        var processed = 0;
        var updated = 0;
        foreach (var movie in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var changed = false;
            var csfdId = movie.GetProviderId("Csfd");
            if (string.IsNullOrEmpty(csfdId)
                && (config.OverwriteExistingCriticRating || !movie.CriticRating.HasValue))
            {
                csfdId = await _resolver.ResolveAsync(movie.Name, movie.OriginalTitle, movie.ProductionYear, cancellationToken).ConfigureAwait(false);
                if (csfdId is not null)
                {
                    movie.SetProviderId("Csfd", csfdId);
                    changed = true;
                }
            }

            if (!string.IsNullOrEmpty(csfdId))
            {
                var rating = await _client.GetRatingPercentAsync(csfdId, cancellationToken).ConfigureAwait(false);
                if (rating.Success && rating.Percent.HasValue && movie.CriticRating != rating.Percent.Value)
                {
                    movie.CriticRating = rating.Percent.Value;
                    changed = true;
                }
            }

            if (changed)
            {
                await movie.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                updated++;
            }

            processed++;
            progress.Report(processed * 100.0 / movies.Count);
        }

        _logger.LogInformation("ČSFD refresh finished: {Updated} of {Count} movies updated", updated, movies.Count);
    }
}
