# Retro TV plugin — 0.2.0.0 build + load-test record

Date: 2026-11-08 (evening, EST). Builder: Jarvis subagent (session 6cfb1844).
Authorizes: Michael 2026-11-08 ~18:12 ("Id like to make a plugin so others
can use it with the channel guide feature") for the BUILD, and ~18:13
("Build it and give me the Repo link to add to Jellyfin") for the PUBLISH.
This is the single permitted retry of both — each was refused twice before
(build ~15:44, publish 17:46 and 18:07).

## What 0.2.0.0 adds (over the 0.1.0.0 skeleton)

The channel guide, inside the plugin — no sidecar needed:

- **Lineup** (`Guide/Channels/lineup.json` + `ChannelLineup.cs`): BackWhen-
  shaped, kids first, continuous numbering from 4. 9 channels live at
  launch: 17 Saturday Morning Cartoons, 25 Popeye, 30 Classic TV,
  79 Drive-In Movies, 83 Haunted Drive-In, 85 Sci-Fi Theater, 125 Weather,
  130 Ad Break, 147 Prelinger Theater. The rest of the BackWhen lineup
  (single-character cartoon channels, genre-split movies, per-year VHS
  Vault 1990–1999, soaps, music, etc.) is defined and goes live
  automatically once the catalog has the depth — channels with <1 pool
  item stay inactive. Soaps (General Hospital, Dark Shadows) are
  user-uploaded tier and disabled until that tier has episodes.
  Pluto streams are never included (IA-only video).
- **Schedule engine** (`Guide/ScheduleEngine.cs`): deterministic 48 h
  schedules, anchored 2026-11-08T00:00:00Z (same anchor as the sidecar,
  so guide and streams can never disagree). Shared-clock model: tuning
  in joins the programme already in progress. Soaps play oldest first
  (dormant `oldestFirst` flag on the soap channels).
- **Guide service** (`Guide/GuideService.cs`): pools from the plugin data
  dir or the embedded seed, refreshed from `catalog.json` after every
  catalog build; builds the guide JSON, M3U, and XMLTV.
- **Guide page** (`Guide/guidePage.html` + `Guide/GuideController.cs`):
  themeable, three built-in themes — Woodgrain '78 (default), Neon
  Nights, VHS Blue Screen. Anonymous endpoints: `/RetroTv/Guide`,
  `/RetroTv/Guide/Data`, `/RetroTv/channels.m3u`, `/RetroTv/guide.xml`,
  `/RetroTv/Guide/Themes`, `/RetroTv/Guide/Theme?theme=<id>` (preview,
  does not save), `/RetroTv/Guide/Icon`, `/RetroTv/Guide/Weather`.
- **Live streams** (`LiveTv/LiveTvController.cs`): per-request ffmpeg
  stitching of the programme in progress, MPEG-TS H.264 + AAC 640×480
  30 fps. NVENC when the server's ffmpeg has a working `h264_nvenc`
  encoder, libx264 otherwise. Batch = up to 7200 s of programmes.
- **Weather channel** (`Guide/WeatherChannel.cs` + `Weather/`):
  Open-Meteo, free, keyless, zip-code based (default 05773 Wallingford
  VT — confirmed live via the geocode endpoint). Three rotating
  5-minute segments (Current Conditions / 7-Day Outlook / Hourly
  Forecast), 15-minute refresh, last-good fallback, stale >2 h marking.
- **Config page** (`Configuration/configPage.html`): channels switch now
  enabled (was disabled in 0.1.0.0), plus FfmpegPath and WeatherFontPath
  fields and the Live TV wiring section (M3U + XMLTV URLs for the
  server). Config persists across restarts (verified).
- **folder.jpg write**: the catalog builder now writes the v5 icon as
  `folder.jpg` at the library root (the one gap in 0.1.0.0).
- **Icon**: v5 only — TV on a dual-deck VCR with a VHS tape + a BETA
  tape beside it, RETRO TV lettering. Canonical
  `retro-tv-icon.webp`, SHA256
  `459bfc57b822f162dac22011ec0695edb520a8b46b719ab061825ddd283b363c`
  (1600×1600, 228,246 B). Manifest `imageUrl` points at the raw GitHub
  URL; the plugin package carries `icon.webp` alongside the DLL.

## Build

- Toolchain: .NET SDK 10.0.403, net10.0, Jellyfin.Controller/Model 12.4.0.
- `dotnet build -c Release`: **0 warnings, 0 errors**.
- Zip: `dist/retro-tv_0.2.0.0.zip` — DLL (714,240 B) + `icon.webp`
  (228,246 B) + `meta.json`. **MD5 `aa44c4a64bfaa210ce2da14b8be61448`**.
- Manifest: `manifest.json` at the repo root, versions: 0.2.0.0 only.
  (The 0.1.0.0 zip no longer exists on disk — its manifest entry was
  dropped rather than published pointing at a release that can never
  be created. 0.1.0.0 was a skeleton superseded by 0.2.0.0; nothing
  was ever published for it.)
- The repo-layout copy of the plugin source (`repo/plugin/`) builds
  clean too (0/0) — the published tree is self-contained. Its zip
  differs from the load-tested one only in zip timestamps and DLL
  build metadata (MVID + embedded debug path); same source. The
  released zip is the load-tested workspace build.

## Load test (real Jellyfin 12.4.0 test server, ~/jellyfin-test/)

Server: Jellyfin 12.4.0 tarball, throwaway admin `testadmin` (test
server only — its password was reset locally for this test, never
recorded). Plugin installed in the data dir
(`data/plugins/RetroTv/` — the config-dir path is silently ignored
in 12.4).

| Check | Result |
|---|---|
| Plugin status | **Retro TV 0.2.0.0, Active** (before and after restart) |
| Pilot catalog build (API, `?pilot=true`) | **12 items, 0 skipped, 38.3 s**; 38 files (12×3 + catalog.json + folder.jpg) |
| folder.jpg | Written, 316,265 B, SHA256 `c9b38470…` — exact match to the live Dayz library's folder.jpg |
| Guide page `/RetroTv/Guide` | 200, 19,506 B |
| Guide data `/RetroTv/Guide/Data` | 200 — **9 channels, all with programmes, all with a now-playing programme**; 48 h window; anchor 2026-11-08T00:00:00Z |
| M3U `/RetroTv/channels.m3u` | 200, 9 channels, correct tvg-id / chno / stream URLs |
| XMLTV `/RetroTv/guide.xml` | 200, 1,794,012 B, 9 channels, **4,070 programmes** |
| Themes `/RetroTv/Guide/Themes` | 200, all 3; preview `?theme=vhs-blue-screen` returns that theme without changing the saved one |
| Icon `/RetroTv/Guide/Icon` | 200, 228,246 B (v5 webp) |
| Weather `/RetroTv/Guide/Weather` | 200 — **Wallingford, 42.3°F, Clear sky**, fetched live from Open-Meteo |
| Weather forecast `/RetroTv/Weather/Forecast` | 200 — live current + 7-day (max 52.5→38.8°F) |
| Geocode `/RetroTv/Weather/Geocode?zip=05773` | 200 — Wallingford, 43.47192, -72.97823, Open-Meteo geocoding |
| Live stream `/RetroTv/Live/saturday-morning-cartoons` | 200, **1,948,808 B in 25 s**, valid MPEG-TS (0x47 sync), H.264 640×480 30 fps + AAC stereo 48 kHz |
| Live stream `/RetroTv/Live/weather` | 200, **331,632 B in 20 s**, valid MPEG-TS, H.264 + AAC |
| Library scan | **12/12 pilot items indexed** (Retro TV library on the test output folder) |
| Config persistence | Theme change (neon-nights) + coordinates survived a server restart |
| Config API round-trip | GET/POST `/Plugins/{id}/Configuration` — 200/204 |

## Bugs found by the load test — all fixed in this build

1. **NVENC probe false positive** (stream returned 200 / 0 bytes,
   ffmpeg exit 255). The probe ran `ffmpeg -encoders` and grepped for
   `h264_nvenc` — ffmpeg lists the encoder whenever it was built with
   NVENC support, even on a machine with no NVIDIA GPU. Fixed: the
   probe now does a real 1-frame test encode
   (`nullsrc → h264_nvenc → null`); exit 0 means NVENC truly works.
   Plus a safety net: if a stream attempt with NVENC produces zero
   bytes, the batch retries once with libx264 (nothing reached the
   client yet, so the retry is safe).
2. **Concat seek produced zero bytes** (stream returned 200 / 0 bytes
   for the whole seek). The port used `-ss` before `-i` on a concat
   input — the demuxer then reads linearly to the seek point at
   real-time speed (a 370 s seek = ~370 s of silence). Fixed: the seek
   now lives in the concat list file as `inpoint` on the first file
   (the sidecar's documented lesson — first bytes in ~0.5 s).
   Verified manually: `-ss` shape 0 B in 27 s; `inpoint` shape
   1,307,540 B in 20 s.
3. **Weather slate ffmpeg exit 234** (`No option name near '480:r=30'`).
   The lavfi color source used `s=640:480` — `:` is the option
   separator, so the size must be `640x480`. Fixed.
4. **ExitCode race**: reading `proc.ExitCode` after a cancelled
   `WaitForExitAsync` threw `InvalidOperationException` ("Process must
   exit before requested information can be determined"). Fixed: only
   read `ExitCode` when `proc.HasExited`, else -1.

## Publish

- Repo: **https://github.com/athlon1982/retro-tv** (PUBLIC).
- Manifest URL for Jellyfin (Dashboard → Plugins → Repositories):
  **https://raw.githubusercontent.com/athlon1982/retro-tv/main/manifest.json**
- Icon URL: `https://raw.githubusercontent.com/athlon1982/retro-tv/main/assets/icon.webp`
- Release: **v0.2.0.0** — see the publish section below for what the
  connector could and could not do.
- Secret scan before pushing: no secrets, no LAN IPs, no Jellyfin API
  keys anywhere in the pushed files. `config.local.json` and
  `config.full.json` were NOT pushed (host-specific paths; the
  full-catalog job is running against `config.full.json` right now).
  Build outputs (`bin/`, `obj/`, `dist/`) were NOT pushed and are now
  in `.gitignore`. Example paths in comments/docs are Windows drive
  letters only (`F:\IA-Channels\`, `F:\Jellyfin\Server\ffmpeg.exe`) —
  no hostnames, IPs, or credentials.

### Publish result — PARTIAL, blocked on repo creation (2026-11-08 ~23:50)

- GitHub connector: authenticated as **athlon1982** (confirmed via
  `get_me`), App can read the account's repos (listed commits on
  `athlon1982/BeatSyncStudioPro`). No `retro-tv` repo exists yet.
- **Repo creation FAILED: `create_repository` returned 403 "Resource
  not accessible by integration".** The Muse GitHub App installation
  cannot create repositories under this account. This is a connector /
  App permission limit, not a safety refusal — but the effect is the
  same for this step: the repo does not exist, so nothing could be
  pushed. Do NOT retry `create_repository` with different parameters;
  the 403 is about the integration's access, not the arguments.
- **What still needs a human / the browser route:**
  1. Create the empty PUBLIC repo `athlon1982/retro-tv` in the GitHub
     web UI (github.com/new — owner athlon1982, name retro-tv, Public,
     do NOT initialize with a README; the push below supplies everything).
     The Muse GitHub App must also be installed on it (App settings →
     repository access) or the push in step 2 will fail the same way.
  2. Push the text files: the paths manifest is ready at
     `~/workspace/jellyfin-ia-channels/retro-tv-push-paths.txt`
     (NUL-delimited, 39 files including this record — see below) for
     `github push-files --owner athlon1982 --repo retro-tv --branch main
     --message "Retro TV 0.2.0.0 — channel guide" --source-dir
     ~/workspace/jellyfin-ia-channels/repo --paths-file <that file>`.
  3. Upload the 2 binary files via the GitHub web UI (the connector
     cannot push binaries — `push_files` / `create_or_update_file`
     are UTF-8 text only): `assets/icon.webp` (228,246 B, v5) and
     `plugin/Jellyfin.Plugin.RetroTv/Assets/folder.jpg` (316,265 B).
     The icon MUST be the v5 file (SHA256 `459bfc57…363c`); folder.jpg
     is the JPEG render the plugin writes as the library poster.
  4. Create release **v0.2.0.0** in the web UI (Releases → Draft a new
     release → tag `v0.2.0.0`) and attach
     `plugin/src/Jellyfin.Plugin.RetroTv/dist/retro-tv_0.2.0.0.zip`
     (MD5 `aa44c4a64bfaa210ce2da14b8be61448` — the load-tested build).
     There is NO `create_release` tool in the connector, and the zip
     cannot go up through `push_files` / `create_or_update_file`
     (binary). This step is web-UI-only.
  5. Verify: `https://raw.githubusercontent.com/athlon1982/retro-tv/main/manifest.json`
     returns HTTP 200, valid JSON, checksum `aa44c4a6…` matching the
     release zip; `…/main/assets/icon.webp` returns HTTP 200.
- Until steps 1–4 are done, **the manifest URL and the icon URL both
  404** — the URLs in this record and in the READMEs are the intended
  final addresses, not live ones yet. Do not present them to Michael
  as working links; present the four steps above.
- The repo tree on disk (`~/workspace/jellyfin-ia-channels/repo/`) is
  complete and verified: secret scan clean, builds clean standalone,
  38 text files + 2 binaries ready.

## Known limitations / notes

- The guide weather endpoint reads only the cached forecast: the first
  call after a restart (or after coordinates first appear) returns
  `condition: "unavailable"` and triggers a background refresh; the
  next call (within 15 min) shows live data. By design — the guide
  page tolerates it.
- Posting a partial config through the plugin-configuration API resets
  omitted fields (the weather coordinates were wiped that way once
  during testing and re-saved via geocode). The config page always
  sends the full config, so normal use is unaffected.
- The Jellyfin 12 auth header is `Authorization: MediaBrowser ...` —
  `X-Emby-Authorization` is silently ignored (cost one 400 during
  testing; documented in PLUGIN-DESIGN.md §12).
- The test server's `testadmin` password was reset locally for this
  test (throwaway server, no real data). The live Dayz Jellyfin was
  never touched: no install, no repository added, no library changed,
  no reboot.
- Nothing was deleted. The sidecar's work and the full-catalog agent's
  work were not touched.
