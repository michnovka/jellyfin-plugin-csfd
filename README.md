# Jellyfin ČSFD Rating plugin

Fetches movie ratings from [ČSFD](https://www.csfd.cz) and stores them in
Jellyfin's native **critic rating** field, so the percentage shows up in every
Jellyfin client (web, Android, TV) without any UI hacks. Also registers the
ČSFD id as an external id, adding a clickable ČSFD link on movie detail pages.

## How it works

- ČSFD has no public API; the plugin scrapes `csfd.cz` politely (serialized
  requests, configurable delay, default 1.5 s).
- ČSFD is protected by [Anubis](https://anubis.techaro.lol/) proof-of-work;
  the plugin solves the challenge (SHA-256, difficulty 1) and keeps the auth
  cookie for subsequent requests.
- Movies are matched by title (Czech or original) + production year.
- Two update paths:
  - a custom metadata provider that runs on metadata refresh / new items,
  - a scheduled task ("Refresh ČSFD ratings") that backfills the whole library.

## ČSFD look in the web client (optional)

Clients render critic rating with a rotten-tomatoes icon (fresh ≥ 60). To show
the ČSFD look in the web client instead, add Custom CSS in Dashboard → General:

```css
.mediaInfoCriticRatingFresh,
.mediaInfoCriticRatingRotten {
    background-image: url('https://static.pmgstatic.com/assets/images/60b418342f47054c7481ad9e0c8e40b4/apple-touch-icon.png');
}
```

## Development

Local dev Jellyfin (same version as production) runs via Docker:

```bash
./dev.sh          # build + deploy plugin + restart dev Jellyfin
```

Dev server: http://localhost:8096 — config, cache and media live in `dev/`
(gitignored).

## Deployment

Copy `Jellyfin.Plugin.Csfd.dll` into the Jellyfin `config/plugins/CsfdRating/`
directory and restart Jellyfin.
