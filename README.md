# Retro TV

![Retro TV](assets/icon.webp)

Retro TV turns the Internet Archive into a browsable retro-video library inside
Jellyfin — Saturday-morning cartoons, classic TV, drive-in movies, Prelinger
shorts, vintage ads, soaps, and more — plus always-on linear channels with a
themeable guide and a weather channel, in the spirit of BackWhen TV. It is not
affiliated with BackWhen TV, the Internet Archive, or Jellyfin.

## Install — the Jellyfin plugin (easiest)

Dashboard → Plugins → Repositories → add
`https://raw.githubusercontent.com/athlon1982/Retro-TV/main/manifest.json`,
then install **Retro TV** from the catalog and restart Jellyfin. Open the
plugin settings, set the output folder, and hit **Build pilot**. Add the
output folder as a Movies library named **Retro TV**.

The plugin (v0.2.0.0) does everything below in one place: it builds the
on-demand catalog **and** serves the live channel guide — 9 channels at
launch (Saturday Morning Cartoons, Popeye, Classic TV, Drive-In Movies,
Haunted Drive-In, Sci-Fi Theater, Weather, Ad Break, Prelinger Theater),
48-hour shared-clock schedules, three switchable guide themes, M3U + XMLTV
for Jellyfin Live TV, and live ffmpeg streams. See `plugin/README.md`
for the guide, the endpoints, and the settings. The plugin is GPL-3.0
(it links Jellyfin's GPLv3 assemblies).

Prefer the standalone builder instead? The rest of this README is for you.
It writes the same catalog the plugin builds, without installing anything
into Jellyfin — just point a library at its output folder.

## How it works

The catalog builder queries the Internet Archive's free public API (no key,
no account), picks items with a playable MP4 derivative, and writes a small
file set per item into an output folder:

- `<Title> (<Year>).strm` — a one-line text file whose body is the direct
  Internet Archive download URL. Jellyfin scans `.strm` files natively and
  streams the URL at play time.
- `<Title> (<Year>).nfo` — title, year, plot, runtime, genre, and tags from
  the Archive metadata.
- `poster.jpg` — the item's Archive thumbnail.

Add the output folder to Jellyfin as a library named **Retro TV** and it
scans like any other movie library. Nothing is downloaded to disk in Stage 1:
video streams direct from the Internet Archive when someone presses Play.

Stage 2 — the linear channels — lives in the plugin (`plugin/`): the same
catalog drives scheduled channels, stitched into continuous streams and
served to Jellyfin Live TV as M3U + XMLTV. The standalone builder does not
serve channels; install the plugin for those.

## Two tiers — read this

The catalog has two tiers, kept separate so either can be pruned or removed
without touching the other:

- **`pd` — public domain / Creative Commons.** An item enters this tier only
  if its Archive metadata marks it public domain (or an explicit CC license
  whose URL contains `publicdomain`), or it sits in a curated public-domain
  collection with the license field confirming it. Anything else is excluded.
- **`user-uploaded` — rights status NOT verified.** The Archive also hosts
  user-uploaded recordings of shows that may still be under copyright
  (taped-off-air soaps, TV blocks, anime, and the like). These are the
  channels BackWhen-style lineups are famous for, and they are included here
  only as a separate, clearly-marked tier because the household asked for
  them. Items in this tier are **not** presented as public domain. If you
  publish or share this setup, review this tier yourself — drop anything
  you are not comfortable with (see *Curation* below).

Archive metadata is user-supplied, so the license field is a strong signal,
not a legal opinion. Spot-check sampled items; wrongly-marked items get
dropped.

## Requirements

- Jellyfin 12 (any install; the library is just a folder of `.strm` files)
- Node.js 18+ (the builder uses only Node built-ins — no dependencies)
- Internet access to `archive.org` (reads only; no uploads, no keys)
- Nothing paid, no accounts, no Docker

## Quickstart

