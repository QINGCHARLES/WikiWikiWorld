# Revision lookup and line-ending release

Deployed commit `d206ff78eef8` at about 5:58 p.m. CST on 2026-11-27 (`2026-11-27T23:58Z`). The image was built from a clean worktree of that commit, so uncommitted work in the main checkout was not included. The release requires no schema change.

- Revision timestamps match the span they name. A 14-digit `yyyyMMddHHmmss` timestamp now finds a revision saved within that second. The article API returns `400` for an unreadable `Revision` and `404` for a missing one, instead of the current revision.
- New revisions store article text with LF line endings (`ArticleRevision.Text` converts CRLF and lone CR). Existing CRLF rows convert on their next save.
- In Development only, startup recreates DevvyTheBot from `.local/DevvyTheBot.json` when it is missing. Production is unaffected.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| This release | `release-20261127T235711Z-d206ff78eef8` (also `latest`) | `sha256:1a9ea142fb2c8be2049e8a62c1b90a211ac1641e44d059e49b10c85c7c706278` |
| Previous production image (user administration, `2152ce2c5fba`) | `rollback-before-release-20261127T235711Z-d206ff78eef8` (pinned on host and in GHCR) | `sha256:8e388b143edad864bcdffc3e76c21a6bc600b870249980d6e7b25548d334acc1` |

## Verification

- All 474 .NET tests (`dotnet test -c Release`) and `python scripts/localization.py check` passed in the release worktree. Four `CanonicalDomainMiddlewareTests` cases need the Git-ignored `appsettings.Development.json`; they passed once it was copied in and failed without it.
- Host `/home/deploy/update.sh` matched `deploy/update.sh` (`9e96f7d71f3848bbf93b665a071b9ed20bb81064b0c3b9eeb9495db428a93cba`). The previous image was pinned and the release tag pulled before the old container stopped.
- Container running as UID `1654` with `restarts=0` and zero `fail:`, `crit:` or unhandled-exception log entries.
- HTTP 200 over public HTTPS for `en.magazedia.com/`, `magazedia.com/`, `ja.magazedia.com/`, `ar.magazedia.com/`, `en.cinepedia.wiki/`, `fanfare.wiki/`, `crtr.world/`, `en.magazedia.com/playboy` and `en.magazedia.com/byte/revision/20261126065328`.
- `GET api/article/byte?Revision=20261126065328` returned older revision 1405 (previously the current revision); `…065329` returned `404`; a 13-digit value returned `400`.
- GHCR `latest` promoted to the release digest after these checks.

## Rollback

```sh
ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T235711Z-d206ff78eef8'
```

Then point GHCR `latest` back at the previous digest from a machine with Buildx and package write access (the production host has both):

```sh
docker buildx imagetools create --prefer-index=false --tag ghcr.io/qingcharles/wikiwikiworld:latest ghcr.io/qingcharles/wikiwikiworld@sha256:8e388b143edad864bcdffc3e76c21a6bc600b870249980d6e7b25548d334acc1
```

Revisions saved by this release are plain LF text, which the previous build reads normally.
