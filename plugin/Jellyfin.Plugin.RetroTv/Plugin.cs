using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.RetroTv;

/// <summary>
/// Retro TV — the Internet Archive as a retro-video library and live
/// channels inside Jellyfin. The plugin identity (Id) is permanent: it is
/// the guid in meta.json and in the plugin repository manifest, and it must
/// never change between versions.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Stable plugin identity. Generated once, 2026-11-08. Never change.</summary>
    public static readonly Guid PluginGuid = new("25eef671-4023-4c23-badb-6c051fa0620f");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Current plugin instance (set by the constructor). Null only before the server loads the plugin.</summary>
    public static Plugin? Instance { get; private set; }

    public override string Name => "Retro TV";

    public override string Description => "Turns the Internet Archive into a retro-video library and live channels inside Jellyfin.";

    public override Guid Id => PluginGuid;

    /// <summary>Configuration page served from the embedded configPage.html resource.</summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Assembly.GetName().Name)
            }
        };
    }
}
