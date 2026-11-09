# Retro TV plugin — build plan

Staged plan for the native Jellyfin plugin. The skeleton (v0.1.0.0) is
**done and load-tested** — see PLUGIN-DESIGN.md §12. Each slice is its
own version, its own package, its own backup; nothing ships to the live
Dayz server and nothing is pushed to GitHub until Michael says so at the
1.0.0.0 gate.

## Toolchain (verified 2026-11-08)

- .NET SDK **10.0.403** at `~/.dotnet` (`dotnet-install`, channel 10.0).
  First install hit tar chown errors and a "No space left" failure with a
  0-byte runtimeconfig.json; a second install extracted fine (621M).
  Smoke test passed (trivial console app, 0 warnings, 0 errors).
- Target: `net10.0`, `Jellyfin.Controller` / `Jellyfin.Model` **12.4.0**
  from NuGet, `Microsoft.Extensions.Http` 10.0.0.
- **No `Jellyfin.Api` NuGet package exists** — the controller is a plain
  ASP.NET Core `ControllerBase` (verified working on a real 12.4.0
  server, PLUGIN-DESIGN.md §12).
- Build: `./build.sh` in `src/Jellyfin.Plugin.RetroTv/` — builds Release,
  zips DLL + meta.json + icon into `dist/retro-tv_<version>.zip`,
  computes the MD5, writes it into `../../manifest.json`.
- Test server (VM only): Jellyfin 12.4.0 linux tarball from
  repo.jellyfin.org (`files/server/linux/stable/v12.4/amd64/
  jellyfin_12.4-amd64.tar.gz`, 113,075,397 B). Run with separate
  `--datadir/--configdir/--cachedir/--logdir/--webdir`. **Install the
  plugin into `<datadir>/plugins/RetroTv/`** — the config-dir path is
  silently ignored in 12.4. Jellyfin 12.4 refuses to start if the data
  path has under 2 GiB free (`/tmp` is a small tmpfs in this VM — use
  home-dir paths). Auth header is `Authorization: MediaBrowser ...`,
  not `X-Emby-Authorization`. Throwaway test admin only; never the live
  server, never a real key.
- Iteration loop that worked: edit → `dotnet build` → `./build.sh` →
  stop test server by PID → copy DLL into the test plugins dir →
  restart → re-test via the API. (Kill by PID, never `pkill -f` on a
  pattern that matches your own shell.)

## Slice 0.1.0.0 — skeleton (DONE 2026-11-08)

**What shipped:** plugin identity (GUID
`25eef671-4023-4c23-badb-6c051fa0620f`, name Retro TV, version 0.1.0.0,
targetAbi 12.0.0.0), `PluginConfiguration` (all fields incl. theme +
weather zip), config page (`configPage.html`, embedded, with theme
switcher + zip geocode + build-now buttons), IA client
(advancedsearch + metadata + download, 1 req/s throttle, 429/503
backoff), catalog builder (faithful port of the Node builder: license
filter, MP4 scoring, sanitize, runtime/year parsing, catalog.json
versioning), scheduled task (daily 03:00, single-flight guard, never
rethrows except cancellation), admin API (Status / Build / Guide /
Guide Themes / Weather Geocode / Weather Forecast), theme system (3
built-in themes as embedded JSON + user themes from the plugin data
dir), weather client (Open-Meteo geocode + forecast, Zippopotam.us
fallback), `meta.json`, `manifest.json` (placeholders for owner URLs),
`build.sh`, GPL-3.0 LICENSE, README.

**Verification (all in the VM):** `dotnet build` 0 warnings, 0 errors.
Zip `retro-tv_0.1.0.0.zip` (273,044 B first build; 091022eb… after the
length fix), MD5 in manifest.json. **Load test on a real Jellyfin
12.4.0:** plugin loads Active; all API endpoints answer; pilot build
**12 items, 0 skipped, 34.7 s**; output byte-comparable in layout to
the Node pilot (37 files; the Node pilot's 38th is `folder.jpg`, which
the port does not write yet — see 0.2.0.0). One real bug found and
fixed by the test: IA `length`-as-string
(`LenientNullableDoubleConverter`). Full detail: PLUGIN-DESIGN.md §12.

**Effort note:** the skeleton took one session including the toolchain
install and the load test. The load test was the expensive part (server
download, wizard/auth header debugging, the install-path discovery,
the length bug) — roughly half the session. Future slices reuse the
test server setup, so they should be faster.

## Slice 0.2.0.0 — on-demand wired (next)

Goal: the on-demand half is finished and proven in Jellyfin, not just
in the API.

1. **`folder.jpg` write.** On build, write the v5 icon to
   `<OutputFolder>/folder.jpg` so the library shows the icon without
   the Node builder's help. Decide: convert the webp icon to JPEG at
   build/package time (ship a `folder.jpg` asset in the zip) or copy
   the webp bytes as `folder.jpg` and verify Jellyfin accepts it.
   Prefer the converted JPEG.
2. **Config page in the web client.** Open the plugin settings page in
   a browser against the test server; save each field; confirm the
   theme dropdown populates from `/RetroTv/Guide/Themes`, the zip
   geocode button fills lat/lon/place, and Build-now buttons work.
   (The page is written but was exercised only via the API so far.)
