# Bookmark notes and list introductions UI — release 2026-11-08

The owner approved the clickable mock-up and authorized committing, pushing and deploying the
real UI to production. Behaviour is described in
[Notes and introductions UI](bookmark-lists.md#notes-and-introductions-ui).

## Source and deployment

- Source: `aa617f79ed75b56f39e060433fceee1b1aba3675` (`Add bookmark note and list introduction UI`).
  It also deploys `d21d61c` (`Restore a removed bookmark's note when the article is saved again`),
  which had been committed but not yet deployed.
- Release: `release-20261108T182305Z-aa617f79ed75`.
- Production image: `ghcr.io/qingcharles/wikiwikiworld@sha256:dab097adcb235dca08f55511510553236b719267409d3cc07123f0bf18284bea`
  (linux/amd64, user 1654). SDK container publishing pushed the immutable release tag and `latest`
  straight to GHCR, with `ContainerFamily=noble-chiseled-extra`. The host pulled the digest before
  `/home/deploy/update.sh` replaced the container.
- Production started at `2026-11-08T18:24:37.79944856Z`, keeping the
  `/opt/wiki/data -> /app/data` and `/opt/wiki/certs -> /app/certs` mounts.

No database change was needed. `Bookmarks.Notes` and `BookmarkLists.Description` were already in
both schemas from the earlier 2026-11-08 releases. Nothing under `deploy/` changed.

## Verification

- Release build passed with only the existing ImageSharp NU1902/NU1903 advisories. All 395 .NET
  tests and all 27 browser-script tests passed.
- **Development browser checks.** These ran signed in as DevvyTheBot on the preview instance, at
  1280×900 and at 375×812.
  - Article page: the menu shows a note button and an excerpt per saved list. The post-save
    "Add a note" prompt appears. The note dialog saves with Ctrl+Enter, the menu and status refresh,
    and the dialog closes. On phones the dialog is a bottom sheet with no horizontal overflow.
  - Bookmarks page:
    - The introduction empty state is shown, and the Write/Preview dialog works. Preview escapes
      raw HTML, and saving the introduction works.
    - The inline note editor works, and long notes clamp with Read more / Show less.
    - The publish confirmation counts notes and mentions the introduction.
    - An anonymous fetch of the public list showed the byline, introduction and note, with no
      owner controls.
  - The test bookmark, note and introduction were then cleared, and the list is private again.
- **Production checks.** These were anonymous only.
  - Nine TLS-validated pages returned 200: English, root, Japanese and Arabic Magazedia, English
    Cinepedia, Fanfare, English CRTR, the header helper and a representative article.
  - Anonymous article pages include neither the note dialog nor `bookmark-notes.js`.
  - The served `icons.svg` contains the new Note and Lock symbols. `bookmark-notes.js` and the new
    profile CSS are served.
  - The anonymous `/@QINGCHARLES/bookmarks` page shows only the no-public-lists message. Production
    currently has no public lists.
  - The container reported zero restarts and no exception, `fail:` or `crit:` log lines.
- **Not verified.** Signed-in flows were not verified in production, because that needs a person
  signed in. The re-save path that shows "Your earlier note is back" was checked by the service
  test only, not in a browser.

## Rollback

Previous image retained on the host:
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261108T182305Z-aa617f79ed75`.
Previous digest:
`ghcr.io/qingcharles/wikiwikiworld@sha256:63c7b5884bcbcddb710539a1b119a6e080c7fee0dedb79cc770b7aff6d7be0b1`
(`release-20261108T073412Z-41c9c7f7cfcb`). Pass it to `/home/deploy/update.sh` to roll back. No
database restore is needed: the previous application reads the same schema, and notes or
introductions written since then stay in place.
