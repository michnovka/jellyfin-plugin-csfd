#!/usr/bin/env python3
"""Package a built plugin into a release zip and update manifest.json.

Usage: package.py <version> <changelog>
Expects the plugin already built at src/.../bin/Release/net9.0/.
Writes dist/csfd-rating_<version>.zip and updates manifest.json in place.
Prints the zip's MD5 checksum.
"""
import hashlib
import json
import re
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path

GUID = "af0c9c45-a8e5-498a-b848-3963aade7e6e"
TARGET_ABI = "10.11.0.0"
REPO = "michnovka/jellyfin-plugin-csfd"
NAME = "ČSFD Rating"
DESCRIPTION = "Fetches movie and series ratings from ČSFD and stores them in the native critic rating field."
OVERVIEW = "ČSFD ratings as native critic rating"
OWNER = "michnovka"
IMAGE_URL = "https://raw.githubusercontent.com/michnovka/jellyfin-plugin-csfd/main/assets/icon.png"

def main() -> None:
    version, changelog = sys.argv[1], sys.argv[2]
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        sys.exit("version must be N.N.N.N")
    root = Path(__file__).resolve().parent.parent
    dll = root / "src/Jellyfin.Plugin.Csfd/bin/Release/net9.0/Jellyfin.Plugin.Csfd.dll"
    if not dll.exists():
        sys.exit(f"missing {dll}; build first")

    timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    meta = {
        "category": "Metadata",
        "changelog": changelog,
        "description": DESCRIPTION,
        "guid": GUID,
        "name": NAME,
        "overview": OVERVIEW,
        "owner": OWNER,
        "targetAbi": TARGET_ABI,
        "timestamp": timestamp,
        "version": version,
        "status": "Active",
        "autoUpdate": True,
        "imagePath": "",
    }

    dist = root / "dist"
    dist.mkdir(exist_ok=True)
    zip_path = dist / f"csfd-rating_{version}.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.write(dll, dll.name)
        zf.writestr("meta.json", json.dumps(meta, ensure_ascii=False, indent=2))

    md5 = hashlib.md5(zip_path.read_bytes()).hexdigest()

    manifest_path = root / "manifest.json"
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    else:
        manifest = [{
            "guid": GUID,
            "name": NAME,
            "description": DESCRIPTION,
            "overview": OVERVIEW,
            "owner": OWNER,
            "category": "Metadata",
            "imageUrl": IMAGE_URL,
            "versions": [],
        }]
    manifest[0]["imageUrl"] = IMAGE_URL

    entry = {
        "version": version,
        "changelog": changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": f"https://github.com/{REPO}/releases/download/v{version}/{zip_path.name}",
        "checksum": md5,
        "timestamp": timestamp,
    }
    manifest[0]["versions"] = [v for v in manifest[0]["versions"] if v["version"] != version]
    manifest[0]["versions"].insert(0, entry)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(md5)

if __name__ == "__main__":
    main()
