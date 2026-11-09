#!/usr/bin/env node
/**
 * Retro TV catalog builder — Stage 1 (on-demand .strm library).
 *
 * Queries the Internet Archive's free public API (no key, no account),
 * picks items with a playable MP4 derivative, and writes per-item
 * `.strm` + `.nfo` + `poster.jpg` plus a versioned `catalog.json`
 * into the output folder. Jellyfin scans the folder as a normal library;
 * video streams direct from the Archive at play time (nothing downloaded).
 *
 * Node built-ins only (global fetch) — no dependencies.
 * Be polite: ~1 request/second, exponential back-off on 429/503.
 *
 * Tiers (kept separate in the catalog so either can be pruned cleanly):
 *   "pd"             — public domain / CC only. Enforced: the item's
 *                      metadata licenseurl must contain "publicdomain".
 *   "user-uploaded"  — rights status NOT verified. Recorded, never
 *                      presented as public domain.
 *
 * Usage:
 *   node src/catalog_builder.mjs --config config.local.json [--pilot]
 *       [--out <dir>] [--limit N] [--folders "A,B"]
 */
import { promises as fs } from "node:fs";
import path from "node:path";

const IA = "https://archive.org";
const DEFAULT_CONFIG = "./config.local.json";

function parseArgs(argv) {
  const out = { config: DEFAULT_CONFIG, pilot: false, out: null, limit: null, folders: null };
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--config") out.config = argv[++i];
    else if (a === "--pilot") out.pilot = true;
    else if (a === "--out") out.out = argv[++i];
    else if (a === "--limit") out.limit = parseInt(argv[++i], 10);
    else if (a === "--folders") out.folders = argv[++i].split(",").map((s) => s.trim());
    else if (a === "--help" || a === "-h") {
      console.log("Usage: node src/catalog_builder.mjs --config <path> [--pilot] [--out <dir>] [--limit N] [--folders \"A,B\"]");
      process.exit(0);
    } else {
      console.error(`Unknown argument: ${a}`);
      process.exit(2);
    }
  }
  return out;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function first(v) {
  if (Array.isArray(v)) return v.length ? v[0] : "";
  return v ?? "";
}

function asArray(v) {
  if (Array.isArray(v)) return v;
  if (v === undefined || v === null || v === "") return [];
  return [v];
}

