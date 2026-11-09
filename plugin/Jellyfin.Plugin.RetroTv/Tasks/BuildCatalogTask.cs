using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RetroTv.Catalog;
using Jellyfin.Plugin.RetroTv.Guide;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Tasks;

/// <summary>
/// Scheduled task: "Build Retro TV catalog". Runs the ported catalog builder
/// on Jellyfin's schedule (daily by default, if AutoBuildEnabled) and on
/// demand from the config page / API. Defensive by design (see
/// PLUGIN-DESIGN.md §2.1): an IA failure on one item is a skip, never a
/// crash; a run-level failure is logged and returned, never rethrown into
/// the server — except cancellation, which propagates as cancellation.
/// </summary>
public class BuildCatalogTask : IScheduledTask
{
    private readonly ILogger<BuildCatalogTask> _logger;
    private readonly RetroTvCatalogBuilder _builder;
    private readonly CatalogBuildState _state;
    private readonly GuideService _guide;

    public BuildCatalogTask(
        ILogger<BuildCatalogTask> logger,
        RetroTvCatalogBuilder builder,
        CatalogBuildState state,
        GuideService guide)
    {
        _logger = logger;
        _builder = builder;
        _state = state;
        _guide = guide;
    }

    public string Name => "Build Retro TV catalog";

    public string Description => "Builds the Retro TV catalog from the Internet Archive: .strm + .nfo + posters into the output folder, for the Retro TV library.";

    public string Category => "Retro TV";

    public string Key => "RetroTv.BuildCatalog";

    public bool IsHidden => false;

    public bool IsEnabled => Plugin.Instance?.Configuration.AutoBuildEnabled ?? true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            _logger.LogError("[RetroTv] Build task ran before the plugin instance existed; nothing to do.");
            return;
        }

        var config = plugin.Configuration;
        if (string.IsNullOrWhiteSpace(config.OutputFolder))
        {
            _logger.LogWarning("[RetroTv] OutputFolder is not set. Open the Retro TV plugin settings and choose the output folder (on Dayz: F:\\IA-Channels\\), then run this task again.");
            return;
        }

        if (!_state.TryBegin())
        {
            _logger.LogInformation("[RetroTv] A catalog build is already running; this scheduled run stands down.");
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var result = await _builder.BuildAsync(config, pilot: false, progress, cancellationToken).ConfigureAwait(false);
            _state.End(new CatalogBuildState.LastBuildSummary
            {
                FinishedAt = DateTimeOffset.UtcNow,
                Pilot = false,
                Version = result.Version,
                ItemCount = result.ItemCount,
                SkippedCount = result.SkippedCount,
                Duration = DateTimeOffset.UtcNow - startedAt,
                Trigger = "scheduled-task"
            });

            // The guide grows with the catalog: refresh the channel pools from
            // the catalog just written. Best-effort — a pool refresh failure
            // never fails the build (the old pools keep serving).
            _guide.RefreshPoolsFromCatalog(Path.Combine(config.OutputFolder, "catalog.json"));
        }
        catch (OperationCanceledException)
        {
            _state.End(new CatalogBuildState.LastBuildSummary
            {
                FinishedAt = DateTimeOffset.UtcNow,
                Pilot = false,
                Version = 0,
                ItemCount = 0,
                SkippedCount = 0,
                Duration = DateTimeOffset.UtcNow - startedAt,
                Trigger = "scheduled-task",
                Error = "cancelled"
            });
            throw;
        }
        catch (Exception ex)
        {
            // Never take the server down with a build failure: log it and return.
            _logger.LogError(ex, "[RetroTv] Catalog build failed: {Message}", ex.Message);
            _state.End(new CatalogBuildState.LastBuildSummary
            {
                FinishedAt = DateTimeOffset.UtcNow,
                Pilot = false,
                Version = 0,
                ItemCount = 0,
                SkippedCount = 0,
                Duration = DateTimeOffset.UtcNow - startedAt,
                Trigger = "scheduled-task",
                Error = ex.Message
            });
        }
    }
}
