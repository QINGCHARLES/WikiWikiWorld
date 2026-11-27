# Home-page header fallback and responsive multi-column grid — release 2026-11-27

Deployed at `2026-11-27T08:07:25Z` from commit `1da39f8e585a` (`1da39f8e585a3b5c206b5caa430744f0e410733f` on `main`, following `8f53e4eeeb2d`), updating the home page to:

- Include standard articles that have no resolved `{{CoverCarousel}}` cover but have a resolved `{{HeaderImage}}` (preserving `Focus=top|center|bottom`).
- Style header-backed entries as two-column landscape tiles (`cover-item--header`) aligned to the row height of adjacent `0.7:1` portrait covers.
- Break `.home-cover-grid` out of `main`'s `65ch` prose column (`min(100vw - 2 * var(--grid-gutter), 115rem)`) so column counts scale continuously with viewport width up to 8 columns (`115rem` / `1840px`).

This release changes application code, Razor markup, static CSS, documentation, and tests only. No database schema change is required, and rollback preserves all edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| Responsive grid breakout release from `1da39f8e585a` | `release-20261127T080630Z-1da39f8e585a` | `sha256:38d04e7a8221792be2eaba69654082a066f849a731e5b9b3e21ed7d7d8906229` |
| Initial header-fallback release (`8f53e4eeeb2d`) | `rollback-before-release-20261127T080630Z-1da39f8e585a` (pinned on host) | `sha256:ac28731b6c36720d6174f519eaaaabd17da8e8cd1f034554d75f6506216e74c7` |
| Pre-feature production image (`40f576741621`) | `rollback-before-release-20261127T070551Z-8f53e4eeeb2d` (pinned on host) | `sha256:c6b6f35723ed9d1ea61bc1a17f9c753aba16d1aa96a9234bab202a78c6f74528` |

## Verification

- All 474 .NET tests (`dotnet test -c Release`), `python scripts/localization.py check`, and Chromium layout checks across `375×812` (1 col), `820×900` (3 cols), `1280×900` (5 cols), `1440×900` (6 cols, LTR and RTL), `1920×1080` (8 cols), and `2560×1440` (8 cols capped at `1840px`, centered) passed before release.
- Pinned the previous running image (`sha256:ac28731b6c36720d6174f519eaaaabd17da8e8cd1f034554d75f6506216e74c7`) on the production host as `rollback-before-release-20261127T080630Z-1da39f8e585a` and pulled the immutable release tag before stopping the previous container.
- Verified HTTP 200 over public HTTPS for `en.magazedia.com/`, `magazedia.com/`, `ja.magazedia.com/`, `ar.magazedia.com/`, `en.cinepedia.wiki/`, `fanfare.wiki/`, `crtr.world/`, and `en.magazedia.com/playboy`, plus `--cover-grid-max: 115rem` served in versioned `/css/pages/home.css?v=-NhYfX5C1yAB0SFgq7TDwjX2B2KiYhvGWi8TOHW5Vi8`.
- Verified container running as UID `1654` with `Restarts=0` and clean logs, then promoted GHCR `latest` (`sha256:38d04e7a8221792be2eaba69654082a066f849a731e5b9b3e21ed7d7d8906229`).

## Rollback procedure

1. Restore the previous image on the production host (use `rollback-before-release-20261127T080630Z-1da39f8e585a` to keep header fallback at `65ch`, or `rollback-before-release-20261127T070551Z-8f53e4eeeb2d` to revert before header fallback):

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T080630Z-1da39f8e585a'
   ```

2. Confirm the container is running with zero restarts:

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect --format "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to the restored image before the next routine deploy.
