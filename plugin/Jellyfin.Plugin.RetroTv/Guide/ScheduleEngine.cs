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

        /// <summary>True for an interleaved commercial-break spot (Title is "Commercial Break", Description is the spot's title). The guide shows it as a break; the stream plays the spot's MP4.</summary>
        public bool IsCommercialBreak { get; init; }

        public int EffectiveRuntimeSeconds =>
            RuntimeSeconds is > 0 ? RuntimeSeconds.Value : DefaultRuntimeSeconds;
    }

    /// <summary>One vintage commercial spot from breaks-pool.json.</summary>
    public class BreakSpot
    {
        public required string Identifier { get; init; }
        public required string Title { get; init; }
        public int? Year { get; init; }
        public required int RuntimeSeconds { get; init; }
        public required string Mp4Url { get; init; }
        public string? PosterUrl { get; init; }
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

    /// <summary>
    /// Commercial breaks — a direct port of the Stage 2 sidecar's break
    /// engine (server.mjs v0.2.0), same constants, same FNV-1a spot pick,
    /// same greedy boundaries + repeat-boundary guard, so the plugin's
    /// guide and the sidecar's guide agree break-for-break. Rule: at most
    /// ONE break per hour per channel, one 30–60 s spot, at programme
    /// boundaries only. Breaks are interleaved into the channel's
    /// effective item list, so the guide (XMLTV + JSON) and the live
    /// stream (ffmpeg batch) can never disagree — both walk the same
    /// list. With breaks off, the original list is returned unchanged,
    /// so OFF reproduces the pre-breaks feed exactly.
    /// </summary>
    public static class CommercialBreaks
    {
        /// <summary>At most one break per hour per channel.</summary>
        public const int MinGapSeconds = 3600;

        /// <summary>One spot per break, 30–60 s.</summary>
        public const int SpotMinSeconds = 30;

        /// <summary>One spot per break, 30–60 s.</summary>
        public const int SpotMaxSeconds = 60;

        /// <summary>Eligible pool spots run 20–90 s.</summary>
        public const int EligibleMinSeconds = 20;

        /// <summary>Eligible pool spots run 20–90 s.</summary>
        public const int EligibleMaxSeconds = 90;

        /// <summary>Channel slugs that never get breaks (their programmes ARE commercials/bumpers, or audio-only).</summary>
        public static readonly HashSet<string> ExcludedSlugs = new(StringComparer.OrdinalIgnoreCase)
        {
            "ad-break", "ads-1950s", "ads-1960s", "ads-1970s", "station-break", "audiobooks"
        };

        /// <summary>FNV-1a 32-bit, same as the sidecar's hashStr (uint arithmetic wraps mod 2^32, matching JS Math.imul).</summary>
        public static uint Fnv1aHash(string s)
        {
            uint h = 2166136261u;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619u;
            }

            return h;
        }

        /// <summary>One spot for a boundary: a pure function of (channel, boundary index), so the schedule is stable across restarts and across the on/off toggle. Short pool (30–60 s) first, full pool (20–90 s) as fallback.</summary>
        public static BreakSpot? PickSpot(string channelSlug, int boundaryIndex, IReadOnlyList<BreakSpot> shortPool, IReadOnlyList<BreakSpot> fullPool)
        {
            var pool = shortPool.Count > 0 ? shortPool : fullPool;
            if (pool.Count == 0)
            {
                return null;
            }

            return pool[(int)(Fnv1aHash(channelSlug + ":" + boundaryIndex) % (uint)pool.Count)];
        }

        /// <summary>Boundary indices (into baseItems) after which a break goes. Greedy: a break only once &gt;= MinGapSeconds of programming have played since the last break. The list repeats forever, so the starting "time since last break" is iterated until the tail stops changing (max 10 passes) — then the repeat-boundary guard drops the first break if the wraparound gap (tail + first-break offset) would come in under the minimum.</summary>
        public static HashSet<int> BreakBoundariesFor(IReadOnlyList<GuideItem> baseItems)
        {
            var startElapsed = 0;
            var chosen = new List<int>();
            for (var pass = 0; pass < 10; pass++)
            {
                var breaks = new List<int>();
                var elapsed = startElapsed;
                for (var i = 0; i < baseItems.Count; i++)
                {
                    elapsed += baseItems[i].EffectiveRuntimeSeconds;
                    if (elapsed >= MinGapSeconds)
                    {
                        breaks.Add(i);
                        elapsed = 0;
                    }
                }

                int tail;
                if (breaks.Count > 0)
                {
                    tail = 0;
                    for (var i = breaks[^1] + 1; i < baseItems.Count; i++)
                    {
                        tail += baseItems[i].EffectiveRuntimeSeconds;
                    }
                }
                else
                {
                    tail = 0;
                    foreach (var item in baseItems)
                    {
                        tail += item.EffectiveRuntimeSeconds;
                    }
                }

                chosen = breaks;
                if (tail == startElapsed)
                {
                    break;
                }

                startElapsed = tail;
            }

            if (chosen.Count >= 2)
            {
                var firstOffset = 0;
                for (var i = 0; i <= chosen[0]; i++)
                {
                    firstOffset += baseItems[i].EffectiveRuntimeSeconds;
                }

                var tail = 0;
                for (var i = chosen[^1] + 1; i < baseItems.Count; i++)
                {
                    tail += baseItems[i].EffectiveRuntimeSeconds;
                }

                if (tail + firstOffset < MinGapSeconds)
                {
                    chosen = chosen.Skip(1).ToList();
                }
            }

            return new HashSet<int>(chosen);
        }

        /// <summary>The channel's effective item list: the original items, repeated until they span at least two break gaps (a channel whose whole cycle is shorter than the gap would otherwise never earn a break), with one break spot interleaved after each qualifying boundary.</summary>
        public static List<GuideItem> BuildEffectiveItems(IReadOnlyList<GuideItem> orig, string channelSlug, IReadOnlyList<BreakSpot> shortPool, IReadOnlyList<BreakSpot> fullPool)
        {
            if (orig.Count == 0)
            {
                return new List<GuideItem>();
            }

            var baseItems = new List<GuideItem>();
            var baseSeconds = 0;
            while (baseSeconds < MinGapSeconds * 2)
            {
                foreach (var item in orig)
                {
                    baseItems.Add(item);
                    baseSeconds += item.EffectiveRuntimeSeconds;
                }
            }

            var breaksAt = BreakBoundariesFor(baseItems);
            var effective = new List<GuideItem>();
            for (var i = 0; i < baseItems.Count; i++)
            {
                effective.Add(baseItems[i]);
                if (!breaksAt.Contains(i))
                {
                    continue;
                }

                var spot = PickSpot(channelSlug, i, shortPool, fullPool);
                if (spot is null)
                {
                    continue;
                }

                effective.Add(new GuideItem
                {
                    Identifier = "adbreak-" + spot.Identifier,
                    Title = "Commercial Break",
                    Year = spot.Year,
                    RuntimeSeconds = spot.RuntimeSeconds,
                    Description = spot.Title,
                    Mp4Url = spot.Mp4Url,
                    PosterUrl = spot.PosterUrl,
                    Tier = "pd",
                    IsCommercialBreak = true
                });
            }

            return effective;
        }
    }
}
