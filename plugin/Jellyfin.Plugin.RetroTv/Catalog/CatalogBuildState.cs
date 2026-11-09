using System;

namespace Jellyfin.Plugin.RetroTv.Catalog;

/// <summary>
/// Single-flight guard + last-run status for catalog builds. The scheduled
/// task and the build-now API endpoint both go through here, so a second
/// trigger while a build is running gets a 409 instead of starting a
/// duplicate build. All state is in-memory; the durable record is
/// catalog.json itself.
/// </summary>
public class CatalogBuildState
{
    private readonly object _lock = new();
    private bool _running;

    /// <summary>True while a catalog build is running (from any trigger).</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }

    /// <summary>Summary of the most recent finished build (null before the first run).</summary>
    public LastBuildSummary? LastBuild { get; private set; }

    /// <summary>Try to claim the build slot. Returns false if a build is already running.</summary>
    public bool TryBegin()
    {
        lock (_lock)
        {
            if (_running)
            {
                return false;
            }

            _running = true;
            return true;
        }
    }

    /// <summary>Release the build slot and record the run's summary.</summary>
    public void End(LastBuildSummary summary)
    {
        lock (_lock)
        {
            LastBuild = summary;
            _running = false;
        }
    }

    public class LastBuildSummary
    {
        public required DateTimeOffset FinishedAt { get; init; }
        public required bool Pilot { get; init; }
        public required int Version { get; init; }
        public required int ItemCount { get; init; }
        public required int SkippedCount { get; init; }
        public required TimeSpan Duration { get; init; }
        public required string Trigger { get; init; }
        public string? Error { get; init; }
    }
}
