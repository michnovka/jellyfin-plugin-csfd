#!/usr/bin/env bash
# Build the plugin, deploy it into the local dev Jellyfin and restart it.
set -euo pipefail
cd "$(dirname "$0")"

dotnet build src/Jellyfin.Plugin.Csfd -c Debug

PLUGIN_DIR=dev/config/plugins/CsfdRating
mkdir -p "$PLUGIN_DIR"
cp src/Jellyfin.Plugin.Csfd/bin/Debug/net9.0/Jellyfin.Plugin.Csfd.dll "$PLUGIN_DIR/"

docker compose -f docker-compose.dev.yml up -d
docker restart jellyfin-csfd-dev >/dev/null
echo "Plugin deployed. Jellyfin restarting at http://localhost:8096"
echo "Logs: docker logs -f jellyfin-csfd-dev"
