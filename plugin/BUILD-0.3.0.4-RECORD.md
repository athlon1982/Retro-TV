# Retro TV plugin — BUILD 0.3.0.4 record

Date: 2026-11-09 ~19:37 EST. The next release (Michael's direction, 2026-11-09 ~19:31: "add a Green Theme to the Retro-TV theme selection for next release and a choice for other colors").

## What changed

1. **Four new guide themes** — Green Mountain (deep forest green `#0c1a10` + Vermont gold `#f2c14e`, matching the Mega Dashboard), Harvest Gold, Burnt Orange, Cherry Red. Each theme is a JSON definition in `Guide/Themes/builtin/` (same shape as woodgrain-78.json: colors, fonts, density comfortable, posters on, CRT overlay on) plus a Branding CSS file in `Guide/Themes/Branding/` (woodgrain layout, new THEME block only).
2. **Theme count is now 7** (was 3): woodgrain-78, neon-nights, vhs-blue-screen, green-mountain, harvest-gold, burnt-orange, cherry-red.
3. **No config-page change needed** — the settings dropdown is built live from `GET /RetroTv/Guide/Themes`, so the new themes appear automatically. The one-click Apply-theme button works for all four new themes (each has a matching Branding CSS file). The guide page switcher picks them up the same way.
4. **Docs updated**: PLUGIN-DESIGN.md §5.3 (7 themes, 4 new rows), §5.4, §12 verification line; repo README.md theme list.
5. Version bumped 0.3.0.3 → **0.3.0.4** (csproj Version/AssemblyVersion/FileVersion + meta.json). Meta changelog rewritten for the new themes.

## Build

- `dotnet build -c Release`: **0 errors, 1 warning** — CS8602 nullable dereference at `LiveTv/LiveTvController.cs:625` (`proc.HasExited` in the probe-timeout cleanup). Pre-existing code from the 0.3.0.2 probe fix; this change touches no C# at all. Noted honestly; 0.3.0.3's record said 0 warnings.
- DLL: 2,985,472 B (was 2,940,928 B — 4 new embedded JSON + 4 new embedded CSS resources).
- Package (manual, same as 0.3.0.3 — never build.sh): `dist/retro-tv_0.3.0.4.zip`
  - **987,003 B, MD5 3099b57284334e38b2afd1f515967d93, SHA256 4b2e882f3a8b71a38764db9c18fc7cb11b239cb7f2e40e7a3c1e71a4884365ad**

## Load test (test server, stopped by PID after)

- Plugin loads as **0.3.0.4** (log: "Loaded plugin: Retro TV 0.3.0.4").
- `/RetroTv/Guide/Themes` 200: **7 themes**, all four new ones listed (green-mountain, harvest-gold, burnt-orange, cherry-red).
- `/RetroTv/Guide/Theme?theme=green-mountain` 200: id green-mountain, accent `#f2c14e`, background `#0c1a10`.
- `/RetroTv/Guide` 200; M3U **154** entries; `/RetroTv/guide.xml` 200 (34,739,347 B).
- Stream ch28 (drive-in-movies): 200, 1,252,268 B, **valid MPEG-TS** (sync bytes check passed).
- Config page 200.

## What 0.3.0.4 does NOT change vs 0.3.0.3

- Everything else from 0.3.0.3 is unchanged: generic settings, Getting-started box, copyable tuner links, one-click theme apply, 154 channels, commercial breaks, 96 h guide, probe-timeout fix.

## Manifest

- Prepared entry (NOT pushed — the v0.3.0.4 release doesn't exist yet): version 0.3.0.4, checksum 4b2e882f…, sourceUrl …/releases/download/v0.3.0.4/retro-tv_0.3.0.4.zip. Push it the moment the release exists.
