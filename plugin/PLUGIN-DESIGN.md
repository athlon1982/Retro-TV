# Retro TV — Native Jellyfin Plugin Design

**Status:** design + compiling skeleton (v0.1.0.0). **Written:** 2026-11-08.
**Target server:** Jellyfin 12.4 (.NET 10.0.14) on Dayz. **Nothing is deployed
anywhere.** No GitHub push, no install on the live server, no restart.
Publishing waits for Michael's word (GitHub post held until the pilot plays
and the full version is ready — his decision 2026-11-08).

**What this plugin is:** a native C# Jellyfin server plugin that brings Retro
TV *inside* Jellyfin — the Internet Archive catalog builder (today a Node
script) becomes a plugin scheduled task + config page, and the Stage 2 linear
channels become plugin-served Live TV (M3U + XMLTV) with a themeable guide
and a weather channel. One install, one config page, no second service.

**What it is not:** it does not replace the Stage 1 `.strm` library (that
library keeps working exactly as it is — the plugin *writes* the same
`.strm`/`.nfo`/poster files into the same folder). It is not Tunarr, not
Nostalgex (cancelled, do not resume), not a fork of BackWhen TV.

---

## 1. Decisions (locked by Michael, 2026-11-08)

| # | Decision | Source |
|---|---|---|
| 1 | Name: **Retro TV** | 2026-11-08 ~13:41 |
| 2 | Both modes: on-demand library AND live channels | widget tap ~13:33 |
| 3 | On-demand first, then live channels | widget tap ~13:40 |
| 4 | Content: **don't leave anything out** — strict PD/CC base PLUS user-uploaded classic TV as a **separate clearly-marked tier** (`pd` vs `user-uploaded`); never present user-uploaded items as public domain | ~13:40 |
| 5 | GitHub post WAITS until the pilot plays and the full version is ready | widget tap ~15:07 |
| 6 | Real plugin with a repository link, built in parallel with the *arr/Jellyfin fixes | ~15:07 "Lets work on this while your fixing..." |
| 7 | Themeable channel guide (theme system, switchable looks) | ~15:09 |
| 8 | Weather channel by zip code, default = home zip | ~15:09 |

---

## 2. Architecture

```
Jellyfin 12.4 server (Dayz, .NET 10)
└── Jellyfin.Plugin.RetroTv (this plugin, net10.0, Jellyfin.Controller/Model 12.4.0)
    ├── Plugin.cs                  — BasePlugin<PluginConfiguration>, IHasWebPages
    ├── PluginConfiguration.cs     — persisted config (see §6)
    ├── PluginServiceRegistrator.cs— DI: HttpClient factory, IA client, catalog builder, weather client
    ├── Configuration/configPage.html (+ configPage.js) — dashboard config UI (embedded resources)
    ├── Catalog/
    │   ├── CatalogEntry.cs        — one catalog item (matches catalog.json from the Node builder)
    │   ├── InternetArchiveClient.cs — advancedsearch + metadata, 1 req/s, 429/503 backoff
    │   ├── CatalogBuilder.cs      — the port of src/catalog_builder.mjs: license filter,
    │   │                            MP4 scoring, .strm/.nfo/poster writing, catalog.json versioning
    │   └── NfoWriter.cs           — .nfo XML (same shape as the Node builder)
    ├── Tasks/BuildCatalogTask.cs  — IScheduledTask: runs the builder on a schedule / on demand
    ├── Guide/
    │   ├── ScheduleEngine.cs      — (Stage 2) deterministic 48 h schedules, pure function of
    │   │                            channel + wall-clock, so restarts never desync guide vs stream
    │   ├── GuideService.cs        — (Stage 2) serves schedule data to the guide page + XMLTV
    │   ├── Themes/Theme.cs        — theme definition model + loader (embedded + user themes)
    │   ├── Themes/builtin/*.json  — starter themes (§7)
    │   └── guidePage.html         — (Stage 2) the themeable guide UI (embedded resource)
    ├── Weather/
    │   ├── OpenMeteoClient.cs     — (Stage 2) geocoding + forecast, keyless (§8)
    │   └── WeatherChannel.cs      — (Stage 2) forecast → channel segments + guide entries
    ├── LiveTv/                    — (Stage 2) M3U + XMLTV endpoints + ffmpeg stitching (§5)
    └── Api/RetroTvController.cs   — admin-only API: build now, status, guide data, theme switch
```

### 2.1 Defensive rules (a crashing plugin takes Jellyfin down)

These are design constraints, not aspirations — every later slice must hold
them:

1. **The plugin constructor does no I/O.** No network, no disk, no config
   migration in the constructor. All work happens in tasks / API calls.
