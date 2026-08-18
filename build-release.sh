#!/usr/bin/env bash
# Cut a release: verify, tag the current commit and push the tag.
# The Release GitHub Actions workflow then builds the tagged commit, publishes
# the release zip and updates manifest.json on main — do NOT create the GitHub
# release locally, that would collide with the workflow.
#
# Usage: ./build-release.sh <version> [changelog]
set -euo pipefail
cd "$(dirname "$0")"

VERSION=${1:?usage: ./build-release.sh <version, e.g. 0.2.1.0> [changelog]}
CHANGELOG=${2:-"Release $VERSION"}

[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "version must be N.N.N.N" >&2; exit 1; }
if [[ -n "$(git status --porcelain)" ]]; then
    echo "refusing to release from a dirty worktree; commit or stash first" >&2
    exit 1
fi

# Don't tag broken code.
dotnet build src/Jellyfin.Plugin.Csfd -c Release "-p:AssemblyVersion=${VERSION}" "-p:FileVersion=${VERSION}"
dotnet test tests/Jellyfin.Plugin.Csfd.Tests

git tag -a "v${VERSION}" -m "$CHANGELOG"
git push origin "v${VERSION}"
echo "Tag v${VERSION} pushed — the Release workflow publishes it (watch: gh run watch)"