function escXml(s) {
  return String(s ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

/** Windows- and POSIX-safe file/folder name. */
function sanitize(name) {
  let s = String(name ?? "")
    .replace(/[<>:"/\\|?*\x00-\x1f]/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/[. ]+$/, "");
  if (s.length > 100) s = s.slice(0, 100).trim().replace(/[. ]+$/, "");
  return s || "untitled";
}

function parseRuntimeSeconds(meta, chosenFile) {
  if (chosenFile && typeof chosenFile.length === "number" && chosenFile.length > 0) {
    return Math.round(chosenFile.length);
  }
  const raw = first(meta.runtime);
  if (!raw) return null;
  if (typeof raw === "number") return Math.round(raw);
  const s = String(raw).trim();
  let m = s.match(/^(\d+):(\d{2}):(\d{2})$/);
  if (m) return +m[1] * 3600 + +m[2] * 60 + +m[3];
  m = s.match(/^(\d+):(\d{2})$/);
  if (m) return +m[1] * 60 + +m[2];
  m = s.match(/^(\d+)\s*min/i);
  if (m) return +m[1] * 60;
  return null;
}

function parseYear(meta) {
  const y = String(first(meta.year) ?? "").match(/(\d{4})/);
  return y ? parseInt(y[1], 10) : null;
}

/** Score an .mp4 file: exact <id>.mp4 wins, then plain derivatives, then .ia, then 512kb. */
function scoreMp4(file, identifier) {
  const name = file.name || "";
  if (!/\.mp4$/i.test(name)) return -1;
  let score = 0;
  if (name === `${identifier}.mp4`) score += 3;
  if (file.source === "derivative") score += 1;
  if (/\.ia\.mp4$/i.test(name)) score += 1;
  else if (/_512kb\.mp4$/i.test(name)) score += 0;
  else if (/_edit\.mp4$/i.test(name)) score += 1;
  else score += 3;
  return score;
}

/** Strip HTML tags from Archive descriptions (they render literally in Jellyfin). */
function stripHtml(s) {
  return String(s ?? "")
    .replace(/<[^>]+>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

class IAClient {
  constructor(userAgent, requestsPerSecond) {
    this.userAgent = userAgent;
    this.minGapMs = Math.ceil(1000 / Math.max(0.1, requestsPerSecond));
    this.lastAt = 0;
  }

  async throttle() {
    const now = Date.now();
    const wait = this.minGapMs - (now - this.lastAt);
    if (wait > 0) await sleep(wait);
    this.lastAt = Date.now();
  }

  async fetch(url, binary = false, tries = 5) {
    for (let attempt = 0; attempt < tries; attempt++) {
      await this.throttle();
      let res;
      try {
        res = await fetch(url, { headers: { "User-Agent": this.userAgent } });
      } catch (e) {
        if (attempt === tries - 1) throw e;
        await sleep(2000 * 2 ** attempt);
        continue;
      }
      if (res.status === 429 || res.status === 503) {
        const retryAfter = res.headers.get("retry-after");
        const wait = retryAfter ? parseInt(retryAfter, 10) * 1000 : 2000 * 2 ** attempt;
        console.log(`  [rate-limited ${res.status}, waiting ${Math.round(wait / 1000)}s]`);
        await sleep(wait);
        continue;
      }
      if (!res.ok) throw new Error(`HTTP ${res.status} for ${url}`);
      return binary ? Buffer.from(await res.arrayBuffer()) : await res.json();
    }
    throw new Error(`failed after ${tries} tries: ${url}`);
  }

  async search(query, rows, page = 0) {
    const params = new URLSearchParams({
      q: query,
      "fl[]": ["identifier", "title"],
      rows: String(rows),
      page: String(page),
      output: "json",
    });
    const data = await this.fetch(`${IA}/advancedsearch.php?${params.toString()}`);
    return { docs: data?.response?.docs ?? [], numFound: data?.response?.numFound ?? 0 };
  }

  async metadata(identifier) {
    const data = await this.fetch(`${IA}/metadata/${encodeURIComponent(identifier)}`);
    if (!data || !data.metadata) throw new Error(`no metadata for ${identifier}`);
    return data;
  }
}

function buildNfo(entry) {
  const runtimeMin = entry.runtime_seconds ? Math.max(1, Math.round(entry.runtime_seconds / 60)) : "";
  const lines = [
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
    "<movie>",
    `  <title>${escXml(entry.title)}</title>`,
    `  <originaltitle>${escXml(entry.title)}</originaltitle>`,
  ];
  if (entry.year) lines.push(`  <year>${entry.year}</year>`);
  if (entry.description) lines.push(`  <plot>${escXml(entry.description)}</plot>`);
  if (runtimeMin !== "") lines.push(`  <runtime>${runtimeMin}</runtime>`);
  lines.push(`  <genre>${escXml(entry.folder)}</genre>`);
  if (entry.poster_url) lines.push(`  <thumb>${escXml(entry.poster_url)}</thumb>`);
  lines.push(`  <tag>Retro TV</tag>`);
  lines.push(
    entry.tier === "pd"
      ? `  <tag>public domain</tag>`
      : `  <tag>user-uploaded - rights unverified</tag>`
  );
  lines.push(`  <uniqueid type="archive.org" default="true">${escXml(entry.identifier)}</uniqueid>`);
  lines.push("</movie>");
  return lines.join("\n") + "\n";
}

async function buildItem(client, outDir, folder, tier, identifier, pruneSet, overrides = {}) {
  if (pruneSet.has(identifier)) return { skipped: identifier, reason: "pruned" };
  const data = await client.metadata(identifier);
  const meta = data.metadata || {};
  const files = Array.isArray(data.files) ? data.files : [];

  const licenseurl = String(first(meta.licenseurl) || "");
  if (tier === "pd" && !licenseurl.toLowerCase().includes("publicdomain")) {
    return { skipped: identifier, reason: `pd tier requires publicdomain licenseurl (got: ${licenseurl || "(none)"})` };
  }

  const mp4s = files
    .map((f) => ({ file: f, score: scoreMp4(f, identifier) }))
    .filter((x) => x.score >= 0)
    .sort((a, b) => b.score - a.score);
  if (!mp4s.length) return { skipped: identifier, reason: "no MP4 derivative" };
  const chosen = mp4s[0].file;
  const small = files.find((f) => /_512kb\.mp4$/i.test(f.name || ""));

  const title = String(first(meta.title) || identifier).trim();
  const year = parseYear(meta);
  const runtimeSeconds = parseRuntimeSeconds(meta, chosen);
  const description = stripHtml(String(first(meta.description) || asArray(meta.description).join(" ") || "")).slice(0, 1000);
  // Curated title/year overrides (Archive titles are often verbose or wrong).
  const ov = overrides[identifier] || {};
  const finalTitle = ov.title || title;
  const finalYear = ov.year ?? year;
  // Anime folder: IA's anime collection is mostly opaque upload IDs and
  // fansub filenames — require a real title (a space, or one clean word
  // with no digits). Skips are recorded, not silent.
  if (folder === "Anime") {
    const t = finalTitle.trim();
    const isRealTitle = t.includes(" ") || /^[A-Za-z][A-Za-z'’\-]{3,29}$/.test(t);
    if (!isRealTitle) return { skipped: identifier, reason: "opaque title (Anime filter)" };
  }
  const collection = asArray(meta.collection);
  const mp4Url = `${IA}/download/${encodeURIComponent(identifier)}/${chosen.name.split("/").map(encodeURIComponent).join("/")}`;
  const posterFile = files.find((f) => f.name === "__ia_thumb.jpg");
  const posterUrl = posterFile ? `${IA}/download/${encodeURIComponent(identifier)}/__ia_thumb.jpg` : null;

  const baseName = sanitize(finalYear ? `${finalTitle} (${finalYear})` : finalTitle);
  const itemDir = path.join(outDir, sanitize(folder), baseName);
  await fs.mkdir(itemDir, { recursive: true });

  const strmName = `${baseName}.strm`;
  const nfoName = `${baseName}.nfo`;
  await fs.writeFile(path.join(itemDir, strmName), mp4Url + "\n", "utf8");

  const entry = {
    identifier,
    title: finalTitle,
    year: finalYear,
    runtime_seconds: runtimeSeconds,
    licenseurl,
    tier,
    collection,
    folder,
    mp4_url: mp4Url,
    mp4_512kb_url: small ? `${IA}/download/${encodeURIComponent(identifier)}/${small.name.split("/").map(encodeURIComponent).join("/")}` : null,
    poster_url: posterUrl,
    description,
    files: { strm: `${sanitize(folder)}/${baseName}/${strmName}`, nfo: `${sanitize(folder)}/${baseName}/${nfoName}`, poster: posterFile ? `${sanitize(folder)}/${baseName}/poster.jpg` : null },
  };
  await fs.writeFile(path.join(itemDir, nfoName), buildNfo(entry), "utf8");
  if (posterFile) {
    // Poster is optional: a failed poster fetch must not drop the item
    // (the .strm/.nfo are already written). Record the miss instead.
    try {
      const bytes = await client.fetch(posterUrl, true);
      await fs.writeFile(path.join(itemDir, "poster.jpg"), bytes);
    } catch (e) {
      entry.files.poster = null;
      entry.poster_error = String(e.message || e);
    }
  }
  return { entry };
}

async function main() {
  const args = parseArgs(process.argv);
  const configPath = path.resolve(args.config);
  const config = JSON.parse(await fs.readFile(configPath, "utf8"));
  const outDir = path.resolve(args.out || config.outputDir || "./output");
  const pruneSet = new Set(config.prune || []);
  const client = new IAClient(
    config.userAgent || "RetroTV/0.1",
    config.requestsPerSecond || 1
  );
  const limitOverride = args.limit ?? null;
  const folderFilter = args.folders ? new Set(args.folders) : null;

  await fs.mkdir(outDir, { recursive: true });

  // Version the catalog: keep the previous run as catalog.prev.json.
  const catalogPath = path.join(outDir, "catalog.json");
  let prevVersion = 0;
  try {
    const prev = JSON.parse(await fs.readFile(catalogPath, "utf8"));
    prevVersion = prev.version || 0;
    await fs.copyFile(catalogPath, path.join(outDir, "catalog.prev.json"));
  } catch { /* first run */ }

  const items = [];
  const skipped = [];
  let jobs = [];

  if (args.pilot) {
    jobs = (config.pilot || []).map((p) => ({ folder: p.folder, tier: p.tier, identifier: p.identifier }));
    console.log(`Pilot mode: ${jobs.length} curated items`);
  } else {
    for (const folder of config.folders || []) {
      if (folderFilter && !folderFilter.has(folder.name)) continue;
      const limit = limitOverride ?? folder.limit ?? 100;
      console.log(`Searching "${folder.name}" (${folder.tier}) — ${folder.query} [limit ${limit}]`);
      const { docs, numFound } = await client.search(folder.query, Math.min(limit, 1000));
      console.log(`  found ${numFound} total, taking ${docs.length}`);
      for (const d of docs) jobs.push({ folder: folder.name, tier: folder.tier, identifier: d.identifier });
    }
  }

  for (const job of jobs) {
    process.stdout.write(`- ${job.identifier} [${job.folder} / ${job.tier}] ... `);
    try {
      const res = await buildItem(client, outDir, job.folder, job.tier, job.identifier, pruneSet, config.overrides || {});
      if (res.entry) {
        items.push(res.entry);
        console.log(`OK (${res.entry.title.slice(0, 50)}${res.entry.year ? `, ${res.entry.year}` : ""})`);
      } else {
        skipped.push({ identifier: job.identifier, folder: job.folder, reason: res.reason });
        console.log(`SKIP (${res.reason})`);
      }
    } catch (e) {
      skipped.push({ identifier: job.identifier, folder: job.folder, reason: `error: ${e.message}` });
      console.log(`ERROR (${e.message})`);
    }
  }

  const catalog = {
    library: config.libraryName || "Retro TV",
    version: prevVersion + 1,
    generated_at: new Date().toISOString(),
    pilot: args.pilot,
    counts: {
      items: items.length,
      by_folder: {},
      by_tier: {},
    },
    items,
    skipped,
  };
  for (const it of items) {
    catalog.counts.by_folder[it.folder] = (catalog.counts.by_folder[it.folder] || 0) + 1;
    catalog.counts.by_tier[it.tier] = (catalog.counts.by_tier[it.tier] || 0) + 1;
  }
  await fs.writeFile(catalogPath, JSON.stringify(catalog, null, 2) + "\n", "utf8");
  console.log(`\nDone: ${items.length} items, ${skipped.length} skipped. Catalog v${catalog.version} -> ${catalogPath}`);
  console.log(`Folders: ${JSON.stringify(catalog.counts.by_folder)} | Tiers: ${JSON.stringify(catalog.counts.by_tier)}`);
}

main().catch((e) => {
  console.error("FATAL:", e);
  process.exit(1);
});
