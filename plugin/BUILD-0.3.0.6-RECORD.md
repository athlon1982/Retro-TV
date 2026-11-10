# Retro TV plugin — BUILD 0.3.0.6 record

Date: 2026-11-10 ~00:41–00:50 EST. Michael's direction, 2026-11-10 ~00:37 ("Go for it" — approved building the missing Backwhen channels, Holiday TV first).

## What changed

1. **New channel: Holiday TV (ch 69)** — a permanent 365-day channel for Thanksgiving, Christmas, New Year, Valentine's Day, Easter and Summer. Group "Holiday & seasonal", folder "Holiday TV", tier "pd", slug "holiday-tv". Channel count is now **156** (was 155).
2. **15 Holiday TV items** (all verified public-domain on IA, each with an MP4):
   - NEW to the catalog (5): Rudolph the Red-Nosed Reindeer (1948), Santa Claus (1959, ~70 min film), Sappy New Year (1961), Ozzie & Harriet: Valentine Show, Jack Benny Program S10E14 Easter Show.
   - Already in the catalog, duplicated into the Holiday TV folder (10): Christmas Comes But Once A Year (1936), The Snowman (1933), Ozzie & Harriet: Late Christmas Gift, Dragnet: A Gun For Christmas, Beverly Hillbillies: Home For Christmas, Dragnet: The Big New Year, The Turkey Shoot, Beverly Hillbillies: Turkey Day, The Easter Bunny's Comin' To Town promo, Easter Seals with Carol Burnett.
   - The same identifier can appear in two folders (the catalog builder only dedupes output directories, not identifiers) — so these items still play on their original channels too.
3. **Holiday TV folder added** to the catalog config (`repo/config.full.json`, query is an explicit identifier list so it pulls exactly these 15) and to the **live plugin's FoldersJson on Dayz** (config backed up). The next catalog build (daily 3 AM) will populate it live.
4. Version bumped 0.3.0.5 → **0.3.0.6** (csproj Version/AssemblyVersion/FileVersion + meta.json). Meta changelog rewritten for Holiday TV.
5. **Note:** a new channel needs a seed pool. The guide service creates the pool from the catalog on the next `RefreshPoolsFromCatalog` (triggered by the catalog build task). On the VM test server the pool was added manually to pools.json to verify the channel; on Dayz it will be created by the 3 AM catalog build. The channel will NOT appear in the guide until that build runs.

## Build

- `dotnet build -c Release`: **0 errors, 1 warning** — CS8602 nullable dereference at `LiveTv/LiveTvController.cs:625` (pre-existing, from the 0.3.0.2 probe fix). Same warning as 0.3.0.5.
- DLL: 2,985,984 B.
- Package (manual, same as 0.3.0.5 — never build.sh): `dist/retro-tv_0.3.0.6.zip`
  - **987,054 B, MD5 5fe5de5f3194701874ddf9b5e7b1fd02, SHA256 a5eb531c89f0fd12d6bd434b7501b641b77de1ece7bf25e91104f7ec5d57c741**

## Load test (test server, stopped by PID after)

- Plugin loads as **0.3.0.6** (log: "Loaded plugin: Retro TV 0.3.0.6").
- Guide loaded: **155 active channels** (was 154 — Holiday TV is the +1; Audiobooks stays disabled).
- M3U: **155 entries**, Holiday TV present as `tvg-id="retrotv-holiday-tv" tvg-chno="69" tvg-name="Holiday TV"`.
- Stream Holiday TV: HTTP 200, 8,799,528 B, **valid MPEG-TS, h264 1280x720** (ffprobe) — the 720p output from 0.3.0.5 carries over.
- Note: the test server needed the Holiday TV pool added to its pools.json manually (the catalog refresh only runs on a catalog build). On Dayz the 3 AM build does this automatically.

## What 0.3.0.6 does NOT change vs 0.3.0.5

- Everything else from 0.3.0.5 is unchanged: 720p stream output, 7 guide themes, generic settings, Getting-started box, copyable tuner links, one-click theme apply, commercial breaks, 96 h guide, probe-timeout fix.

## Manifest

- Prepared entry (NOT pushed — the v0.3.0.6 release doesn't exist yet): version 0.3.0.6, checksum a5eb531c…, sourceUrl …/releases/download/v0.3.0.6/retro-tv_0.3.0.6.zip. Push it the moment the release exists.

## Release steps

See `~/workspace/your_files/retro-tv-0306-release-steps.md` (Michael creates the release; Jarvis pushes the manifest entry after). Note: 0.3.0.5's release is still uncreated — 0.3.0.5 and 0.3.0.6 can be released together, or 0.3.0.6 can supersede 0.3.0.5 (it contains everything 0.3.0.5 has plus Holiday TV).