```bash
# 1. Copy the example config and edit it (folders, limits, output path)
cp config.example.json config.local.json

# 2. Build a small pilot set first (~12 items) and check it
node src/catalog_builder.mjs --config config.local.json --pilot

# 3. Look at what it made
ls output/

# 4. In Jellyfin: Dashboard > Libraries > Add Media Library,
#    name it "Retro TV", content type Movies, folder = your output folder.
#    Save — Jellyfin scans it automatically. Press Play on one item.

# 5. When the pilot plays, build the full catalog
node src/catalog_builder.mjs --config config.local.json
```

Use `--limit N` to cap items per folder on a run, and `--folders
"Saturday Morning Cartoons,Classic TV"` to build only some folders.

## Configuration

`config.local.json` (git-ignored — never commit it) controls the build:

```jsonc
{
  "libraryName": "Retro TV",
  "outputDir": "./output",          // where .strm/.nfo/poster files go
  "userAgent": "RetroTV/0.1 ...",   // sent to archive.org on every request
  "requestsPerSecond": 1,           // be polite to the Archive; backs off on 429
  "mp4Preference": "full",          // "full" MP4 first, 512kb fallback
  "folders": [
    {
      "name": "Saturday Morning Cartoons",
      "tier": "pd",
      "query": "collection:animationandcartoons AND licenseurl:*publicdomain* AND mediatype:(movies)",
      "limit": 200
    }
  ],
  "pilot": [                        // curated items for --pilot runs
    { "folder": "Prelinger Theater", "tier": "pd", "identifier": "ToEnrichMank" }
  ],
  "prune": []                       // identifiers to always exclude
}
```

- `tier` is `"pd"` or `"user-uploaded"` and is recorded on every catalog
  entry. The builder enforces the license rule for `"pd"` folders and
  records (but does not require) a license for `"user-uploaded"` folders.
- `query` is an Internet Archive Advanced Search Lucene query. The builder
  pages through it with `rows`/`page`; very large result sets should use
  the Scrape API instead (a later improvement).
- The builder writes `catalog.json` in the output folder (one entry per
  item: identifier, title, year, runtime, licenseurl, tier, collection,
  MP4 URLs, poster URL, description) plus a `catalog.prev.json` backup of
  the previous run. `prune` entries are never re-added by a later run.

## Project layout

```
.
├── README.md
├── LICENSE                 # MIT — the catalog builder
├── manifest.json           # Jellyfin plugin repository manifest
├── config.example.json   # copy to config.local.json and edit (git-ignored)
├── assets/
│   └── icon.webp         # Retro TV logo (also used as the Jellyfin library image)
├── src/
│   └── catalog_builder.mjs   # the builder — Node built-ins only, no dependencies
└── plugin/
    ├── README.md             # plugin install, guide, endpoints, settings
    ├── LICENSE               # GPL-3.0 — the plugin
    ├── PLUGIN-DESIGN.md      # full design (catalog, guide, weather, themes)
    ├── BUILD-PLAN.md         # slice plan
    ├── BUILD-0.2.0.0-RECORD.md   # the 0.2.0.0 build + load-test record
    └── Jellyfin.Plugin.RetroTv/  # C# source — dotnet build via build.sh
```

Generated output (`output/`, `catalog.json`) is git-ignored: the repo holds
code and docs, not the catalog. Your `config.local.json` (paths, Jellyfin
address) is git-ignored too — it never belongs in a public repo.

## Curation

After a build, check the catalog and prune anything that does not play,
is wrongly marked, or is junk quality: add its identifier to `prune` in
`config.local.json` and re-run. Pruned items stay out on later runs. To
drop the whole user-uploaded tier, delete its folders from `config` and
remove the tier's entries from `catalog.json` — the `pd` tier is untouched.

## License

Dual-licensed:

- **The catalog builder (`src/`) is MIT** — see `LICENSE`.
- **The Jellyfin plugin (`plugin/`) is GPL-3.0** — see `plugin/LICENSE`.
  It links Jellyfin's GPLv3 assemblies, so the plugin itself must be GPL.

The Retro TV logo (`assets/icon.webp`) is original artwork for this project.
The videos themselves are not part of this repo: they stream from the
Internet Archive at play time, under whatever license their Archive
metadata states.

## Disclaimer

This is a personal, household project. You are responsible for what you
include in your catalog — especially the `user-uploaded` tier, whose rights
status is unverified by design. When in doubt, leave it out.
