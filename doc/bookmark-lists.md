# Bookmark lists

Each user has one permanent default list per site, lazily created when needed, and up to 99 additional
lists. Lists are private by default. The article split button saves to the default list; its menu
lets an article belong to several lists. "New list" in that menu creates a private list holding the article,
or reuses a same-named list. Names are trimmed, 1–60 characters, and compared ignoring case.

## Bookmark notes (backend)

Each `Bookmarks` membership has optional `Notes`: original Markdown explaining why that article
was saved in that particular list. The same article can have different notes in different lists.
The limit is 10,000 UTF-16 code units (`string.Length`). Null, empty or whitespace-only input
clears a note; all other source, including meaningful Markdown whitespace, is preserved verbatim.

- `SetBookmarkNotesAsync` accepts the bookmark membership ID and authenticated owner. It requires
  an active membership and an active owned parent list in the current site; unavailable targets
  return false without creating anything. Oversized input throws `ArgumentOutOfRangeException`.
  The tracked edit keeps subsequent membership operations in that unit of work coherent.
- `BookmarkEntry.Notes` is projected by `GetListPageAsync`, after the existing visibility,
  site/culture, current-article, sorting and paging rules. Private notes never enter visitor
  results; public list readers can read the notes. Note edits preserve the original save date,
  list description, counts and ordering.
- Render authorized source using `IMarkdownPipelineFactory.RenderUserContentMarkdown`, the same
  pipeline as list descriptions. Links and emphasis work; raw HTML is disabled, unsafe URLs
  remain inert and wiki directives are literal. Generated HTML is not stored. Future UI follows
  the existing authored-content/Datastar boundaries.
- Saving an already-present article or reusing a named list leaves its notes intact. Removal,
  default-list clearing and custom-list deletion retain notes in the soft-deleted rows. Undo
  restores the original note and save date. Re-adding after removal creates a new membership
  with a new save date that copies the notes of the most recently removed membership of that
  article in that list. Its notes may be null if they were cleared before removal. This
  protects notes from an accidental uncheck in the article menu, which has no undo. It also
  applies after Clear list, because removal and clearing store the same soft-deleted state.
  Undo of the older membership does not replace an already re-added membership's note.

