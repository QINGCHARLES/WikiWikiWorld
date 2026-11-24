# Delete article from the menu — release 2026-11-24

The owner asked to push and deploy.

## Change

- `242e490`: the article menu's "Delete article" item and the editor's delete button go to a confirmation page at
  `/{slug}/delete`, routed by `ArticleRouteTransformer` like `/edit` and `/history`. It replaces
  `/Article/Delete?UrlSlug=…`.
  - The page names the article and refuses accounts that may not delete it. After a delete it goes to the home page.
  - The editor and the page agree on what deleting does: the article is hidden, and only an administrator can
    restore it.
  - Delete controls show only to accounts that may delete that article, according to
    `PermissionService.MayDeleteArticleAsync`: the owner for an `@user` page, the `DeleteArticles` permission
    otherwise.
  - `robots.txt` disallows `/*/delete`.
  - Addresses all four points of GitHub issue #7.
- `4c0ef4f`, `61acc67`: release records only.

No database change. Nothing under `deploy/` changed. `git log 5458ba2..main` had no schema scripts.

## Source and deployment

- Source: `242e4901bcf9` on `main`, pushed to `origin/main` (`61acc67..242e490`) before publishing.
- Release: `release-20261124T060314Z-242e4901bcf9`, published with `latest`, family `resolute-chiseled-extra`.
- Image ID on the host: `sha256:0c8666bc266acb1848b5a10ef6b1d15d81a5e228c321ee51ab7f399e3df3c4d5`. Run through
  `/home/deploy/update.sh` with the release tag, started `2026-11-24T06:03:53Z`.

## Verification

- All 436 tests passed.
- Development, on the branch build:
  - As an Administrator: the menu and editor link to `/cli-llm-test-kerrang/delete`, and the page names the article.
    Deleting through the page landed on the home page with the article gone. The article was then restored.
  - As a member: no delete controls in the menu or the editor, and the page refused.
  - As an owner: their own `@user` page offers delete and the page opens.
- Production, anonymous:
  - 200 for the en, ja and ar home pages, `gq-hype`, Cinepedia and the sitemap.
  - `/gq-hype/delete` and `/gq-hype/edit` redirect to sign-in.
  - No delete item is rendered for anonymous visitors, and `robots.txt` lists `/*/delete`.
  - Zero restarts, and no `fail:`, `crit:` or exception log lines.
- Not verified in production: a signed-in delete. It needs the owner's account, and it would delete a live article.

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261124T060314Z-242e4901bcf9` (the roles and
hatnote release, `sha256:f499799a…`) to `/home/deploy/update.sh`, then point `latest` at it. No database restore is
needed.
