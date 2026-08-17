#!/usr/bin/env bash
# Build a plugin release and publish it as a GitHub release, updating
# manifest.json so Jellyfin servers can install/update via the repository URL:
#   https://github.com/michnovka/jellyfin-plugin-csfd/releases/latest/download/manifest.json
#
# Usage: ./build-release.sh <version> [changelog]
#   e.g. ./build-release.sh 0.2.0.0 "Series support"
set -euo pipefail
cd "$(dirname "$0")"

VERSION=${1:?usage: ./build-release.sh <version, e.g. 0.2.0.0> [changelog]}
CHANGELOG=${2:-"Release $VERSION"}
REPO="michnovka/jellyfin-plugin-csfd"

[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "version must be N.N.N.N" >&2; exit 1; }
if [[ -n "$(git status --porcelain)" ]]; then
    echo "refusing to release from a dirty worktree; commit or stash first" >&2
    exit 1
fi

dotnet build src/Jellyfin.Plugin.Csfd -c Release "-p:AssemblyVersion=${VERSION}" "-p:FileVersion=${VERSION}"
python3 scripts/package.py "$VERSION" "$CHANGELOG"

# Release (with the zip) is created before the manifest advertises it.
gh release create "v${VERSION}" "dist/csfd-rating_${VERSION}.zip" --repo "$REPO" --title "v${VERSION}" --notes "$CHANGELOG"

git add manifest.json
git commit -m "Release ${VERSION}" >/dev/null
git push
gh release upload "v${VERSION}" manifest.json --repo "$REPO"
echo "Released v${VERSION}"
