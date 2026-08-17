using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Csfd.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Write the ČSFD percentage into the item's CriticRating field.</summary>
    public bool SetCriticRating { get; set; } = true;

    /// <summary>Overwrite a CriticRating that another provider already set.</summary>
    public bool OverwriteExistingCriticRating { get; set; } = true;

    /// <summary>Minimum delay between requests to csfd.cz, in milliseconds.</summary>
    public int RequestDelayMs { get; set; } = 1500;

    /// <summary>Ignore ČSFD ratings based on fewer votes than this.</summary>
    public int MinimumVotes { get; set; } = 100;

    /// <summary>The scheduled task skips items whose rating was fetched within this many days.</summary>
    public int RefreshIntervalDays { get; set; } = 30;
}
