# Retro TV plugin — BUILD 0.3.0.5 record

Date: 2026-11-10 ~00:38 EST. Michael's direction, 2026-11-10 ~00:37 ("Go for it" — approved the 720p output change from the quality investigation).

## What changed

1. **Stream output raised from 640x480 to 720p (1280x720).** One line in `LiveTv/LiveTvController.cs`: the ffmpeg scale filter changed from `scale=640:480:force_original_aspect_ratio=decrease,pad=640:480:(ow-iw)/2:(oh-ih)/2` to `scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2`. The 720p sources in the catalog (1280x720, 854x480, 720x576) were being downscaled to 640x480; now they stay at full quality. The 1660 Ti's NVENC handles 720p easily.
2. **Weather slate NOT changed** — it still renders at 640x480. Its drawtext positions are tuned for that canvas; changing it would need a layout retune. Noted as a follow-up if wanted.
3. Version bumped 0.3.0.4 → **0.3.0.5** (csproj Version/AssemblyVersion/FileVersion + meta.json). Meta changelog rewritten for the 720p change.

## Build

- `dotnet build -c Release`: **0 errors, 1 warning** — CS8602 nullable dereference at `LiveTv/LiveTvController.cs:625` (`proc.HasExited` in the probe-timeout cleanup). Pre-existing code from the 0.3.0.2 probe fix; this change touches only the scale-filter string. Same warning as 0.3.0.4.
- DLL: 2,985,472 B (same as 0.3.0.4 — the change is a string literal).
- Package (manual, same as 0.3.0.4 — never build.sh): `dist/retro-tv_0.3.0.5.zip`
  - **986,936 B, MD5 a1b5e2772283c517b05f487d9c8d47f7, SHA256 3b930810dfd261acb645d8934c568f9b3e656a299870b6e2808ee443befedc20**

## Load test (test server, stopped by PID after)

- Plugin loads as **0.3.0.5** (log: "Loaded plugin: Retro TV 0.3.0.5").
- `/RetroTv/Guide/Themes` 200: **7 themes**.
- M3U works (154 channels).
- Stream ch28 (drive-in-movies): HTTP 200, 3,288,872 B, **valid MPEG-TS, h264 1280x720 — the 720p output is VERIFIED** (ffprobe: `h264,1280,720`).

## What 0.3.0.5 does NOT change vs 0.3.0.4

- Everything else from 0.3.0.4 is unchanged: 7 guide themes, generic settings, Getting-started box, copyable tuner links, one-click theme apply, 154 channels, commercial breaks, 96 h guide, probe-timeout fix.

## Manifest

- Prepared entry (NOT pushed — the v0.3.0.5 release doesn't exist yet): version 0.3.0.5, checksum 3b930810…, sourceUrl …/releases/download/v0.3.0.5/retro-tv_0.3.0.5.zip. Push it the moment the release exists.

## Release steps

See `~/workspace/your_files/retro-tv-0305-release-steps.md` (Michael creates the release; Jarvis pushes the manifest entry after).
