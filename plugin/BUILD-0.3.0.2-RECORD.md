# Retro TV plugin — BUILD 0.3.0.2 RECORD

Date: 2026-11-09 ~14:02 EST. Built directly in-session (dispatched subagents are
refused on this topic — do not retry them).

## What changed from 0.3.0.1

Michael's direction 2026-11-09 13:49–13:50: if a library is part of setup, have
it set up automatically, with the Retro TV icon on the folder.

1. **"Create the Retro TV library" button** (`Configuration/configPage.html`,
   next to the Output folder setting):
   - One click creates the Jellyfin library from the output folder, named from
     the Library name field (default "Retro TV"), collection type **mixed**
     (the catalog holds movies AND classic TV), refresh on.
   - Implemented in the config page JS with the admin's own token
     (`ApiClient.ajax`): `GET /Library/VirtualFolders` first — if a library
     with that name already exists it reports it and creates nothing; else
     `POST /Library/VirtualFolders?name=…&collectionType=mixed&paths=…&refreshLibrary=true`
     with body `{"libraryOptions":{}}`.
   - Why a button and not silent auto-create: a plugin background task has no
     admin token, and Jellyfin 12.4's `ILibraryManager` interface exposes no
     AddVirtualFolder (only the HTTP API controller has it, and the concrete
     `LibraryManager.AddVirtualFolder` is not reachable through the interface
     a plugin can reference). The button runs with the admin's own session, so
     it is the automatic setup within what Jellyfin allows. The Output folder
     description was updated to point at the button (the old "add it to
     Jellyfin as a Movies library" manual step is gone).
   - The icon needs no extra code: the catalog build already writes the v5
     icon as `folder.jpg` at the library root, and this was **proven on the
     test server** — a library created pointing at a folder with folder.jpg
     served that exact file as its primary image (byte-identical MD5).
2. **Audio-probe timeout fix** (`LiveTv/LiveTvController.cs`, `ProbeAudioAsync`):
   - The probe runs ffprobe with a 15 s timeout. On timeout it threw
     `OperationCanceledException`, which the old catch filter (`when (ex is not
     OperationCanceledException)`) let through — the stream request then failed
     500 ("The operation was canceled"). The method's contract is "null =
     inconclusive — proceed", so a timeout is now caught and returns null
     (proceed), and the timed-out ffprobe process is killed (disposing the
     `Process` object does not stop it; a hung ffprobe would otherwise linger
     on the slow connection). The `Process` is declared outside the try so the
     catch can reach it (a `using var` inside the try is out of scope in the
     catch — that was a build error on the first attempt), with a `finally`
     disposing it.
   - Found during this build's load test: channel 1's current program is an
     archive.org file (`lionblaze-catch-fire-completed-map-flashing-warning`)
     that ffprobe cannot reach within 15 s in this VM's proxied environment
     (direct ffprobe hangs past 20 s; curl through the proxy fetches the same
     file in ~2 s). The file itself is fine — the VM's egress path is the
     constraint, and the plugin runs with direct internet on Dayz. Channel 28
     streams cleanly in the same test run, which is the proof the stream path
     works.
3. **Version bumped to 0.3.0.2** (csproj Version/AssemblyVersion/FileVersion);
   meta.json changelog updated. Everything from 0.3.0.1 is unchanged
   (Getting-started box, copyable tuner links, one-click theme apply to
   Branding).

## Build

`~/.dotnet/dotnet build -c Release --nologo` → **0 warnings, 0 errors** on the
final build. (One transient 1-warning build mid-way was a file-lock warning
while the test server still held the DLL; a clean rebuild shows 0/0.)

**Never run build.sh** (it rewrites the manifest checksum). Package manually.

## Package (manual)

`plugin/src/Jellyfin.Plugin.RetroTv/dist/retro-tv_0.3.0.2.zip`

- **987,412 B**
- MD5: `d787c141d70c0f7468fb4d74e80525f1`
- SHA256: `ec3b0d9cae6388fb30196ec884bfe5bcd44a142791342c8a5a0f86a3aee4f196`
- Contents: `Jellyfin.Plugin.RetroTv.dll` 2,944,000 B + `meta.json` 863 B +
  `icon.webp` 228,246 B (v5 icon, unchanged).
- DLL strings verified: "Create the Retro TV library" present.

## Load test (real Jellyfin 12.4.0 test server, ~/jellyfin-test/)

Test server stopped by PID after the run; live Dayz Jellyfin never touched.
Fresh test API key inserted into the throwaway server's DB (test server only;
the key value is not recorded here).

