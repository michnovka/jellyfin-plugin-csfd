using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Csfd.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Csfd;

/// <summary>
/// ČSFD Rating plugin: fetches movie ratings from csfd.cz and stores them
/// in the native CriticRating field so every Jellyfin client displays them.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "ČSFD Rating";

    public override Guid Id => Guid.Parse("af0c9c45-a8e5-498a-b848-3963aade7e6e");

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "csfdrating",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
