# API article renames and old-slug redirects — release 2026-11-15

The owner asked to add both features, then commit, push and deploy.

## Change

`c473aad` (`Allow renaming articles through the API and redirect old slugs`):

- `PUT /api/article/{UrlSlug}` accepts an optional `NewUrlSlug`. It goes through the existing
  `ArticleRevisionService.UpdateArticleAsync` rename path, so the new revision keeps the
  `CanonicalArticleId` and earlier revisions keep the old slug. A slug used by another current article
  returns 409; a reserved system path returns 400. See [the API docs](api.md#update-article-revision).
- When no current article holds a requested slug, the article page finds the newest non-current,
  non-deleted revision stored under it and redirects with **302** to that article's current path. A
  page that later takes the slug wins, because current revisions are looked up first. The redirect is
  temporary because browsers cache a 301 indefinitely and slugs here are reused; during testing a
  cached 301 kept showing the renamed article after a new page took the old slug.
- Not changed: links to an old slug in other articles' text, `file:` references and `{{CoverGrid}}`
  lists are not rewritten, and those lookups do not follow the redirect. Requests for a specific old
  revision URL (`/old-slug/revision/...`) are unaffected.

No database change: renames already used the existing `ArticleRevisions` rows, and both lookups use the
existing `(SiteId, Culture, UrlSlug, …)` indexes.

## Source and deployment

- Source: `c473aad74136` on `main`, pushed to `origin/main` before publishing. It builds on
  `e3dc54f`, which production was running (`release-20261114T060635Z-e3dc54fbda25`).
- Release: `release-20261115T010113Z-c473aad74136`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:e3e16ae6b4b7ff60edcc2203bfbe33b3cfe454013d5fe721811869de6a06a2cb`,
  published with the release tag and `latest`, pulled by tag on the host and run by digest through
  `/home/deploy/update.sh`. Started `2026-11-15T01:02:10Z`. The host's `update.sh` matches the repository copy.

## Verification

- Release build passed with only the existing ImageSharp NU1902/NU1903 advisories; all 395 .NET tests passed.
- Development, through the API with DevvyTheBot, on test articles since deleted: rename returned 200 and
  history listed revisions under both slugs; renaming onto a taken slug returned 409; onto `api`, 400; an
  empty `NewUrlSlug` kept the slug. The old slug answered 302 to the new one; a deleted article's slug and
  a never-used slug returned 404; a new article on a previously renamed slug was served itself.
- Production, anonymous: 200 for `celebrations`, `the-e-ticket`, `outlander-issue-02` and Cinepedia's
  `spider-man-no-way-home`; 404 for the soft-deleted `celebrations-magazine`, `outlander-no-1` and
  `cover-of-outlander-no-1` and for a never-used slug. Zero restarts and no `fail:`, `crit:` or
  exception log lines.
- Not verified in production: an actual rename, because none was requested.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261115T010113Z-c473aad74136`
(`sha256:e2ffa2726f2543f83dbba660b2a28de727999c37ba3d34a10217e969f423dad8`,
`release-20261114T060635Z-e3dc54fbda25`). Pass it to `/home/deploy/update.sh`; no database restore is needed.