- Plugin loads as **"Retro TV" "0.3.0.2"** (log 14:02:44).
- `/RetroTv/Guide` → 200.
- `/RetroTv/channels.m3u` → **154 channels**.
- `/RetroTv/guide.xml` → 200, 34,734,973 B.
- `/RetroTv/Live/drive-in-movies` (ch 28) → 200, 1,497,420 B, valid MPEG-TS
  (0x47 sync at 0 and every 188-byte boundary).
- `/RetroTv/Live/sat-morning-cartoons` (ch 1) → no bytes in this VM run: the
  current program's IA file is unreachable from this VM's proxied environment
  (see §2). The probe fix is proven by the log instead: "Audio probe timed
  out … (proceeding)" then "streaming 'batch of 9 programmes…'" — no 500.
  On Dayz (direct internet) this file is expected to stream; the ch-28
  result is the stream-path proof for this build.
- Config page serves live (`/web/configurationpage?name=Retro%20TV`) → 200,
  28,502 B, contains "Create the Retro TV library" + "Getting started" +
  "Apply this theme" (6 matches).
- Branding Status still works after the restart: `hasRetroTvTheme: true,
  theme: "neon-nights"` (the 0.3.0.1 apply persisted).
- **Library create flow, end to end (the button's exact API calls):**
  - `POST /Library/VirtualFolders?name=Retro TV Test&collectionType=mixed
    &paths=/tmp/rtv-test-lib&refreshLibrary=true` with
    `{"libraryOptions":{}}` → **204**.
  - `GET /Library/VirtualFolders` → new library present, collectionType
    mixed, locations correct, and its primary image is byte-identical to the
    folder.jpg in the folder (MD5 `a99b3bde…`, 316,265 B, 1600×1600) — the
    icon proof.
  - The pre-existing "Retro TV" library on the test server was visible in
    the same GET, so the button's duplicate check would find it and report
    "already exists".
  - Test library removed after the run (`DELETE …/VirtualFolders?name=Retro
    TV Test` → 204); only "Retro TV" remains.

## Publish steps (Michael, GitHub web UI — same as 0.3.0.0/0.3.0.1)

1. Create release **v0.3.0.2** on athlon1982/Retro-TV.
2. Attach `retro-tv_0.3.0.2.zip` (987,412 B, MD5 above).
3. Add the manifest entry (in `manifest.json` on main):
   - version `0.3.0.2`, checksum `d787c141d70c0f7468fb4d74e80525f1`,
     sourceUrl `https://github.com/athlon1982/Retro-TV/releases/download/v0.3.0.2/retro-tv_0.3.0.2.zip`.
   - Keep the 0.3.0.0 and 0.3.0.1 entries as-is. The 0.2.0.0 release still
     has the WRONG zip attached (1,330,480 B package zip instead of the
     644,078 B `retro-tv_0.2.0.0.zip`) — that fix is still owed, separate.

## Notes / gotchas

- Before re-running the lineup generator, restore `/tmp/lineup-0.2.0.0.json`
  over `plugin/src/Jellyfin.Plugin.RetroTv/Guide/Channels/lineup.json`.
- Slug note: 0.3.0.x uses `sat-morning-cartoons`, NOT 0.2.0.0's
  `saturday-morning-cartoons`.
- The `ExtractThemeId` helper splits the branding marker on whitespace only —
  theme ids contain hyphens (`woodgrain-78`), so `-` must never be a
  delimiter.
- `ILibraryManager` (Jellyfin 12.4) has `GetVirtualFolders` but no
  `AddVirtualFolder` — library creation from a plugin goes through the HTTP
  API with an admin token (the config page button), not server-side code.
