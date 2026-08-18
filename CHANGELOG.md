# Changelog

## 0.3.0.0

- ČSFD "pořad"-typed entries (panel/talk shows) are now series-eligible, recovering shows like The Graham Norton Show.
- Match-preview tool on the settings page: dry-run the matcher for any title + year.
- Plugin icon.
- Resolver test suite over an injectable HTTP layer (37 tests total).
- Dependabot for pinned GitHub Actions and NuGet packages.
- Single release path: local script only tags; the tag workflow builds, publishes and updates the manifest (idempotent).

## 0.2.0.0

- TV series support (ČSFD seriály).
- Unmatched-items report on the settings page.
- Minimum-votes threshold (default 100) — too-few-vote ratings are skipped.
- Staleness-aware scheduled task (default: skip items refreshed within 30 days; monthly default trigger).
- Matching: exact-title priority over stopword-tolerant matching, film-page verification required for every match, "&" ≙ "and", superscript/fraction normalization, pre-colon retry for long titles.
- Anubis cookie persisted across restarts; hardened HTTP (redirects pinned to csfd.cz, 5 MB response cap, 429/5xx backoff, regex timeouts); atomic state/cookie writes.
- Fixture-based parser regression tests; CI + tag-release workflows; MIT license.

## 0.1.1.0

- Film-page verification pass: fixes misses caused by ČSFD omitting the original-name line on exact search hits.
- Superscript/fraction title normalization ("The Accountant²", "Naked Gun 33⅓").
- Per-title logging in the backfill task.

## 0.1.0.0

- Initial release: movie ratings from ČSFD as native critic rating, ČSFD external id + link, scheduled backfill task, Anubis proof-of-work support.
