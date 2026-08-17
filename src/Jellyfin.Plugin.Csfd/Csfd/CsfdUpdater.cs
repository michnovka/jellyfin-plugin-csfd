using Jellyfin.Plugin.Csfd.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>Outcome of one update attempt. RatingFetched means the film page was
/// successfully read this round (whether or not the item changed).</summary>
public sealed record CsfdUpdateResult(bool Changed, bool RatingFetched);

/// <summary>
/// Shared logic for the metadata providers and the scheduled task: resolve the
/// item on ČSFD if needed, fetch the current rating and apply it to CriticRating.
/// Registered as a singleton.
/// </summary>
public sealed class CsfdUpdater
{
    private readonly CsfdClient _client;
    private readonly CsfdResolver _resolver;
    private readonly CsfdStateStore _stateStore;
    private readonly ILogger<CsfdUpdater> _logger;

    public CsfdUpdater(CsfdClient client, CsfdResolver resolver, CsfdStateStore stateStore, ILogger<CsfdUpdater> logger)
    {
        _client = client;
        _resolver = resolver;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <summary>
    /// Resolves and applies the ČSFD rating. With <paramref name="recordStateImmediately"/>
    /// the fetch timestamp is stored here; the scheduled task passes false and records
    /// it only after the item was successfully persisted.
    /// </summary>
    public async Task<CsfdUpdateResult> UpdateItemAsync(BaseItem item, bool series, PluginConfiguration config, bool recordStateImmediately, CancellationToken cancellationToken)
    {
        var changed = false;
        int? percent;
        int? votes;

        var csfdId = item.GetProviderId("Csfd");
        if (string.IsNullOrEmpty(csfdId))
        {
            // A rating set by someone else and overwriting disabled: leave the item alone.
            if (!config.OverwriteExistingCriticRating && item.CriticRating.HasValue)
            {
                return new CsfdUpdateResult(false, false);
            }

            var resolution = await _resolver.ResolveAsync(item.Name, item.OriginalTitle, item.ProductionYear, series, cancellationToken).ConfigureAwait(false);
            if (resolution is null)
            {
                return new CsfdUpdateResult(false, false);
            }

            csfdId = resolution.Id;
            percent = resolution.Percent;
            votes = resolution.Votes;
            item.SetProviderId("Csfd", csfdId);
            changed = true;
        }
        else
        {
            var details = await _client.GetFilmDetailsAsync(csfdId, cancellationToken).ConfigureAwait(false);
            if (!details.Success)
            {
                return new CsfdUpdateResult(changed, false);
            }

            percent = details.Percent;
            votes = details.Votes;
        }

        if (percent.HasValue)
        {
            // With a positive threshold, an unknown vote count is treated as "not
            // enough votes" — obscure titles often lack aggregateRating entirely.
            if (config.MinimumVotes > 0 && (!votes.HasValue || votes.Value < config.MinimumVotes))
            {
                _logger.LogInformation(
                    "Skipping ČSFD rating for {Name}: {Votes} votes (minimum {Min})",
                    item.Name,
                    votes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
                    config.MinimumVotes);
            }
            else if (item.CriticRating != percent.Value)
            {
                _logger.LogInformation("ČSFD rating for {Name}: {Percent}%", item.Name, percent.Value);
                item.CriticRating = percent.Value;
                changed = true;
            }
        }

        if (recordStateImmediately)
        {
            _stateStore.SetFetchedAt(item.Id, DateTimeOffset.UtcNow);
        }

        return new CsfdUpdateResult(changed, true);
    }
}