2. **Every external call is isolated.** IA, Open-Meteo, and ffmpeg calls
   run inside try/catch with a timeout; a failure is logged and the plugin
   keeps serving (last-good catalog, last-good forecast, other channels
   unaffected). The scheduled task **never rethrows** — it logs and returns.
3. **Never block server startup.** No `IServerEntryPoint` that waits on
   the network. Background work starts lazily (first task run / first API
   call), never in `Plugin.OnStartup` equivalents.
4. **No unbounded work on the request path.** API endpoints return fast;
   long work (catalog build, channel render) runs on a background task with
   a "already running" guard — a second trigger while one is running returns
   409, it never starts a duplicate.
5. **Config is validated on save.** Bad output paths, bad zip codes, bad
   theme IDs are rejected with a message; the previous good config is kept.
6. **Logs are clear and greppable.** Every log line carries a `[RetroTv]`
   prefix and the component name, so a failure is findable in the Jellyfin
   log without guessing.
7. **The plugin never touches another library.** It writes only its own
   output folder (default `F:\IA-Channels\` on Dayz, configurable) and its
   own plugin data directory. It never edits `library.db` directly, never
   deletes media, never restarts the server.

### 2.2 What the plugin reuses vs what it replaces

| Existing piece | Fate in the plugin build |
|---|---|
| `src/catalog_builder.mjs` (Node) | **Ported** to `CatalogBuilder.cs` — same queries, same license filter, same MP4 scoring, same file layout, same `catalog.json` shape. The Node builder stays in the repo as the reference implementation and the standalone fallback. |
| `config.example.json` (10 folders, pilot list, prune, overrides) | **Adopted as the default plugin config** — the folder list, pilot list, prune list, and title/year overrides move into `PluginConfiguration` (serialized JSON fields). Same defaults, same behavior. |
| `F:\IA-Channels\` pilot (12 items, `.strm` + `.nfo` + poster) | **Untouched.** The plugin reads the same folder; a plugin catalog build writes the same layout and versions `catalog.json` the same way (`catalog.prev.json` backup). |
| `assets/icon.webp` (v5 icon) | **The plugin icon** — copied into the plugin project, referenced by `meta.json`/`manifest.json` `imageUrl` once hosted, and shown on the config page. |
| iptv-org M3U + XMLTV install (Dayz :3000) | **Pattern reused, not touched.** Stage 2 serves its own M3U + XMLTV the same way Jellyfin already consumes them. |
| jellyfin-virtual-tv design | **Reference design for Stage 2** (deterministic schedules, ffmpeg concat pipeline, M3U/XMLTV outputs). Design only — any code reuse needs a license check of its LICENSE file at build time. |

---

## 3. On-demand slice (Stage 1, in-plugin) — the first buildable slice

This is what the v0.1.0.0 skeleton proves, and what v0.2.0.0 finishes:

1. **Config page** — output folder, library name, requests/second,
   MP4 preference, include-user-uploaded-tier toggle, folder list (JSON
   editor seeded from the 10 defaults), prune list, pilot list. Save →
   validated → persisted by Jellyfin's config store.
2. **Catalog build task** — `IScheduledTask` "Build Retro TV catalog"
   (default: daily, plus "Run now" from the config page / API). Runs the
   ported builder: IA search per folder → metadata per item → PD license
   filter for `pd` folders (licenseurl must contain `publicdomain`) →
   MP4 derivative pick (exact `<id>.mp4` wins, then derivatives, 512kb
   fallback) → writes `.strm` + `.nfo` + `poster.jpg` + versioned
   `catalog.json`. Progress reported through the task's `IProgress<double>`.
   Runs are idempotent: re-running does not duplicate items, and `prune`
   entries are never re-added.
3. **Build-now API** — `POST /RetroTv/Build` (admin only) starts a build
   if none is running; `GET /RetroTv/Status` returns last run (time, items,
   skipped, errors, catalog version). The config page uses these.
4. **Done when:** the plugin installs on a test Jellyfin, the task runs,
   the output folder contains the same file layout the Node builder makes,
   and Jellyfin scans it as the Retro TV library. (Install/test happens on
   a test server, never the live Dayz server, until Michael approves.)

**Out of this slice:** live channels, guide, themes beyond the config-page
proof (§7.4), weather (§8 is Stage 2). The skeleton (v0.1.0.0) compiles
with the builder + task + config page stubbed; v0.2.0.0 wires them fully.

---

## 4. Live channels (Stage 2) — design, not built yet

The plugin becomes a Live TV source the same way the iptv-org install did,
but from the IA catalog instead of a playlist file:

1. **Schedule engine** — deterministic 48-hour schedules per channel: a
   pure function of (channel, wall-clock time). A show is mid-programme
   when you tune in, like BackWhen's shared clock. Restarting Jellyfin or
   the plugin never desyncs the schedule, because the schedule is computed,
   not stored as a timeline that can drift.
2. **ffmpeg stitching** — one continuous stream per live channel: concat
   the scheduled files in small batches, re-encode to uniform H.264 +
   AAC LC stereo, hold timestamps stable across programme changes (the
   jellyfin-virtual-tv pipeline, adapted). NVENC on Dayz's GTX 1660 Ti is
   the target (driver 560.94 is PINNED — never touch it); verify NVENC
   availability to the plugin's ffmpeg at build time. Start with 3
   channels; grow only after load is proven. Steal MosaicTV's trick if
   always-on load proves heavy: only encode channels someone is actually
   watching.
3. **M3U + XMLTV** — the plugin serves `GET /RetroTv/channels.m3u` and
   `GET /RetroTv/guide.xml` (same shape Jellyfin already consumes from
   iptv-org). Add as an M3U tuner + XMLTV provider in the dashboard
   (or, later, via the plugin's own Live TV registration — see §4.4).
   **Refresh Channels first, then Refresh Guide**, or the guide comes up
   blank (the documented gotcha).
4. **Starter lineup** (from SCOPE.md §5, plus the weather channel §8):

   | # | Channel | Source | Notes |
   |---|---|---|---|
   | 1 | Saturday Morning Cartoons | `animationandcartoons` + PD filter | Flagship |
   | 2 | Classic TV | `classic_tv` + PD filter | Sitcoms, westerns, detectives |
   | 3 | Drive-In Movies | `feature_films` + PD filter | Noir, horror, sci-fi, westerns |
   | 4 | Prelinger Theater | `prelinger` | Most reliably PD collection on IA |
   | 5 | Ad Break | `classic_tv_commercials` + PD filter | Vintage ads; filler pool for all channels |
   | 6 | Creature Features | `feature_films` + PD filter, horror/sci-fi | Late-night companion to #3 |
   | 7 | VHS Vault | `vhsvault` + PD filter, year blocks 1990–1999 | Only its PD slice — the license filter is load-bearing |
   | 8 | **Weather — Wallingford, VT** | Open-Meteo (§8) | The weather channel; appears in the guide like any other channel |
   | — | *Not in v1* | Soaps, Nick/MTV blocks, anime | User-uploaded tier channels — the on-demand library carries them; linear inclusion is a later decision, same tier rules |

### 4.4 Open question (Stage 2 build time, not now)

Whether the plugin registers itself as a Live TV tuner/guide provider
through Jellyfin's plugin Live TV surface (`ITunerHost` /
`IListingsProvider` — the surface is thin and built for external tuners)
or simply serves M3U + XMLTV for the dashboard to consume as a tuner.
The M3U + XMLTV route is the default (proven by iptv-org, no new API
surface to fight). The native registration is evaluated only if the M3U
route shows a concrete limitation.

---

## 5. Themeable channel guide (Michael, 2026-11-08 ~15:09)

**Requirement:** the channel guide must be able to change looks like
themes — a theme system with switchable themes.

### 5.1 The honest constraint

Jellyfin's built-in Live TV guide is rendered by Jellyfin's own web
client, and Jellyfin 12 serves `index.html` as a plain static file with
no native plugin injection hook. **A plugin cannot re-theme Jellyfin's
built-in guide.** So the themeable guide is the plugin's *own* guide
surface: a standalone guide web page served by the plugin itself
(`GET /RetroTv/Guide`), showing the same schedule data the XMLTV feed
carries, rendered as a time grid. The plugin's config page links to it,
and it works on any device with a browser. The built-in Jellyfin guide
keeps working untouched alongside it.

### 5.2 Mechanism

- **Theme definition = one JSON file per theme.** Shape:
  ```jsonc
  {
    "id": "woodgrain-78",
    "name": "Woodgrain '78",
    "description": "Warm 70s console TV — walnut, cream, amber.",
    "colors": {
      "background": "#2b1d12", "panel": "#3d2b1a", "text": "#f3e6c8",
      "mutedText": "#c9a86a", "accent": "#e8a33d", "header": "#1d130a",
      "gridLine": "#5a4026", "nowLine": "#ff5a3c", "programBlock": "#4a3220",
      "programBlockHover": "#5d3f27", "liveBadge": "#c0392b"
    },
    "fonts": { "display": "Georgia, 'Times New Roman', serif", "body": "Georgia, serif", "mono": "'Courier New', monospace" },
    "density": "comfortable",        // "compact" | "comfortable" | "roomy"
    "showPosters": true,             // program blocks carry poster art
    "crtOverlay": { "enabled": true, "scanlines": 0.25, "vignette": 0.35, "trackingNoise": 0.1 },
    "channelColumnWidth": 220
  }
  ```
- **Where themes live.** Built-in themes are embedded resources in the
  plugin assembly (`Guide/Themes/builtin/*.json`) — they always exist and
  cannot be broken by a bad file. User themes are optional JSON files
  dropped into a `Themes` folder under the plugin's data directory; the
  plugin loads them at startup, validates each against the schema, and
  **ignores any invalid file with a log line** (a bad theme never breaks
  the guide — it falls back to the default theme).
- **How the user picks/switches.** The plugin config page has a theme
  dropdown (built-ins + any valid user themes) with a live preview swatch.
  Changing it saves `PluginConfiguration.ActiveGuideTheme` (the theme id)
  and the guide page applies it on next load — **no Jellyfin restart**.
  The guide page also has its own theme switcher in the corner, so anyone
  looking at the guide can flip themes without opening the dashboard.
  The choice is stored server-side (plugin config), so every device sees
  the same theme; a `?theme=<id>` URL parameter overrides it for one view
  only (handy for showing Michael a theme without changing the saved one).
- **How the guide applies a theme.** The guide page is one HTML file +
  one JS file. The JS fetches the active theme JSON from
  `GET /RetroTv/Guide/Theme` (or the `?theme=` override), then sets CSS
  custom properties (`--rtv-bg`, `--rtv-accent`, …) on the page root and
  toggles the CRT overlay layer. All themeable styling goes through those
  variables — no per-theme HTML. The guide data comes from
  `GET /RetroTv/Guide/Data?hours=48` (channels + programs + now-line),
  the same schedule the XMLTV feed is built from, so the themed guide and
  Jellyfin's built-in guide can never disagree.
- **Config page shares the theme.** The config page uses the same CSS
  variables (light application: header + accents), so the plugin feels
  like one product. This is the proof of the theme plumbing in the
  skeleton: the config page reads the active theme and applies it.

### 5.3 Starter themes (3 — all retro, all distinct)

| # | Theme | Look | When it fits |
|---|---|---|---|
| 1 | **Woodgrain '78** (default) | Warm walnut browns, cream text, amber accent, serif display type, gentle scanlines + vignette. The 70s console TV in the living room. | The default. Warm, readable, on-brand. |
| 2 | **Neon Nights** | Near-black navy background, hot-pink + cyan accents, glow on the now-line and live badges, condensed display type. The 80s/90s video-store window. | Night viewing; the flashy one. |
| 3 | **VHS Blue Screen** | The classic VCR blue screen: solid blue background, white monospace text, "PLAY" style badges, minimal decoration, tracking-noise shimmer. High contrast, dead readable. | Accessibility / daylight; the joke that is also genuinely usable. |

Each theme also sets `density`, `showPosters`, and the CRT overlay
intensity, so switching themes changes feel, not just colors.

### 5.4 What lands when

- **Skeleton (v0.1.0.0):** theme model + loader + the 3 built-in theme
  JSONs + `ActiveGuideTheme` config field; the config page applies the
  active theme's variables (proof the plumbing works). No guide page yet.
- **Stage 2:** the guide page itself + guide data API + the in-guide
  theme switcher + user-theme folder loading.

---

## 6. Weather channel by zip code (Michael, 2026-11-08 ~15:09)

**Requirement:** a dedicated weather channel in the lineup, driven by a
configurable zip code (default = Michael's home zip).

### 6.1 Source decision — Open-Meteo (chosen), NWS (alternative)

| | Open-Meteo (chosen) | US National Weather Service (alternative) |
|---|---|---|
| Key | **None — keyless** | None — keyless |
| Geocoding (zip → lat/lon) | **Built in** — `geocoding-api.open-meteo.com/v1/search` takes a postal code | Not provided — needs a separate geocoder |
| Forecast call | One call, lat/lon → current + hourly + daily | 2-hop chain: point → gridpoint → forecast |
| Headers | No User-Agent requirement (polite to send one) | Descriptive User-Agent **required** |
| Limits (free) | 10,000 calls/day, 600/min, 5,000/hr — we need ~100/day | Generous, but rate limits are per-endpoint and undocumented in one place |
| Data license | CC BY 4.0 — attribution required ("Weather data by Open-Meteo.com") | Public domain (US government work) |
| Extra | Global, simple JSON, 16-day forecast | Official US source; **alerts + forecaster discussion text** |

**Why Open-Meteo:** one provider covers geocoding *and* forecast, no key,
no mandatory headers, trivial JSON, and the free quota is ~100× what the
channel needs. **NWS stays the documented alternative** if alerts or the
human-written forecast discussion become wanted later (an NWS alerts
crawl is a clean add-on: it only needs the same cached lat/lon).

**Terms (verified 2026-11-08):** Open-Meteo free tier is for
non-commercial use, no API key, 10,000 calls/day. Data is CC BY 4.0 — the
channel, the guide, and the config page must all carry the attribution
"Weather data by Open-Meteo.com". If Retro TV ever becomes a commercial
product, the weather source must be re-licensed — noted here so it is not
discovered later.

### 6.2 Zip code — verified, not guessed

**Default zip: `05773` (Wallingford, VT).** Verified 2026-11-08 against
Wikipedia (Wallingford, Vermont — ZIP Codes 05773, 05742 East
Wallingford) and multiple ZIP directories. Michael's home is 64 School
Street, Wallingford — the main village, ZIP 05773. (East Wallingford is
a separate ZIP, 05742 — not his.) Town centroid is roughly 43.44, -72.95.

### 6.3 Mechanism

- **Config:** `WeatherZipCode` (default `"05773"`), plus cached
  `WeatherLatitude` / `WeatherLongitude` / `WeatherPlaceName` (resolved
  once, stored — see below). Changing the zip re-resolves on save.
- **Geocode once, cache forever (until the zip changes).**
  `GET https://geocoding-api.open-meteo.com/v1/search?name=05773&count=1&language=en&countryCode=US`
  → take the first result's `latitude`/`longitude`/`name`. Store them in
  plugin config. **Forecast refreshes use the cached coordinates only —
  the geocoder is never called per refresh.** This sidesteps the known
  Open-Meteo geocoder weakness (it is place-name oriented and misses many
  postcodes): the postcode only has to resolve once, at config time, and a
  human (Michael) can see the resolved place name on the config page and
  correct it if it lands wrong.
  **Fallback geocoder:** Zippopotam.us — `GET https://api.zippopotam.us/us/05773`
  (free, no key) — used automatically if Open-Meteo geocoding returns no
  result for the zip.
- **Forecast refresh:** every 15 minutes (scheduled task, shared with the
  schedule build — the weather channel's segments are rebuilt from the
  latest forecast). One call:
  `GET https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m&hourly=temperature_2m,precipitation_probability,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=America%2FNew_York&forecast_days=7`
  Imperial units, `America/New_York` timezone (Michael's timezone).
  The forecast JSON is cached in the plugin data dir with a timestamp.
- **What the channel shows.** A linear channel needs a continuous video
  stream, so the weather channel is a rendered loop, rebuilt on each
  refresh: current conditions (temp, feels-like, wind, humidity, condition
  icon) + 7-day outlook + hourly strip, as a ~5-minute looping segment.
  The guide lists it like any other channel, with programs named
  "Current Conditions", "7-Day Outlook", "Hourly Forecast" so the guide
  grid reads naturally. **Rendering approach is TBD at Stage 2 build
  time** — leading candidate is ffmpeg `drawtext` over a themed
  background still (no new dependency, works headless); a headless-browser
  screenshot is the fallback if drawtext cannot carry the layout.
  `System.Drawing` is explicitly out (not supported server-side on Linux).
- **Failure modes (defensive, §2.1):** API down → keep serving the last
  good forecast and log; zip invalid → config save is rejected, old zip
  kept; coordinates missing → the channel shows a "weather unavailable"
  slate and the other 7 channels are unaffected; forecast stale > 2 h →
  the guide marks the channel's programs "forecast as of <time>".
- **Attribution:** "Weather data by Open-Meteo.com" on the channel slate,
  in the guide footer, and on the config page (CC BY 4.0).

### 6.4 What lands when

- **Skeleton (v0.1.0.0):** `WeatherZipCode` (default `05773`) +
  cached-coordinate fields in `PluginConfiguration`; the geocoding +
  forecast client classes exist as compilable stubs with the real
  endpoints and request shapes. No channel yet.
- **Stage 2:** live refresh task, loop rendering, guide entries, channel
  in the M3U/XMLTV lineup.

---

## 7. Configuration model (`PluginConfiguration`)

Persisted by Jellyfin's plugin config store. Defaults mirror
`config.example.json` from the catalog builder.

| Field | Type | Default | Notes |
|---|---|---|---|
| `OutputFolder` | string | `""` (empty = plugin data dir `RetroTv` until set) | Where `.strm`/`.nfo`/poster + `catalog.json` go. On Dayz this will be `F:\IA-Channels\`. Validated on save (must exist or be creatable). |
| `LibraryName` | string | `"Retro TV"` | Written into `catalog.json`; the Jellyfin library keeps this name. |
| `RequestsPerSecond` | double | `1` | IA politeness. Backs off on 429/503 regardless. |
| `Mp4Preference` | string | `"full"` | `"full"` MP4 first, 512kb fallback (same scoring as the Node builder). |
| `IncludeUserUploadedTier` | bool | `true` | Michael's "don't leave anything out". `false` drops the whole tier from builds (the `pd` tier is untouched). |
| `FoldersJson` | string | the 10 folders from `config.example.json` | Folder name, tier, IA Lucene query, limit. Edited as JSON on the config page (validated on save). |
| `PilotJson` | string | the 12 pilot items | Curated items for pilot builds. |
| `PruneJson` | string | `[]` | Identifiers never re-added. |
| `OverridesJson` | string | the 3 title/year overrides | Curated title/year fixes. |
| `ActiveGuideTheme` | string | `"woodgrain-78"` | Theme id (§5). |
| `WeatherZipCode` | string | `"05773"` | §6. Changing it re-geocodes on save. |
| `WeatherLatitude` / `WeatherLongitude` | double? | `null` until first geocode | Cached coordinates (§6.3). |
| `WeatherPlaceName` | string | `""` | Resolved place name, shown on the config page. |
| `AutoBuildEnabled` | bool | `true` | Daily scheduled catalog build on/off. |
| `ChannelsEnabled` | bool | `false` | Stage 2 master switch — live channels off until Stage 2 ships. |

No API keys, no passwords, no tokens anywhere in config or code. The
IA and Open-Meteo calls need no credentials at all.

---

## 8. Repository + manifest (how the link will work)

Jellyfin's **Dashboard → Plugins → Repositories** consumes a repository
manifest: a JSON array of plugin entries. The official manifest
(`https://repo.jellyfin.org/releases/plugin/manifest.json`, read
2026-11-08) establishes the exact schema — one entry per plugin:

```json
[
  {
    "guid": "25eef671-4023-4c23-badb-6c051fa0620f",
    "name": "Retro TV",
    "description": "Turns the Internet Archive into a retro-video library and live channels inside Jellyfin.",
    "overview": "Retro TV — Saturday-morning cartoons, classic TV, drive-in movies and more, from the Internet Archive.",
    "owner": "Michael DeCato",
    "category": "MoviesAndShows",
    "imageUrl": "https://raw.githubusercontent.com/<owner>/retro-tv/main/assets/icon.webp",
    "versions": [
      {
        "version": "0.1.0.0",
        "changelog": "Skeleton — compiles; catalog builder ported; no live channels yet.",
        "targetAbi": "12.0.0.0",
        "sourceUrl": "https://github.com/<owner>/retro-tv/releases/download/v0.1.0/retro-tv_0.1.0.0.zip",
        "checksum": "<md5 hex of the zip>",
        "timestamp": "2026-11-08T00:00:00Z"
      }
    ]
  }
]
```

Rules the manifest must follow (verified against the official manifest):

- **Top level is a JSON array**, one object per plugin (not an object with
  a key). Jellyfin parses it as a list.
- **`guid`** is the plugin's permanent identity — generated once
  (`25eef671-4023-4c23-badb-6c051fa0620f`), it must never change between
  versions. It matches the `Id` in `Plugin.cs` and the `guid` in
  `meta.json`.
- **`targetAbi` is `12.0.0.0`** for Jellyfin 12.x (confirmed: Fanart
  15.0.0.0, IMVDb 6.0.0.0, Kodi Sync Queue 16.0.0.0 all ship
  `targetAbi: 12.0.0.0` against Jellyfin 12). The plugin targets
  `net10.0` with `Jellyfin.Controller` / `Jellyfin.Model` **12.4.0**
  (verified on NuGet 2026-11-08).
- **`checksum` is the MD5 hex of the release zip** (the official
  manifest's checksums are 32 hex chars — MD5). The build script computes
  it with `md5sum` and writes it into the manifest.
- **The release zip** contains the plugin DLL + `meta.json` (+ icon) at
  its root, named `<plugin>_x.y.z.w.zip` — Jellyfin installs from the zip.
- **`sourceUrl` / `imageUrl` are `<owner>` placeholders until publish.**
  The owner/repo name is not decided (Michael's GitHub identity for this
  project is his call). The moment the repo is public, the URLs become:
  `https://github.com/<owner>/retro-tv/releases/download/v<ver>/retro-tv_<ver>.zip`
  for the zip, and the manifest itself is hosted at
  `https://raw.githubusercontent.com/<owner>/retro-tv/main/manifest.json`
  — **that raw URL is the link Michael pastes into Dashboard → Plugins →
  Repositories**. The build script (`build.sh`) regenerates the manifest
  from `meta.json` + the computed checksum, so the manifest is never
  hand-edited with a stale hash.
- **Per-plugin `meta.json`** (in the plugin folder, shipped inside the
  zip) carries the same fields as the manifest entry's first version
  (`guid`, `name`, `version`, `targetAbi`, `category`, `changelog`,
  `description`, `overview`, `owner`, `timestamp`) plus `status` and
  `autoUpdate`. Jellyfin reads it on install.

**What exists now (v0.1.0.0):** `meta.json` (real values, guid fixed),
`manifest.json` (real structure, placeholder URLs, checksum filled in by
the build script after the zip is made), `build.sh` (build + zip +
checksum + manifest regeneration). **Nothing is hosted, nothing is
pushed** — the link becomes real the day Michael says post.

---

## 9. Licensing — the GPL-3.0 fact (read before publishing)

The catalog builder (Node, standalone, no Jellyfin linkage) is MIT — that
stays. **The plugin is different:** it compiles against Jellyfin's
`Jellyfin.Controller` / `Jellyfin.Model` assemblies, which are GPLv3.
Per the plugin template's own license note, a plugin built against those
assemblies is itself GPLv3-licensed and must be open source under a
GPL-3.0-compatible license. So:

- The plugin subproject carries **GPL-3.0** (`src/Jellyfin.Plugin.RetroTv/LICENSE`,
  full GPL-3.0 text), not MIT.
- The repo as a whole becomes **dual-licensed**: MIT for the catalog
  builder + docs, GPL-3.0 for the plugin. The root README must state this
  clearly at publish time (a publish-time edit, listed in BUILD-PLAN.md).
- This is a *publishing* constraint, not a build constraint — it changes
  nothing about the code, only the license file and the README wording.
  Michael decides the GitHub owner/repo name when he says post; the
  license split is recorded here so it is not discovered the day of.

---

## 10. Build slices (summary — full plan in BUILD-PLAN.md)

| Slice | Version | What ships | Gate |
|---|---|---|---|
| **Skeleton** | 0.1.0.0 | Compiling plugin: identity, config + stub page, IA client, builder port, scheduled task, build-now API, theme model + 3 built-in themes + config plumbing, weather client stubs, `meta.json`, `manifest.json`, `build.sh` | `dotnet build` 0 errors in the VM **+ load test on a Jellyfin 12.4.0 test server — DONE 2026-11-08 (see §12). Pilot build 12/12, 0 skipped.** |
| **On-demand wired** | 0.2.0.0 | Builder + task + config page fully working end to end on a test Jellyfin; catalog output byte-comparable in layout to the Node builder's pilot | Core DONE in the 0.1.0.0 load test (plugin loads, task runs, pilot builds 12/12). Remaining: `folder.jpg` write from the icon, config-page UI exercised in the web client, library scan of the built output in Jellyfin |
| **Full catalog** | 0.3.0.0 | Full catalog build (all 10 folders) through the plugin; prune/curation round | Pilot plays (Michael's gate) → full build |
| **Live channels** | 0.4.0.0 | Schedule engine + ffmpeg stitching + M3U/XMLTV + guide page + theme switcher + weather channel loop | 3 channels play end-to-end across a programme change |
| **Repository** | 1.0.0.0 | First public release: zip + manifest + README license split + hosted manifest URL | Michael says post |

---

## 11. Honest gaps / open questions

- **Load test DONE 2026-11-08 (§12).** The skeleton was loaded into a
  real Jellyfin 12.4.0 test server in the VM and built the 12-item pilot
  end to end (12/12, 0 skipped) after one real bug was found and fixed
  (IA `length`-as-string — see §12). What is still unproven: the config
  page in the web client (the HTML/JS is written but was exercised only
  via the API, not in a browser), the library scan of plugin-built
  output in Jellyfin, and the `folder.jpg` write (the port does not write
  it yet — the Dayz pilot folder already has one from the Node builder).
- **The plugin template is stale** (pins 10.9.11/net9.0). The skeleton
  builds directly against the 12.4.0 packages and `net10.0`, not off the
  template — re-check the API surface against the 12.4.0 packages at each
  slice (the compiler is the check).
- **Live TV native registration** (§4.4) is undecided — M3U + XMLTV is the
  default; revisit only on a concrete limitation.
- **Weather loop rendering** (§6.3) — ffmpeg `drawtext` is the leading
  candidate; confirm at Stage 2 build time.
- **NVENC availability** to the plugin's ffmpeg under the pinned 560.94
  driver — verify at Stage 2 build time; never touch the driver.
- **GitHub owner/repo name** — Michael's call at publish time; all URLs
  are placeholders until then.
- **The `user-uploaded` tier in a public repo** — the tier is separable by
  design; whether it ships in the public release is Michael's call at
  publish time (the README already says to review it yourself).
- **Jellyfin 12 plugin facts** (net10.0, targetAbi 12.0.0.0) were
  re-verified for this build (NuGet 12.4.0 exists; official manifest shows
  12.0.0.0 ABIs). The `.NET 10 SDK 10.0.403` used for the build is recorded
  in BUILD-PLAN.md.

---

## 12. Verified at build time — 2026-11-08 load test (VM only)

A real Jellyfin 12.4.0 server was run in the VM
(`jellyfin_12.4-amd64.tar.gz`, 113,075,397 B, from repo.jellyfin.org;
test dirs under `~/jellyfin-test/`, throwaway admin `testadmin`, nothing
touched on Dayz). Results:

- **Plugin loads.** `Loaded plugin: "Retro TV" "0.1.0.0"`, status Active
  in `GET /Plugins` (7 plugins total). Scheduled task registered:
  `Daily trigger for "Build Retro TV catalog" set to fire at 2026-11-09
  03:00:00 UTC` — the `GetDefaultTriggers()` implementation works.
- **Install path is the DATA dir, not the config dir.** First install at
  `<configdir>/plugins/RetroTv/` was silently ignored (no log line at
  all). Jellyfin 12.4's plugin path is
  `<datadir>/plugins/<Name>/` — on Dayz that is
  `F:\JellyfinData\Server\plugins\RetroTv\`. The README install steps
  use the data-dir path.
- **No `Jellyfin.Api` NuGet package exists** (nuget.org returns
  BlobNotFound). `BaseJellyfinApiController` cannot be referenced by a
  plugin. The plugin's controller is a plain ASP.NET Core
  `ControllerBase` with `[ApiController]` + `[Route("RetroTv")]` +
  `[Authorize(Policy = "RequiresElevation")]` — Jellyfin discovers it
  from the plugin assembly (application parts) and it works:
  `GET /RetroTv/Status`, `GET /RetroTv/Guide/Themes`,
  `GET /RetroTv/Weather/Geocode`, `POST /RetroTv/Build` all answered
  200/202 with the admin token.
- **`IPluginServiceRegistrator` lives in `MediaBrowser.Controller.Plugins`**
  in 12.4 (not `MediaBrowser.Common.Plugins` — the old namespace does
  not exist). Fixed at build time; services register cleanly.
- **`IScheduledTask` in 12.4 requires `GetDefaultTriggers()`** (in
  addition to Name/Description/Category/Key/IsHidden/IsEnabled/
  ExecuteAsync). Implemented as a daily 03:00 trigger.
- **Auth header in Jellyfin 12 is `Authorization: MediaBrowser ...`, not
  `X-Emby-Authorization`.** The old header is silently ignored, which
  surfaces as `request.App` null (`400 Error processing request`) on
  `POST /Users/AuthenticateByName`. The `Authorization: MediaBrowser
  Token="<key>"` form from the connector doc is the working one.
- **Weather geocode verified live.** `GET /RetroTv/Weather/Geocode?zip=
  05773` → Wallingford, 43.47192, -72.97823, source "Open-Meteo
  geocoding". Zip 05773 is confirmed as Wallingford VT.
- **Themes verified live.** `GET /RetroTv/Guide/Themes` returns all 3
  built-ins (woodgrain-78, neon-nights, vhs-blue-screen);
  `GET /RetroTv/Guide/Theme` returns the active theme JSON with colors.
- **Pilot build end to end: 12 items, 0 skipped, 34.7 s.**
  Output: 37 files — 12 items x (.strm + .nfo + poster.jpg) +
  catalog.json, same folders (2/2/2/2/1/2/1) and tiers (pd 9,
  user-uploaded 3) as the Node pilot. The 12 `.strm` URLs are identical
  picks to the Node pilot's (including the 512kb fallbacks); spot-checked
  `206 video/mp4` after redirect. One `.nfo` inspected: well-formed,
  title/plot/runtime/genre/thumb/tags (`Retro TV`, `public domain`),
  `uniqueid type="archive.org"`.
- **Real bug found and fixed by the load test.** First pilot run:
  0 items, 12 skipped — every item failed in `MetadataAsync` with
  `The JSON value could not be converted to System.Nullable<Double>.
  Path: $.files[N].length`. The IA metadata API returns file `length`
  as a **string** for many items; a plain `double?` property throws and
  the item is skipped. The Node builder never sees this (JSON.parse is
  untyped). Fix: `LenientNullableDoubleConverter` (accepts JSON numbers
  and numeric strings; non-numeric reads as null) on `IaFile.Length`.
  Rebuilt, re-packaged (zip MD5
  `091022ebe70006353b6c9d6c8dc3083b`), redeployed, re-ran: 12/12.
  The defensive design worked as intended throughout — each failure was
  a logged per-item skip with the reason in catalog.json, never a crash.
- **Known gap from the test:** the C# port does **not** write
  `folder.jpg` at the library root (37 files vs the Node pilot's 38).
  The Dayz pilot folder already has `folder.jpg` from the Node builder,
  so the live library is unaffected; the port gains the write in v0.2.0.0.
- **Test server stopped after the test.** No Jellyfin server is left
  running in the VM; the test install (`~/jellyfin-test/`) is scratch
  and can be deleted.