The original change was backend only. The editing and reading UI is described in
[Notes and introductions UI](#notes-and-introductions-ui).

### Bookmark note schema upgrade

Existing databases require `src/WikiWikiWorld.Data/SqliteScripts/AddBookmarkNotes.sql` before
starting updated code. The script adds nullable, default-free `Notes TEXT` in one `BEGIN IMMEDIATE`
transaction. Old memberships start with null notes; list descriptions, existing values, identity
history and indexes stay intact. Fresh databases use `Create.sql` and skip the additive script.

Stage the migration and both companion check files beside the standard maintenance runner:

```bash
/home/deploy/db-maintenance/db-update.sh production /opt/wiki/data/app.db \
  /home/deploy/db-maintenance/AddBookmarkNotes.sql \
  /home/deploy/db-maintenance/VerifyBookmarkNotes.sql \
  /home/deploy/db-maintenance/CheckBookmarkNotesBefore.sql
```

Use the [database maintenance procedure](database-maintenance.md) for consistent backups,
preconditions and verification. The precheck requires the current list-description schema and
no existing bookmark `Notes` column. Verification requires the expected nullable `TEXT` column
and valid foreign keys. Both return a single integer 1. Rehearse on disposable backups; compare
all retained data and identity history with each maintenance backup after applying the change.
The old app accepts the additive schema, so apply it before deployment. Application rollback can
leave the column and notes in place; a backup restore would discard later writes and is a separate
recovery decision. Deployment records below or in the project reference record actual application.

Verification includes `BookmarkManagementTests` for note ownership, visibility, exact source
round trips, length/blank validation, cancellation, independent memberships, removal/undo/re-add,
shared Markdown rendering, and populated/fresh SQLite schema preservation.

The note upgrade was applied to development and production on 2026-11-08 after rehearsals on
consistent disposable backups. Both schemas match each other and `Create.sql`; all retained
data, including list descriptions and identity counters, was preserved. The updated Linux
image and Windows development app are running. See [the bookmark-note release record and
backup locations](release-bookmark-notes-2026-11-08.md).

## List descriptions (backend)

Each list has an optional `Description` containing the original Markdown introduction, shared
across its language views. The limit is 10,000 UTF-16 code units (`string.Length`). Null, empty
or whitespace-only input becomes null; other source, including meaningful whitespace, is
preserved verbatim. HTML is generated on demand, never stored.

- `CreateEmptyListAsync` has an overload accepting a description; existing callers still create
  lists without one. Invalid descriptions are rejected before any list, including the default,
  is created. Duplicate names never replace existing metadata.
- `SetListDescriptionAsync` updates only an active owned list in the current site and its UTC
  `DateModified`. Oversized source throws `ArgumentOutOfRangeException`. List ID zero selects
  the default list and creates it privately on demand, without adding an article. The set-based
  update bypasses tracked list instances; query again for current values.
- `GetListDetailsAsync` projects the selected list's metadata and Markdown source independently
  of article availability or count. Site/soft-delete filters and owner/public visibility apply.
  `IsOwner` must come from authenticated ownership checks, never request input. Owners may read
  an unsaved default placeholder with ID zero without creating database state. Other missing,
  deleted or inaccessible lists return null. The selector's `BookmarkListSummary` stays lean
  and does not load descriptions for every list.
- Render an authorized description using `IMarkdownPipelineFactory.RenderUserContentMarkdown`.
  This provides headings, paragraphs, links, emphasis and the shared short-content Markdown
  features. Raw HTML is disabled, unsafe destinations are restricted and wiki directives remain
  literal. Future UI rendering must retain the project's authored-HTML/Datastar boundaries.
- Descriptions follow list visibility: public list readers can read them; private descriptions
  are owner-only. Publishing includes the description. Making the list private restricts later
  reads. Bookmark saves, same-named-list reuse, removal/undo and clearing the default list leave
  descriptions intact. Custom-list deletion retains its description in the soft-deleted row.

The original change provided storage and service operations only. The editing and reading UI is
described in [Notes and introductions UI](#notes-and-introductions-ui). Individual bookmark notes
use the separate membership field described above.

### Description schema upgrade

Existing databases require `src/WikiWikiWorld.Data/SqliteScripts/AddBookmarkListDescription.sql`
before running an updated application: EF entity queries and inserts now include `Description`.
There are no startup migrations. The script adds nullable `TEXT` in one `BEGIN IMMEDIATE`
transaction, leaves existing values and indexes unchanged and gives old lists null descriptions.
Fresh databases use `Create.sql` and must not run the additive upgrade.

The database operator applies this once through the [standard maintenance command](database-maintenance.md),
which takes a consistent SQLite backup before changing the database:

```bash
/home/deploy/db-maintenance/db-update.sh production /opt/wiki/data/app.db \
  /home/deploy/db-maintenance/AddBookmarkListDescription.sql \
  /home/deploy/db-maintenance/VerifyBookmarkListDescription.sql \
  /home/deploy/db-maintenance/CheckBookmarkListDescriptionBefore.sql
```

Stage the three reviewed SQL files beside the runner first. The precheck requires an existing
visibility-aware list schema without `Description`; verification requires a nullable, default-free
`TEXT` column and valid foreign keys. Both checks return the runner's required single integer 1.
Compare existing list/membership values and counts with the maintenance backup, verify initial
null descriptions and run integrity/foreign-key checks. Test representative populated data before
live application. The old application remains compatible with the additive column, so schema
application can precede deployment. Roll back application code while retaining the column and
any descriptions; restoring a backup would discard later writes and needs a recovery decision.

The description upgrade was applied to development and production on 2026-11-08 through the
maintenance runner after rehearsals on consistent disposable backups. All retained values and
identity history were preserved; both schemas now match each other and fresh `Create.sql`.
See [backup locations, release verification and rollback](release-2026-11-08.md).

Verification:

```powershell
dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter "FullyQualifiedName~BookmarkListDescriptionTests|FullyQualifiedName~BookmarkManagementTests|FullyQualifiedName~MarkdownSafetyTests"
```

SQLite tests cover exact source round trips, validation, ownership/visibility/site/deletion,
empty and default lists, shared language views, timestamps, cancellation, membership lifecycle,
shared Markdown rendering, populated schema preservation and fresh/upgraded column parity.

Implementation verification on 2026-11-07: the Release build and all 384 .NET tests passed,
including the 11 new description cases, along with all 14 existing Python maintenance tests.
Existing ImageSharp NU1902/NU1903 advisory warnings remain unchanged. Build outputs were
isolated so the running Windows development process could remain active.

## Notes and introductions UI

- **Article page.** In the bookmark menu, each list holding the article gets a note button, and a
  one-line plain-text excerpt of any note under the list name. `GetListMembershipsAsync` returns
  each membership's ID, note source and list visibility for this; it is only shown to the owner.
  The note button opens one `#article-note-dialog` per page, kept outside `#article-bookmark` so
  toggle responses (which patch the menu by id) never replace it. `js/bookmark-notes.js` fills the
  dialog from the button's data attributes. Save posts `?handler=SetBookmarkNote` through Datastar
  with the form's antiforgery token. The `_BookmarkState` response refreshes the menu, confirms in
  the status line and closes the dialog. Ctrl+Enter saves, and Delete note saves a blank note.
- **No prompt on save.** Saving never opens the dialog. `#article-bookmark-prompt` says
  "Saved to List" with an "Add a note" button, or that an earlier note is back after a re-save.
  It clears on the next click elsewhere and gives way to any status message.
- **Profile, owner.** The list introduction renders under the list header, with an empty-state
  invitation when there is none. It is edited in `#bookmark-intro-dialog` with Write/Preview tabs.
  Preview posts the draft to `?handler=Preview`, which returns `RenderUserContentMarkdown` output
  without storing anything. Each entry shows its rendered note. Add note / Edit note reveals an
  inline editor in the row: a plain POST (`operation=notes`) that redirects back to `#bookmark-{id}`.
  Notes longer than 240 characters or three lines start clamped to three lines with Read more.
- **Publish confirmation.** Make public… now counts the list's notes and mentions its introduction.
  Every editor states whether its text is public or private.
- **Profile, visitor.** Public lists show "A list by {user}" in place of the visibility pill, then
  the introduction and each entry's note, with no edit controls.
- **Markup constraint.** `HtmlPrettifyMiddleware` removes empty `<p>` elements and empty `data-*`
  attributes from full pages. JavaScript hooks therefore carry values (`data-bookmark-note="true"`),
  and initially empty live regions use `<div>`.

## Profile page

- `/@username/bookmarks` shows the owner's default list. Visitors see the first public list, or a neutral
  empty state when there are none. `/@username/bookmarks/{listid}` selects a stable list identity.
- A single default list needs no selector. Multiple visible lists appear in a native disclosure with
  names, counts, and privacy labels; default first, others alphabetically. Visitors never receive private
  list names or counts. Private/deleted/foreign-owner list IDs return 404.
- `?sort=name&pagenumber=3` describes the view. Parameters are lowercase. Default sort is recently saved;
  default page is 1. Fifty entries per page means 400 entries occupy eight pages. Numbered links include
  previous/next, first/last, and nearby pages.
- Name sorting uses the connection-local `BOOKMARK_TITLE` SQLite collation, implemented with .NET invariant
  Unicode comparison ignoring case (not SQLite's ASCII-only NOCASE). Equal titles sort by membership ID.
  Recent saves sort by stored UTC timestamp descending and membership ID descending. The existing timestamp
  mapping is retained; application writes must remain UTC.
- Visibility/current-article/site/culture filtering, sorting, counting and paging occur in SQL.
  Queries project titles/slugs/types/save times, never full article bodies. Unavailable current articles
  are omitted before counting. Selector counts cover the current language; publish/delete confirmations
  count all active memberships across site languages.
- `bookmarks.js` imports the pinned self-hosted Datastar 1.0.4. Reads patch `_BookmarkBrowser` by ID;
  a matching server navigation identifier is required before committing a history URL. A newer request
  aborts an older one. Back/Forward reload the corresponding fragment. Network failures preserve the
  prior view; failed history navigation performs a normal reload rather than showing stale content.
- Pagination and list selection are real links, and sorting is a GET form. They work without JavaScript
  and in new tabs. Nonempty enhancement attributes and a div live region survive HTML prettification.
- Mutations use ordinary antiforgery-protected POST forms, authenticated ownership checks, and redirects.
  New-list duplicate names show validation errors. Creating an empty list does not need an article.
- Publishing an existing private list requires confirmation. Switching back to private takes effect on
  subsequent requests; it does not retract copies someone already made while it was public.
- Removing an entry affects only that list. Undo uses a five-minute data-protected token bound to the owner,
  list, membership, and exact deletion timestamp, retaining the original save date. Re-adding the same
  article before undo does not create a duplicate. A subsequent clear invalidates earlier undo tokens.
- Custom-list deletion soft-deletes the list and every membership in one transaction. The default list
  instead offers Clear list. Both require confirmation and have no UI undo. Articles and other lists
  remain untouched. Article saves, deletion, and undo use a transaction executed through the configured
  SQLite retry strategy, so a concurrent save cannot revive a deleted list.
- Responses are no-store and vary by Datastar-Request. Public-list links appear in the profile tabs when
  at least one public list exists; the visitor's tab count includes only publicly readable memberships.
- Renaming and manual list ordering are not implemented.

## Database upgrade: visibility and recent-page index

Schema ownership remains manual SQL, not startup migrations or EnsureCreated.

Use the standard [database maintenance command](database-maintenance.md) on the
host to perform the backup, apply the SQL and run explicit verification assertions.
The steps below describe this migration's preconditions and expected results.

The database operator must apply `src/WikiWikiWorld.Data/SqliteScripts/AddBookmarkListVisibility.sql`
once to an existing database before deploying the code that reads IsPublic:

1. Take a consistent SQLite backup using the backup API or sqlite3's `.backup`, not a live file copy.
2. Inspect `PRAGMA table_info(BookmarkLists);`. If IsPublic already exists, do not rerun the ALTER;
   verify its definition and the recent-page index instead.
3. Run with error stopping:
   `sqlite3 -bail /path/to/app.db ".read /path/to/AddBookmarkListVisibility.sql"`.
4. Check `PRAGMA table_info(BookmarkLists);`, `PRAGMA index_list(Bookmarks);`, and `PRAGMA integrity_check;`.
   Verify existing names/memberships are preserved and old lists have IsPublic=0.
5. Start the updated app and check owner and anonymous views.

The script uses one transaction, adds IsPublic with a private default, and creates
`IX_Bookmarks_List_Recent_Active (BookmarkListId, DateCreated DESC, Id DESC) WHERE DateDeleted IS NULL`.
Fresh databases use Create.sql and must not run the upgrade script. EF now also describes the existing
active-membership unique index, making disposable-provider tests enforce production's constraint.

Rollback may leave the additive column/index in place. Roll back application code rather than dropping
state. Restoring a backup discards later writes and requires a deliberate recovery decision.
Production upgrade/deployment is not performed by local development testing.

## Existing constraint: one default list per user per site

`UX_BookmarkLists_SiteId_CreatedByUserId_Default_Active` is a partial unique index on
`BookmarkLists (SiteId, CreatedByUserId) WHERE IsDefault = 1 AND DateDeleted IS NULL`.
`UX_Bookmarks_BookmarkListId_CanonicalArticleId_Active` prevents duplicate active memberships.
Creation paths handle concurrent constraint conflicts as already-created/already-saved state.

The default-list index upgrade is `src/WikiWikiWorld.Data/SqliteScripts/AddBookmarkListDefaultUniqueIndex.sql`.
Run its duplicate preflight first; fresh databases already have it.
Previously applied: development `data/app.db` and production `/opt/wiki/data/app.db` on 2026-10-22
(release d9a69c4). That historical record does not imply the new visibility upgrade is applied.

## Verification

`dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter "FullyQualifiedName~BookmarkManagementTests|FullyQualifiedName~ProfileTests"`

The SQLite tests cover 400-entry paging, clamping, Unicode ordering, visibility/site/culture filtering,
empty creation and limits, ownership rejection, per-list removal, undo, clearing/deletion, and the additive
upgrade. They use the application's SQLite retry strategy for transactional workflows.
Browser checks cover no-reload Datastar paging, URL/history state, network failures, responsive layouts,
publishing/re-privatizing, no-JavaScript forms and pagination, and undo.

Visibility upgrade applied to development `data/app.db` on 2026-10-24 after a consistent backup at
`.local/before-bookmark-visibility-20261024T151339Z.db`. All five existing lists and eleven memberships
were preserved, all lists remained private, and SQLite integrity_check passed.

Production `/opt/wiki/data/app.db` was upgraded on 2026-10-24 before deploying release 4e8b83c, after a
consistent backup at `/home/deploy/backups/app-before-4e8b83c-20261024T165250Z.db`. The one list and three
memberships were preserved and stayed private; integrity_check passed. `AddArticleCultureLinkIndexes.sql` was
applied in the same window. `deploy` cannot write the database (owned by UID 1654), so the scripts ran through a
throwaway `python:3.14-slim` container as `--user 1654:1654` with no network. The previous application image is
tagged `rollback-before-4e8b83c` on the host.
