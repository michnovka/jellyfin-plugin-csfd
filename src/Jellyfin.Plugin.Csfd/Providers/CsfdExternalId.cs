using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Csfd.Providers;

/// <summary>Registers "Csfd" as a known external id (shown in the metadata editor).</summary>
public class CsfdExternalId : IExternalId
{
    public string ProviderName => "ČSFD";

    public string Key => "Csfd";

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public bool Supports(IHasProviderIds item) => item is Movie;
}
