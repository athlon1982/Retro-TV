# Retro TV plugin — BUILD 0.3.0.3 record

Date: 2026-11-09 ~14:47 EST. Supersedes 0.3.0.2 before its release (0.3.0.2 was never released; no release exists for it).

## What changed (Michael's direction, 2026-11-09 ~14:43–14:46)

1. **Library-create button STRIPPED** (Michael tapped "Strip it out before 0.3.0.2 ships").
   - `Configuration/configPage.html`: removed the `#btnCreateLibrary` button + `#retroTvLibraryResult` status line, and the whole `ApiClient.ajax` VirtualFolders click handler.
   - Output folder description rewritten: no button instruction; "optional on-demand library. Leave blank if you only want Live TV — the channels do not need it."
   - Output folder + Library name fields KEPT (they feed the optional catalog build / catalog.json).
2. **Settings page fully generic** — no personal references anywhere a user can see:
   - `PluginConfiguration.cs`: OutputFolder doc (was "On Dayz this is F:\IA-Channels\"), ffmpeg doc (was "On Dayz this is F:\Jellyfin\Server\ffmpeg.exe"), WeatherZipCode default **"05773" → blank** (string.Empty), "Michael's 'don't leave anything out'" → neutral wording, "Stage 2 master switch" → "Live channels master switch".
   - `Catalog/CatalogBuilder.cs` + `Tasks/BuildCatalogTask.cs`: error/warning no longer names Dayz/F:\IA-Channels\.
   - `Guide/WeatherChannel.cs` + `Guide/guidePage.html`: place fallback "Wallingford, VT" → "Unknown location".
   - `configPage.html`: "nothing is downloaded in Stage 1" → "nothing is downloaded to your server"; "(Stage 1)" / "(Stage 2)" dropped from the on-demand and weather headings; weather description rewritten (no 05773/Wallingford); JS fallbacks `|| "05773"` → `|| ""` (load + save).
   - `Guide/Channels/ChannelLineup.cs`: "shape Michael sent" → neutral.
   - Verified by re-grep: zero matches for dayz / 192.168 / 05773 / wallingford / IA-Channels / Mike's Flix in shipped code, and zero in the built DLL strings.
   - Internal code comments still say "Stage 2 sidecar" in 4 places (ScheduleEngine, GuideService, GuideController, LiveTvController) — developer history, not user-visible. Left alone.
3. Version bumped 0.3.0.2 → **0.3.0.3** (csproj Version/AssemblyVersion/FileVersion + meta.json). Meta changelog rewritten for the strip + generic settings.

## Build

- `dotnet build -c Release`: **0 warnings / 0 errors** (clean rebuild).
- DLL: 2,940,928 B (was 2,944,000 B — button code gone).
- Package (manual, same as 0.3.0.2 — never build.sh): `dist/retro-tv_0.3.0.3.zip`
  - **986,784 B, MD5 af808e56045c3d98b906a24f95d730a4, SHA256 539b46a5854b1846c51aa20d92f2327cc88a641ac15e64a4f8799690de3c5593**

## Load test (test server, stopped by PID after)

- Plugin loads as **0.3.0.3** (log: "Loaded plugin: Retro TV 0.3.0.3").
- `/RetroTv/Guide` 200; M3U **154** entries; `/RetroTv/guide.xml` 200 (34,733,228 B).
- Stream ch28 (drive-in-movies): 200, 2,981,304 B, **valid MPEG-TS** (sync bytes check passed).
- Config page 200 (25,574 B — smaller, button code gone): **button present 0**, **personal refs (Dayz/05773/Wallingford) 0**, "Getting started" 2, "Apply this theme" 2.
- Note: the test server's stored WeatherZipCode was still 05773 (test data from the old install); the served page carries no hardcoded 05773 — a fresh install's field is blank by code default.

## What 0.3.0.3 does NOT change vs 0.3.0.2

- Everything else from 0.3.0.2 is unchanged: probe-timeout fix, Getting-started box, copyable tuner links, one-click theme apply to Branding, 154 channels, commercial breaks, 96 h guide.
- 0.3.0.1 and 0.3.0.2 are superseded and should never be released. **Release v0.3.0.3.**

## Manifest

- Prepared entry (NOT pushed — the v0.3.0.3 release doesn't exist yet): version 0.3.0.3, checksum af808e56…, sourceUrl …/releases/download/v0.3.0.3/retro-tv_0.3.0.3.zip. Push it the moment the release exists.
- The previously prepared 0.3.0.2 manifest entry is discarded (0.3.0.2 never shipped).