3. **Library scan.** Add the test output folder to the test Jellyfin
   as a Movies library named "Retro TV", scan, and confirm all 12
   pilot items appear with posters and NFO metadata.
4. **Config persistence round-trip.** Restart the test server; confirm
   the saved config (output folder, theme, zip) survives.
5. **Re-package + re-verify.** `./build.sh`, reload, rebuild pilot,
   confirm still 12/12.

Gate: pilot library plays in the test Jellyfin (one cartoon, one
movie, one Prelinger short, one user-uploaded show — same four as
Michael's Dayz play test). **Do not start 0.3.0.0 on your own.**

## Slice 0.3.0.0 — full catalog

Goal: the full 10-folder catalog through the plugin.

1. Port the full folder list from `config.example.json` (all 10 folders:
   Saturday Morning Cartoons, Classic TV, Drive-In Movies, Prelinger
   Theater, Ad Break, Creature Features, VHS Vault, Soaps, Toonami,
   Anime) into the plugin config defaults.
2. **Anime needs the real-title filter first** — the Node pilot found
   `collection:anime` titles are mostly opaque IDs. Build the filter
   in the Node builder first (its iteration is faster), verify, then
   port the rule.
3. Full build on the test server; prune/curation round (dead links,
   wrong picks) with the skip list as the work queue.
4. Compare output counts against the Node builder's full catalog
   (same builder, same inputs — counts must match or the difference
   must be explained per item).

Gate: full catalog builds clean on the test server AND Michael's pilot
play test on Dayz has passed (his gate, not ours — the pilot must play
before the full version is built for real use). **Do not start 0.4.0.0
on your own.**

## Slice 0.4.0.0 — live channels (Stage 2)

Goal: always-on linear channels with the themeable guide and the
weather channel. This is the biggest slice; split it if it grows.

1. **Schedule engine.** Deterministic 48h schedules per channel from
   the catalog (same rotation rules as the Node sidecar design in
   SCOPE.md §Stage 2); schedule persisted so a restart resumes
   mid-programme, not at the top.
2. **ffmpeg stitching.** Continuous H.264 + AAC per channel from the
   IA MP4s (no re-encode where the source already matches; NVENC on
   the 1660 Ti if available under the pinned 560.94 driver — verify at
   build time, never touch the driver; CPU fallback must exist).
3. **M3U + XMLTV.** Plugin serves its own M3U + XMLTV endpoints;
   register them as a Jellyfin Live TV tuner + guide provider
   (M3U + XMLTV is the default route; native Live TV registration only
   on a concrete limitation).
4. **Guide page.** The plugin's own guide page (Jellyfin's built-in
   guide cannot be re-themed): programme grid, now/next, theme
   switcher (dropdown + `?theme=` override), all 3 starter themes.
5. **Weather channel.** Loop renderer (~5 min): current conditions +
   7-day forecast for the configured zip, refreshed every 15 min from
   Open-Meteo; ffmpeg `drawtext` is the leading candidate (confirm at
   build time); attribution "Weather data by Open-Meteo.com" on the
   frame. Appears in the guide as channel #8 (Wallingford, VT default).
6. **Channels toggle.** `ChannelsEnabled` gates the whole Stage 2
   (off by default until this slice ships).

Gate: 3 channels play end to end across a programme change on the
test server, guide themes switch visibly, weather channel shows
Wallingford conditions for zip 05773. **Do not start 1.0.0.0 on your
own.**

## Slice 1.0.0.0 — repository release

Goal: the first public release, only when Michael says post.

1. Push the repo to GitHub (owner/repo name is his call); create
   release v1.0.0, attach the zip.
2. Replace `<owner>` in `manifest.json` (sourceUrl + imageUrl) with the
   real owner; host `manifest.json` at
   `https://raw.githubusercontent.com/<owner>/retro-tv/main/manifest.json`.
3. Verify the repository URL in a clean test Jellyfin: Dashboard →
   Plugins → Repositories → add the URL → Retro TV appears in the
   catalog → installs → loads.
4. License split documented: plugin GPL-3.0, catalog builder MIT,
   icon is original project artwork.
5. **The `user-uploaded` tier in the public release is Michael's call**
   at publish time — the tier is separable by design; confirm whether
   it ships or is held back.

Gate: Michael says post, and the repository link installs cleanly on
the test server.

## Standing rules for every slice

- Never touch the live Dayz Jellyfin (no install, no deploy, no
  restart, no API writes). All testing in the VM test server.
- Never record any API key, password, token, or secret anywhere. The
  test admin credentials are throwaway and live only in the scratch
  test dir.
- Defensive by design (§2.1): constructor does no I/O; every external
  call isolated with timeout; never block startup; no unbounded work
  on the request path; config validated on save; `[RetroTv]` log
  prefix; the plugin writes only its own output folder + data dir.
- ASCII only in staged scripts. Hidden windows on every machine if
  any job touches one (this job should not need any).
- `dotnet build` 0 errors, 0 warnings before any package. Re-verify
  the API surface against the 12.4.0 packages at each slice (the
  compiler is the check).
- Each slice ends with: what changed, what compiles, what was verified
  on the test server, the new zip + checksum, and the next slice. Do
  not start the next slice on your own.
