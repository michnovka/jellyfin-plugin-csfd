<p align="center"><img src="assets/icon.png" alt="ČSFD Rating" width="96"/></p>

# Jellyfin ČSFD Rating plugin

[![CI](https://github.com/michnovka/jellyfin-plugin-csfd/actions/workflows/ci.yml/badge.svg)](https://github.com/michnovka/jellyfin-plugin-csfd/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/michnovka/jellyfin-plugin-csfd)](https://github.com/michnovka/jellyfin-plugin-csfd/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Fetches movie and TV series ratings from [ČSFD](https://www.csfd.cz) and stores
them in Jellyfin's native **critic rating** field, so the percentage shows up in
every Jellyfin client (web, Android, TV) without any UI hacks. Also registers
the ČSFD id as an external id, adding a clickable ČSFD link on detail pages.

## Installation

1. Dashboard → **Plugins** → **Repositories** → add:
   `https://github.com/michnovka/jellyfin-plugin-csfd/releases/latest/download/manifest.json`
2. Install **ČSFD Rating** from the catalog and restart Jellyfin.
3. Run the **Refresh ČSFD ratings** scheduled task once to backfill your library.

Requires Jellyfin 10.11.

## Features

- Movie **and series** ratings (ČSFD "seriály" and "pořady").
- Careful matching, precision-first: title (Czech or original, diacritics- and
  punctuation-insensitive) + production year, with every match verified against
  the film page's full name list. Ambiguous items get *no* rating rather than a
  wrong one.
- Manual override: put a ČSFD id (e.g. `4570-pelisky`) into the item's metadata
  editor ČSFD field and refresh — the plugin uses it from then on.
- Settings page tools: **unmatched items report** (with ČSFD search links) and a
  **match-preview** dry-run for any title + year.
- Minimum-votes threshold (default 100) to skip unreliable ratings.
- Polite scraping: serialized requests (1.5 s delay), backoff on throttling, and
  automatic solving of ČSFD's [Anubis](https://anubis.techaro.lol/) anti-bot
  proof-of-work (cookie persisted across restarts).
- Scheduled task skips recently refreshed items (default 30 days) and runs
  monthly by default.

## How it works

ČSFD has no public API; the plugin scrapes `csfd.cz` politely. Ratings come
from the film page (visible rating + schema.org JSON-LD, which also provides
vote counts and the movie/series type). Matching runs the search page first and
verifies candidates against their film page before anything is stored.

## ČSFD look in the web client (optional)

Clients render critic rating with a rotten-tomatoes icon (fresh ≥ 60). To show
the ČSFD logo instead — only for items actually matched on ČSFD — add Custom
CSS in Dashboard → General:

```css
/* ČSFD logo only for items matched on ČSFD (detected via the ČSFD external
   link the plugin adds); unmatched items keep the Rotten Tomatoes icons. */
.itemDetailPage:has(.itemExternalLinks a[href*="csfd.cz"]) .mediaInfoCriticRatingFresh,
.itemDetailPage:has(.itemExternalLinks a[href*="csfd.cz"]) .mediaInfoCriticRatingRotten {
    background-image: url('https://static.pmgstatic.com/assets/images/c81c12476e7c622b1c771cd9187a56e2/apple-touch-icon.png');
}
```

Drop the `:has(...)` prefixes to show the ČSFD logo unconditionally (needed for
older TVs whose browsers lack `:has()` — e.g. LG webOS before 2024 models).
More robust: download that PNG and embed it as a `data:image/png;base64,...`
URI so the icon doesn't depend on hotlinking csfd.cz's CDN. The webOS app loads
the server's web client, so this CSS applies there too; native Android/TV apps
show the standard icon with the ČSFD number.

## Development

```bash
./dev.sh          # build + deploy plugin into a local dockerized Jellyfin
dotnet test tests/Jellyfin.Plugin.Csfd.Tests
```

Dev server: http://localhost:8096 — config, cache and media live in `dev/`
(gitignored). Releases: `./build-release.sh <version> [changelog]` tags the
current commit; the GitHub Actions Release workflow builds, publishes and
updates `manifest.json`.

## License

[MIT](LICENSE). Not affiliated with ČSFD.cz — ratings and the ČSFD name belong
to POMO Media Group s.r.o.; this plugin is a personal-use metadata fetcher.
