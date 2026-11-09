# Retro TV plugin — BUILD 0.3.0.1 RECORD

Date: 2026-11-09 ~13:27 EST. Built directly in-session (dispatched subagents are
refused on this topic — do not retry them).

## What changed from 0.3.0.0

The "easy setup" release, driven by Michael's direction 2026-11-09 13:21–13:24
("what are new users supposed to put" / "make this setup as easy as possible"
 / "is this also going to add the Custom CSS into the branding information also?").

1. **Config page rewrite** (`Configuration/configPage.html`):
   - New bordered "Getting started — do this first" box at the TOP, with numbered
     steps: weather zip Resolve+Save; add M3U tuner + XMLTV provider; Refresh
     Channels then Guide; apply the theme to Jellyfin's guide (step 4).
   - Copyable readonly URL inputs (`txtRetroTvM3uUrl` / `txtRetroTvXmltvUrl`,
     click-to-select) filled by the same pageshow JS from `window.location.origin`
     + `/RetroTv/channels.m3u` and `/RetroTv/guide.xml` — per-server links,
     never copied from someone else's screenshot.
   - Output-folder and ffmpeg descriptions generalized (Dayz-specific paths
     removed). Bottom "Live TV wiring" section reduced to a short pointer.
2. **One-click branding apply** (new: `Branding/BrandingController.cs`,
   `Guide/Themes/Branding/*.css`, csproj embedded-resource glob):
   - The plugin did NOT write branding before — its themes only styled its own
     `/RetroTv/Guide` page, while Jellyfin's built-in guide needed the CSS
     pasted into Dashboard → General → Custom CSS by hand, and without it the
     guide shows no channel names at all (fix v2 `::after` on the title attr).
   - `POST /RetroTv/Branding/ApplyTheme?theme=<id>` (admin only,
     `[Authorize(Policy = "RequiresElevation")]`) reads Branding via
     `IConfigurationManager.GetConfiguration("branding")`, strips any previous
     Retro TV block (`/* Retro TV theme: ... */` … `/* end Retro TV theme */`),
     preserves all other CSS, appends the theme's generated CSS file, and saves.
     `GET /RetroTv/Branding/Status` reports whether a theme is applied.
   - The three generated CSS files are embedded as resources (woodgrain-78,
     neon-nights, vhs-blue-screen — the same verified files that ship in
     `stage2/retro-tv-livetv-*.css`; retro-blue has no plugin theme and is not
     embedded). The button applies the theme selected in the dropdown.
   - Config page: "Apply this theme to Jellyfin's Live TV guide" button next to
     the Guide theme dropdown + status line (reads Status on pageshow).
3. **Version bumped to 0.3.0.1** (csproj Version/AssemblyVersion/FileVersion);
   meta.json changelog updated.

Nothing else changed: same 155-channel lineup (154 served), same guide, same
streams, same commercial-break rules.

## Build

`~/.dotnet/dotnet build -c Release --nologo` (dotnet is at `~/.dotnet/dotnet`,
not on PATH) → **0 warnings, 0 errors** on the final build. One error fixed
mid-build: `IServerConfigurationManager` does not exist in Jellyfin 12 — the
interface is `IConfigurationManager` (`MediaBrowser.Common.Configuration`) with
non-generic `GetConfiguration(string)` / `SaveConfiguration(string, object)`,
verified against `MediaBrowser.Common.xml` in the test server.

**Never run build.sh** (it rewrites the manifest checksum). Package manually.

## Package (manual)

`plugin/src/Jellyfin.Plugin.RetroTv/dist/retro-tv_0.3.0.1.zip`

- **986,687 B**
- MD5: `533340201a2c11fc13f42c681011bca5`
- SHA256: `79e452e248f343b5c4bac09023aa4f101c5d852a84ec44c0b1f082e3cb651738`
- Contents: `Jellyfin.Plugin.RetroTv.dll` 2,940,928 B + `meta.json` 905 B +
  `icon.webp` 228,246 B (v5 icon, unchanged).
- DLL strings verified: all 3 branding CSS resources embedded, "Getting started"
  ×2, "Apply this theme" ×2.

## Load test (real Jellyfin 12.4.0 test server, ~/jellyfin-test/)

Test server stopped by PID after the run; live Dayz Jellyfin never touched.
Fresh test API key inserted into the throwaway server's DB (test server only;
the key value is not recorded here).

- Plugin loads as **"Retro TV" "0.3.0.1"** (log 13:27:54).
- `/RetroTv/Guide` → 200.
- `/RetroTv/channels.m3u` → **154 channels**.
- `/RetroTv/guide.xml` → 200, 34,733,837 B.
- `/RetroTv/Live/sat-morning-cartoons` → 200, 591,448 B, valid MPEG-TS
  (0x47 sync at 0 and every 188-byte boundary).
- `/RetroTv/Guide/Themes` → 200 (3 themes).
- Config page serves live (`/web/configurationpage?name=Retro%20TV`) → 200,
  25,651 B, contains "Getting started" + "Apply this theme".
- **Branding flow, end to end:**
  - `GET /RetroTv/Branding/Status` without auth → 401. With the test API key
    → 200 (the key satisfies RequiresElevation).
  - Before: `hasRetroTvTheme: false`, `customCssLength: 0`.
  - Seeded existing CSS (`/* my own custom css */` + a rule) via the Branding
    API first, then `POST ApplyTheme?theme=woodgrain-78` →
    `{"applied":true,"theme":"woodgrain-78","customCssLength":10553,
    "replacedPrevious":false,"preservedExistingCss":true}`.
  - Verified in Branding: my CSS intact, exactly **1** Retro TV block,
    channel-name fix (`guide-channelHeaderCell`) present, `.tvguide` scope
    present, theme vars present.
  - `POST ApplyTheme?theme=neon-nights` → `replacedPrevious: true`,
    `preservedExistingCss: true`. Verified: still exactly 1 block, woodgrain
    gone, neon present (`--rtv-bg: #0d0d1a`), my CSS still intact.
  - `GET Status` → `hasRetroTvTheme: true, theme: "neon-nights"`.

## Publish steps (Michael, GitHub web UI — same as 0.3.0.0)

1. Create release **v0.3.0.1** on athlon1982/Retro-TV.
2. Attach `retro-tv_0.3.0.1.zip` (986,687 B, MD5 above).
3. Add the manifest entry (in `manifest.json` on main):
   - version `0.3.0.1`, checksum `533340201a2c11fc13f42c681011bca5`,
     sourceUrl `https://github.com/athlon1982/Retro-TV/releases/download/v0.3.0.1/retro-tv_0.3.0.1.zip`.
   - Keep the 0.3.0.0 entry as-is. The 0.2.0.0 release still has the WRONG zip
     attached (1,330,480 B package zip instead of the 644,078 B
     `retro-tv_0.2.0.0.zip`) — that fix is still owed, separate from this release.

## Notes / gotchas

- Before re-running the lineup generator, restore `/tmp/lineup-0.2.0.0.json`
  over `plugin/src/Jellyfin.Plugin.RetroTv/Guide/Channels/lineup.json`.
- Slug note: 0.3.0.x uses `sat-morning-cartoons`, NOT 0.2.0.0's
  `saturday-morning-cartoons`.
- The `ExtractThemeId` helper splits the marker on whitespace only — theme ids
  contain hyphens (`woodgrain-78`), so `-` must never be a delimiter.
- The config page JS reads Status with both camelCase and PascalCase field
  names (Jellyfin's JSON casing varies by endpoint); same for the Apply result.
- Branding writes take effect on next page load — no Jellyfin restart needed.
