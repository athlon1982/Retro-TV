using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.RetroTv.Guide;

/// <summary>
/// Deterministic schedule engine — a pure function of (channel pool, wall-clock
/// time). Port of the Stage 2 sidecar's programAt/scheduleFrom (same anchor),
/// so the plugin's guide and the sidecar's streams can never disagree.
/// A show is mid-programme when you tune in, just like real TV (BackWhen's
/// shared clock). Restarting Jellyfin or the plugin never desyncs the
/// schedule, because the schedule is computed, not stored as a timeline.
/// </summary>
public static class ScheduleEngine
{
    /// <summary>Fixed anchor: 2026-11-08T00:00:00Z — the same anchor the sidecar uses. Never change it: every schedule ever computed depends on it.</summary>
    public static readonly DateTimeOffset Anchor = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Fallback runtime for an item with no known runtime (30 minutes). The sidecar pools all carry runtimes; catalog-derived pools may not.</summary>
    public const int DefaultRuntimeSeconds = 1800;

    /// <summary>One scheduled programme.</summary>
    public class ScheduledProgramme
    {
        public required GuideItem Item { get; init; }
        public required int Index { get; init; }
        public required DateTimeOffset Start { get; init; }
        public required DateTimeOffset Stop { get; init; }

        /// <summary>How far into the programme we are when tuning in at the query time (0 for later programmes).</summary>
        public TimeSpan Offset { get; init; }
    }

    /// <summary>One schedulable item (a pool entry or a weather segment).</summary>
    public class GuideItem
    {
        public required string Identifier { get; init; }
        public required string Title { get; init; }
        public int? Year { get; init; }
        public int? RuntimeSeconds { get; init; }
        public string Description { get; set; } = string.Empty;
        public string? Mp4Url { get; init; }
        public string? PosterUrl { get; init; }
        public string Tier { get; init; } = "pd";

        public int EffectiveRuntimeSeconds =>
            RuntimeSeconds is > 0 ? RuntimeSeconds.Value : DefaultRuntimeSeconds;
    }

    /// <summary>The programme playing at <paramref name="time"/> on a channel whose items cycle in order.</summary>
    public static ScheduledProgramme ProgramAt(IReadOnlyList<GuideItem> items, DateTimeOffset time)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("A channel needs at least one item to schedule.", nameof(items));
        }

        var cycleMs = 0L;
        foreach (var item in items)
        {
            cycleMs += (long)item.EffectiveRuntimeSeconds * 1000;
        }

        var posMs = (long)((time - Anchor).TotalMilliseconds % cycleMs);
        if (posMs < 0)
        {
            posMs += cycleMs;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var durMs = (long)items[i].EffectiveRuntimeSeconds * 1000;
            if (posMs < durMs)
            {
                var start = time - TimeSpan.FromMilliseconds(posMs);
                return new ScheduledProgramme
                {
                    Item = items[i],
                    Index = i,
                    Start = start,
                    Stop = start + TimeSpan.FromMilliseconds(durMs),
                    Offset = TimeSpan.FromMilliseconds(posMs)
                };
            }

            posMs -= durMs;
        }

        // Unreachable (posMs < cycleMs by construction), but keep the compiler honest.
        return new ScheduledProgramme
        {
            Item = items[0],
            Index = 0,
            Start = time,
            Stop = time + TimeSpan.FromSeconds(items[0].EffectiveRuntimeSeconds),
            Offset = TimeSpan.Zero
        };
    }

    /// <summary>All programmes from <paramref name="from"/> for <paramref name="hours"/> hours, starting with the programme in progress.</summary>
    public static List<ScheduledProgramme> ScheduleFrom(IReadOnlyList<GuideItem> items, DateTimeOffset from, double hours)
    {
        var end = from + TimeSpan.FromHours(hours);
        var current = ProgramAt(items, from);
        var programmes = new List<ScheduledProgramme>();
        var t = current.Start;
        var idx = current.Index;
        while (t < end)
        {
            var item = items[idx % items.Count];
            var dur = TimeSpan.FromSeconds(item.EffectiveRuntimeSeconds);
            programmes.Add(new ScheduledProgramme
            {
                Item = item,
                Index = idx % items.Count,
                Start = t,
                Stop = t + dur,
                Offset = t == current.Start ? current.Offset : TimeSpan.Zero
            });
            t += dur;
            idx++;
        }

        return programmes;
    }

    /// <summary>Sort a channel's items into play order. Channels flagged oldestFirst (soaps) play oldest episode first; everything else keeps catalog order.</summary>
    public static List<GuideItem> OrderItems(IReadOnlyList<GuideItem> items, bool oldestFirst)
    {
        var list = new List<GuideItem>(items);
        if (!oldestFirst)
        {
            return list;
        }

        list.Sort((a, b) =>
        {
            var ya = a.Year ?? int.MaxValue;
            var yb = b.Year ?? int.MaxValue;
            var byYear = ya.CompareTo(yb);
            return byYear != 0 ? byYear : string.Compare(a.Title, b.Title, StringComparison.Ordinal);
        });
        return list;
    }
}
