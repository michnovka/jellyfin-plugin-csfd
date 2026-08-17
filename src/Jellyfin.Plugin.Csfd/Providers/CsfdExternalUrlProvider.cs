using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>Adds a clickable "ČSFD" link on item detail pages.</summary>
public class CsfdExternalUrlProvider : IExternalUrlProvider
{
    public string Name => "ČSFD";

    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        var csfdId = item.GetProviderId("Csfd");
        if (!string.IsNullOrEmpty(csfdId))
        {
            yield return $"https://www.csfd.cz/film/{csfdId}/prehled/";
        }
    }
}
