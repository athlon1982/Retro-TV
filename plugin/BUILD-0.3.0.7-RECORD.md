# Retro TV plugin — BUILD 0.3.0.7 record

Date: 2026-11-10 ~06:50–07:00 EST. Michael's direction, 2026-11-10 06:48 ("Build it" — authorized building the missing Backwhen channels).

## What changed

1. **Seven new channels (163 total, was 156)** — all in the user-uploaded tier:
   - **Swamp TV (ch 217)** — M*A*S*H 24/7. Group "Sitcoms & classic TV", folder "Swamp TV", slug "swamp-tv". Source: `mashmulti` (252 episode MP4s).
   - **Mockingbird (ch 218)** — The Munsters 24/7. Group "Sitcoms & classic TV", folder "Mockingbird", slug "mockingbird". Sources: `munsters_the` (38 eps, 1964 series) + `the-munsters-today-season-1-3-complete` (73 eps, 1988–91 series). Total 111.
   - **Blue's Clues & Friends (ch 219)** — preschool. Group "Kids & cartoons", folder "Blue's Clues & Friends", slug "blues-clues-friends". Source: `blues-clues-s-03-e-59-blues-big-musical-movie` (146 eps, full series).
   - **Watertower Kids (ch 220)** — Kids' WB. Group "Kids & cartoons", folder "Watertower Kids", slug "watertower-kids". Sources: `animaniacs-1993-1998-movie_202601` (100) + `pinky.and.the.-brain.-s-01-e-12.-a.-pinky.and.the.-brain.-christmas.-1080p.-upsc` (95) + `tiny-toon-adventures-1990-1995_202512` (101). Total 296.
   - **Checkerboard (ch 221)** — 90s Cartoon Network. Group "Kids & cartoons", folder "Checkerboard", slug "checkerboard". Sources: `dexters-laboratory-the-complete-series` (81) + `powerpuff-girls-complete-series` (112). Total 193.
   - **Mouse House (ch 222)** — Disney Afternoon. Group "Kids & cartoons", folder "Mouse House", slug "mouse-house". Source: `ducktales-1987_202601` (100 eps, DuckTales 1987). (The other DuckTales upload, `Ducktales-Complete-Series`, is the same show — excluded to avoid duplicate episodes.)
   - **Moose TV (ch 223)** — Canadian classics. Group "Kids & cartoons", folder "Moose TV", slug "moose-tv". Sources: `MashSeries` (Fraggle Rock, 96 eps — filmed in Toronto) + `the-beachcombers-1975-11-16` + `the-beachcombers-1975-09-28-invisible-relic` (1 ep each). Total 98.
   - **Total new catalog entries: 1,196** (verified by simulating the multi-file grouping against live IA metadata).
2. **NEW: multi-file catalog support** (`CatalogFolder.MultiFile`, `CatalogBuilder.BuildItemAsync` now returns `List<CatalogEntry>`):
   - The old model created ONE catalog entry per IA identifier (best MP4 only). A full-series collection (one item, 100+ episode MP4s) contributed a single episode — useless for a 24/7 channel.
   - Folders with `multiFile: true` now emit ONE entry per distinct episode file. MP4s are grouped by file stem (stripping `.ia` / `_512kb` / `_edit` variants and directory prefixes), best-scoring file per group wins, so a file and its `.ia.mp4` derivative don't become two entries.
   - Each entry gets a synthesized unique identifier (`{identifier}--{file-slug}`, e.g. `mashmulti--mash-s10e01-that's-show-biz`) so the guide pool (`RefreshPoolsFromCatalog`, which dedupes by Identifier) treats episodes as distinct items. The Mp4Url points at the actual episode file; the Identifier is only a pool key + label, never used to build the stream URL.
   - Episode titles derive from the file name (e.g. "Mash S1E1 Pilot PAL" → "Mash S1E1 Pilot"), prefixed with the series title when the stem doesn't already start with it.
   - Single-file folders are UNCHANGED (same best-MP4 behavior as before). The daily 3 AM catalog build now handles multi-file folders natively, so the entries survive rebuilds.
   - Also synced the working-dir source to the 0.3.0.6 state first (it was behind: csproj + meta.json at 0.3.0.4, LiveTvController.cs at 640×480). The 720p scale filter is in.
