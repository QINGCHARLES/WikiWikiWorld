# Section anchor links and Unicode heading IDs — release 2026-11-26

Deployed at `2026-11-27T03:33:14Z` from commit `40f576741621` (`40f57674162174740558ba35426cb4302a2c0a36` on `main`), adding hover/focus/touch section anchor links (`a.heading-anchor`) to top-level `h2` article headings and generated `Footnotes`/`Citations` headings, switching Markdig heading identifiers to Unicode-preserving `AutoIdentifierOptions.None`, and updating `{{Redirect}}` fragment validation to accept Unicode anchors.

This release changes application code, static CSS/JS, localization resources, documentation, and tests only. No database schema change is required, and rollback preserves all edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| Heading anchors release from `40f576741621` | `release-20261127T033139Z-40f576741621` | `sha256:c6b6f35723ed9d1ea61bc1a17f9c753aba16d1aa96a9234bab202a78c6f74528` |
| Previous production image (`2c166bca5a4f`) | `rollback-before-release-20261127T033139Z-40f576741621` (pinned on host) | `sha256:794e906dfc2ce50aff1efbe4231596e7cd12b84a2aaeed9295e35f9deac0bdb7` |

## Verification

- All 461 .NET tests (`dotnet test tests/WikiWikiWorld.Web.Tests -c Release`), all 4 new JavaScript interaction tests (`node --test tests/browser/heading-anchor.test.cjs`), headless Chromium UI/interaction checks, and `python scripts/localization.py check` passed before release.
- Pinned the previous running image (`sha256:794e906dfc2ce50aff1efbe4231596e7cd12b84a2aaeed9295e35f9deac0bdb7`) on the production host as `rollback-before-release-20261127T033139Z-40f576741621` and pulled the immutable release tag before stopping the previous container.
- Verified HTTP 200 over public HTTPS for `en.magazedia.com/playboy`, `en.magazedia.com/`, `magazedia.com/`, `ja.magazedia.com/`, `ar.magazedia.com/`, `en.cinepedia.wiki/`, and `fanfare.wiki/`, HTTP 404 on a missing slug, live `class="heading-anchor"` and `id="citations"` markup on `/playboy`, and versioned `/css/pages/article.css?v=EwfV6i7OXErLkBJNbk_pxNl7QhSXVAgr8x_5btR1bQA` plus `/js/article-view.js?v=xcN01Upty6Q1ezHDw6fYjoLMAw5bNysvNjE2vTdpZdE`.
- Verified container running as UID `1654` with `restarts=0`, then promoted GHCR `latest`.

## Rollback procedure

1. Restore the previous image on the production host:

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T033139Z-40f576741621'
   ```

2. Confirm the container is running with zero restarts:

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect --format "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to `sha256:794e906dfc2ce50aff1efbe4231596e7cd12b84a2aaeed9295e35f9deac0bdb7` (`release-20261127T005408Z-2c166bca5a4f`) before the next routine deploy.
