#!/usr/bin/env bash
# Build a plugin release and publish it as a GitHub release, updating
# manifest.json so Jellyfin servers can install/update via the repository URL:
#   https://github.com/michnovka/jellyfin-plugin-csfd/releases/latest/download/manifest.json
#
# Usage: ./build-release.sh <version> [changelog]
#   e.g. ./build-release.sh 0.1.0.0 "Initial release"
set -euo pipefail
cd "$(dirname "$0")"

VERSION=${1:?usage: ./build-release.sh <version, e.g. 0.1.0.0> [changelog]}
CHANGELOG=${2:-"Release $VERSION"}
REPO="michnovka/jellyfin-plugin-csfd"
GUID="af0c9c45-a8e5-498a-b848-3963aade7e6e"
TARGET_ABI="10.11.0.0"
TS=$(date -u +%Y-%m-%dT%H:%M:%SZ)
ZIP="csfd-rating_${VERSION}.zip"
SOURCE_URL="https://github.com/${REPO}/releases/download/v${VERSION}/${ZIP}"

dotnet build src/Jellyfin.Plugin.Csfd -c Release "-p:AssemblyVersion=${VERSION}" "-p:FileVersion=${VERSION}"

mkdir -p dist
rm -f "dist/${ZIP}"

VERSION="$VERSION" TS="$TS" CHANGELOG="$CHANGELOG" GUID="$GUID" TARGET_ABI="$TARGET_ABI" python3 - <<'EOF'
import json, os
meta = {
    "category": "Metadata",
    "changelog": os.environ["CHANGELOG"],
    "description": "Fetches movie ratings from ČSFD and stores them in the native critic rating field.",
    "guid": os.environ["GUID"],
    "name": "ČSFD Rating",
    "overview": "ČSFD ratings as native critic rating",
    "owner": "michnovka",
    "targetAbi": os.environ["TARGET_ABI"],
    "timestamp": os.environ["TS"],
    "version": os.environ["VERSION"],
    "status": "Active",
    "autoUpdate": True,
    "imagePath": "",
}
with open("dist/meta.json", "w", encoding="utf-8") as f:
    json.dump(meta, f, ensure_ascii=False, indent=2)
EOF

zip -j -q "dist/${ZIP}" src/Jellyfin.Plugin.Csfd/bin/Release/net9.0/Jellyfin.Plugin.Csfd.dll dist/meta.json
MD5=$(md5sum "dist/${ZIP}" | cut -d' ' -f1)

VERSION="$VERSION" TS="$TS" CHANGELOG="$CHANGELOG" GUID="$GUID" TARGET_ABI="$TARGET_ABI" MD5="$MD5" SOURCE_URL="$SOURCE_URL" python3 - <<'EOF'
import json, os
entry = {
    "version": os.environ["VERSION"],
    "changelog": os.environ["CHANGELOG"],
    "targetAbi": os.environ["TARGET_ABI"],
    "sourceUrl": os.environ["SOURCE_URL"],
    "checksum": os.environ["MD5"],
    "timestamp": os.environ["TS"],
}
try:
    with open("manifest.json", encoding="utf-8") as f:
        manifest = json.load(f)
except FileNotFoundError:
    manifest = [{
        "guid": os.environ["GUID"],
        "name": "ČSFD Rating",
        "description": "Fetches movie ratings from ČSFD and stores them in the native critic rating field.",
        "overview": "ČSFD ratings as native critic rating",
        "owner": "michnovka",
        "category": "Metadata",
        "imageUrl": "",
        "versions": [],
    }]
manifest[0]["versions"] = [v for v in manifest[0]["versions"] if v["version"] != entry["version"]]
manifest[0]["versions"].insert(0, entry)
with open("manifest.json", "w", encoding="utf-8") as f:
    json.dump(manifest, f, ensure_ascii=False, indent=2)
    f.write("\n")
EOF

git add manifest.json
git commit -m "Release ${VERSION}" >/dev/null
git push
gh release create "v${VERSION}" "dist/${ZIP}" manifest.json --repo "$REPO" --title "v${VERSION}" --notes "$CHANGELOG"
echo "Released v${VERSION} (zip md5 ${MD5})"
