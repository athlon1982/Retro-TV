#!/bin/bash
# Retro TV plugin — build + package + repository manifest.
# Builds the plugin (Release), zips the DLL + meta.json + icon into the
# release zip, computes its MD5 checksum, and writes the checksum into
# manifest.json. Nothing is uploaded or pushed anywhere.
# Usage: ./build.sh   (run from this directory: plugin/src/Jellyfin.Plugin.RetroTv/)
set -euo pipefail

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
if [ ! -x "$DOTNET" ]; then DOTNET="dotnet"; fi

VERSION="$(grep -oP '<Version>\K[^<]+' Jellyfin.Plugin.RetroTv.csproj)"
GUID="25eef671-4023-4c23-badb-6c051fa0620f"
ZIP="retro-tv_${VERSION}.zip"
OUT="bin/Release/net10.0"

echo "=== Retro TV plugin build v${VERSION} ==="
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
"$DOTNET" build -c Release --nologo

echo "=== Packaging ${ZIP} ==="
rm -rf dist && mkdir -p "dist/retro-tv"
cp "${OUT}/Jellyfin.Plugin.RetroTv.dll" "dist/retro-tv/"
cp meta.json "dist/retro-tv/"
cp icon.webp "dist/retro-tv/" 2>/dev/null || cp ../../../repo/assets/icon.webp "dist/retro-tv/icon.webp"
python3 - <<PYEOF
import zipfile, os
zippath = "dist/${ZIP}"
with zipfile.ZipFile(zippath, "w", zipfile.ZIP_DEFLATED) as zf:
    for name in sorted(os.listdir("dist/retro-tv")):
        zf.write(os.path.join("dist/retro-tv", name), name)
print("zip:", zippath)
PYEOF

CHECKSUM="$(md5sum "dist/${ZIP}" | cut -d' ' -f1)"
echo "MD5: ${CHECKSUM}"

echo "=== Updating manifest.json checksum ==="
python3 - "$CHECKSUM" "$VERSION" <<'PYEOF'
import json, sys
checksum, version = sys.argv[1], sys.argv[2]
path = "../../manifest.json"
with open(path) as f:
    manifest = json.load(f)
entry = manifest[0]
assert entry["guid"] == "25eef671-4023-4c23-badb-6c051fa0620f", "manifest guid mismatch"
# The manifest keeps one entry per released version (newest first). Add this
# version if it is not there yet, then fill in its checksum.
versions = entry.setdefault("versions", [])
v = next((x for x in versions if x.get("version") == version), None)
if v is None:
    v = {
        "version": version,
        "changelog": "Channel guide — deterministic 48 h schedules, BackWhen-shaped lineup, themeable guide page, M3U + XMLTV, live ffmpeg streams, weather channel, folder.jpg write.",
        "targetAbi": "12.0.0.0",
        "sourceUrl": "https://github.com/<owner>/retro-tv/releases/download/v" + version + "/retro-tv_" + version + ".zip",
        "checksum": "",
        "timestamp": "2026-11-08T00:00:00Z",
    }
    versions.insert(0, v)
v["checksum"] = checksum
with open(path, "w") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
print("manifest.json checksum set to", checksum, "for version", version)
PYEOF

echo
echo "=== Done ==="
echo "Zip:       $(pwd)/dist/${ZIP}"
echo "Manifest:  $(pwd)/../../manifest.json"
echo "To publish (Michael's call, not this script):"
echo "  1. Push the repo to GitHub, create release v${VERSION}, attach dist/${ZIP}."
echo "  2. Replace <owner> in manifest.json sourceUrl/imageUrl with the real GitHub owner."
echo "  3. Host manifest.json at https://raw.githubusercontent.com/<owner>/retro-tv/main/manifest.json"
echo "  4. In Jellyfin: Dashboard > Plugins > Repositories > add that raw URL."