3. **Folders added to `repo/config.full.json`** (now 18 folders): the 7 new folders, each an explicit-identifier query with `multiFile: true`, tier `user-uploaded`, limit 50.
4. **Lineup updated** (both working dir and repo mirror): 163 channels. Working-dir lineup.json was synced from the repo first (it was missing Holiday TV).
5. Version bumped 0.3.0.6 → **0.3.0.7** (csproj Version/AssemblyVersion/FileVersion + meta.json). Meta changelog rewritten for the 7 channels + multi-file support.

## NOT included: The Bridge (Star Trek)

- **Star Trek TOS has no clean full-episode MP4 collection on the Archive.** Verified: `star-trek-tos-hddvd` has 0 MP4s (HD DVD images); the BluRay item `Privatehd.toStarTrek1966S011080pBluRayVC1DTSHDMA7.1HDBEE1` has only BDMV/STREAM fragments (2–12 MB chunks, not episodes); the DVD-transfer items (`dvd-transfer-75/12/22`) have VTS files (whole-DVD rips, not individual episodes). No single full-series MP4 item found. **The Bridge is blocked on finding a source — flag to Michael, don't fake it.**

## Build

- `dotnet build -c Release`: **0 errors, 1 warning** — CS8602 nullable dereference at `LiveTvController.cs:625` (pre-existing, from the 0.3.0.2 probe fix). Same warning as 0.3.0.5/0.3.0.6.
- DLL: 2,990,080 B.
- Package (manual, same as 0.3.0.6 — never build.sh): `dist/retro-tv_0.3.0.7.zip`
  - **989,903 B, MD5 a4d25a3ec578fa2f147ba4b0325cd90b, SHA256 20cfb185f8b8022f40811996928fd95d0fb97d9361dbece93f946d98fb2228bc**

## Load test (test server, stopped by PID after)

- Plugin loads as **0.3.0.7** (log: "Loaded plugin: Retro TV 0.3.0.7").
- 7 themes endpoint still works. M3U serves 155 channels (the existing ones — the 7 new channels have no pools yet, so they don't appear in the M3U until the catalog build populates them; that's expected, same as Holiday TV before its first catalog build).
- Multi-file grouping validated by simulation against live IA metadata (same grouping rules as the C# code): 1,196 entries across the 7 folders, all identifiers unique.
- **Not yet verified end-to-end on the test server:** the actual catalog build producing the 1,196 entries + pools for the 7 channels (the test server's plugin config still has the old 10-folder FoldersJson, and the Build endpoint needs elevation/auth). **Verify on Dayz after the release: after the catalog build runs, the guide should show 162 active channels (155 + 7) and the 7 new pools should hold 252 / 111 / 146 / 296 / 193 / 100 / 98 items.**

## What 0.3.0.7 does NOT change vs 0.3.0.6

- Everything else from 0.3.0.6 is unchanged: 720p stream output, Holiday TV (ch 69), 7 guide themes, generic settings, Getting-started box, copyable tuner links, one-click theme apply, commercial breaks, 96 h guide, probe-timeout fix.

## Manifest

- Prepared entry (NOT pushed — the v0.3.0.7 release doesn't exist yet): version 0.3.0.7, checksum 20cfb185…, sourceUrl …/releases/download/v0.3.0.7/retro-tv_0.3.0.7.zip. Push it the moment the release exists.

## Release steps

See `~/workspace/your_files/retro-tv-0307-release-steps.md` (Michael creates the release; Jarvis pushes the manifest entry after). Note: 0.3.0.5's release is still uncreated (superseded by 0.3.0.6); 0.3.0.6's release is still uncreated. 0.3.0.7 contains everything in 0.3.0.5 + 0.3.0.6 + the 7 new channels, so Michael can create just the 0.3.0.7 release and skip the earlier two.

## After the release (Dayz)

1. **Update the live plugin's FoldersJson on Dayz** to add the 7 new folders (with `multiFile: true`) — same pattern as the Holiday TV FoldersJson update. The live config currently has 11 folders.
2. **Manual install** (same as the 0.3.0.4 fix, still owed): stop Jellyfin → clean the old `Retro TV_0.3.0.0` + `Retro TV_0.3.0.3` folders → extract the verified 0.3.0.7 zip → restart → verify 0.3.0.7.
3. **Run a catalog build** (or wait for the 3 AM build) → verify the 7 new pools populate (252 / 111 / 146 / 296 / 193 / 100 / 98) and the guide shows 162 active channels.
