using Jellyfin.Plugin.RetroTv.Catalog;
using Jellyfin.Plugin.RetroTv.Guide;
using Jellyfin.Plugin.RetroTv.Guide.Themes;
using Jellyfin.Plugin.RetroTv.Weather;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.RetroTv;

/// <summary>
/// Registers Retro TV services with Jellyfin's DI container. Runs once at
/// server startup; registers only — no I/O, no network, nothing that can
/// fail the startup.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Typed HttpClients (factory-managed lifetime; the IA and Open-Meteo
        // calls need no credentials).
        serviceCollection.AddHttpClient<InternetArchiveClient>();
        serviceCollection.AddHttpClient<OpenMeteoClient>();

        // Catalog build state (single-flight guard + last-run status).
        serviceCollection.AddSingleton<CatalogBuildState>();

        // The catalog builder itself (transient: fresh per build run).
        serviceCollection.AddTransient<RetroTvCatalogBuilder>();

        // Theme loading (embedded built-ins + user themes from the plugin data dir).
        serviceCollection.AddSingleton<ThemeService>();

        // The weather channel (forecast cache + guide segments + slate text).
        serviceCollection.AddSingleton<WeatherChannel>();

        // The channel guide (lineup + pools + schedules + M3U/XMLTV).
        serviceCollection.AddSingleton<GuideService>();
    }
}
