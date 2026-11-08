# Individual bookmark notes — release 2026-11-08

The owner authorized completing the original per-bookmark note backend, committing, pushing
and deploying it. List-header descriptions remain separate and unchanged.

## Source and deployment

- Source: `41c9c7f7cfcb5281284972a6b25245a7bd1dc55b` (`Add Markdown notes to individual bookmark memberships`).
- Release: `release-20261108T073412Z-41c9c7f7cfcb`.
- Production image: `ghcr.io/qingcharles/wikiwikiworld@sha256:63c7b5884bcbcddb710539a1b119a6e080c7fee0dedb79cc770b7aff6d7be0b1`.
  The immutable release tag and `latest` are published to GHCR. The image was successfully
  pulled by digest before the host's `/home/deploy/update.sh` replaced the old container.
- SDK container publishing targeted Linux amd64 with `10.0-noble-chiseled-extra` and
  `InvariantGlobalization=false`. The transferred archive SHA-256 was verified before loading:
  `25ef6041c0019b32550a9e899fd1a7ca43c43a9b064574ea7aa611f6dfe231a3`.
- Production started at `2026-11-08T07:40:39.937391712Z`, with the existing data/certificate
  mounts and UID 1654. Verification reported zero restarts.
- The Windows development app was refreshed from the verified Release build at
  `E:/source/WikiWikiWorld/.local/bookmark-notes-tests-20261108/bin/WikiWikiWorld.Web/release/WikiWikiWorld.Web.exe`,
  using the source Web content root, Development configuration, existing data directory and
  existing trusted certificate. Its HTTPS homepage on port 7126 returned 200.

The feature stores optional Markdown in `Bookmarks.Notes`, limits input to 10,000 UTF-16
code units, validates active membership/parent ownership and current site, and includes source
in the authorized paged projection. It uses `RenderUserContentMarkdown` for rendering.
Blank input clears notes; meaningful whitespace, save dates and list headers are preserved.
Removal/undo retains the original note; a new membership starts blank and an older undo does
not overwrite an already re-added membership's note. UI controls remain deferred, as agreed
for the original backend scope; no HTTP endpoints or preview UI were added.

## Database upgrades and backups

Read-only inspection found only the expected missing `Bookmarks.Notes` column in each database.
The reviewed `AddBookmarkNotes.sql` and companion `CheckBookmarkNotesBefore.sql` /
`VerifyBookmarkNotes.sql` were rehearsed through the maintenance runner on consistent
disposable backups of both actual databases before application.

| Environment | Database | Consistent maintenance backup | Backup time (UTC) |
| --- | --- | --- | --- |
| Development | `E:/source/WikiWikiWorld/data/app.db` | `E:/Source/WikiWikiWorld/data/.db-backups/app.db-20261108T073423Z-ca77h539.db` | 07:34:23 |
| Production | `/opt/wiki/data/app.db` | `/opt/wiki/data/.db-backups/app.db-20261108T073455Z-pdxae7nt.db` | 07:34:55 |

Development used `deploy/db_update.py development`; production used the standard host-side
`/home/deploy/db-maintenance/db-update.sh production` as UID/GID 1654. The runner files matched
repository SHA-256 values. SQL and release verification helpers remain staged at
`/home/deploy/db-maintenance/bookmark-notes-20261108T073151Z`. The production backup is
owned by `1654:1654` with mode `600`. Retain both backups.

The additive transaction changed only the schema by adding nullable, default-free `Notes TEXT`.
Both operations passed precheck, integrity, foreign-key and postcondition assertions.
Comparisons against the maintenance backups confirmed every retained value in all 26 application
tables and all `sqlite_sequence` entries were preserved, including existing list descriptions.
Development retained 6 lists and 17 memberships; production retained 2 lists and 4 memberships.
All old memberships initially had null notes.

Both live schemas match fresh `Create.sql` for table strictness, WITHOUT ROWID flags, ordered
columns, defaults, keys, foreign keys, CHECK expressions, index columns/order/collations/
expressions and partial predicates, views and triggers. Their shared structural SHA-256 is
`4730790825f556ca21b866943f0edc57e4687d303c56415ac91d494665ce3fed`.
Development and production keep separate content; no live data was copied between them.

## Verification

- Release build and all 395 .NET tests passed, including 11 new membership-note cases.
  All 14 Python maintenance tests and 27 JavaScript interaction tests passed.
- The actual image ran on loopback-only fixture ports against an isolated consistent production
  backup. A small external .NET probe loaded the image's own `/app/WikiWikiWorld.Web.dll`
  and data assemblies, rather than replacement application DLLs. It exercised note creation/
  editing, ownership refusal, repeat-save preservation, public-list reads, Markdown rendering,
  retained save dates and list-header preservation. The stored note was read again after the
  application container restarted.
- All 32 stored article probes, discussion restrictions, preserved formatting, Japanese culture
  rendering and fresh-schema checks passed before and after restart. The isolated login form
  created persisted data-protection keys.
- Eight production TLS-validated HTTPS pages returned 200: English/root/Japanese/Arabic
  Magazedia, English Cinepedia, Fanfare, CRTR and the header helper. Anonymous `/message/new`
  returned 302 to login; the existing SVG logo retained the sandbox CSP and `nosniff`.
  Development HTTPS returned 200 using an explicit loopback resolution for its documented
  hostname, without modifying DNS or certificate trust.
- The isolated container, copied production database, generated keys, note-probe state and
  all synthetic users/articles/bookmarks were removed. No synthetic content was inserted
  into either live database.
- Existing ImageSharp NU1902/NU1903 advisory warnings remain unchanged.

## Rollback

Previous image retained on the host:
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261108T073412Z-41c9c7f7cfcb`.
Previous digest:
`ghcr.io/qingcharles/wikiwikiworld@sha256:4f20906df709d8fe70212ad9dd48307e5b234b5a3f3d6141a1c96650bd5a28eb`.
Pull the digest successfully before passing it to `/home/deploy/update.sh`.

The previous application accepts the additive column. Leave `Notes` and any subsequently
written notes in place when rolling back application code. A database restore is not needed
for application rollback; restoring a backup would discard later writes and requires a
deliberate recovery decision.
