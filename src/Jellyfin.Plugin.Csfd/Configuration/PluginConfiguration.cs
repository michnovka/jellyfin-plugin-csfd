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
}
