# Bookmark lists

Each user has one permanent default list per site, lazily created when needed, and up to 99 additional
lists. Lists are private by default. The article split button saves to the default list; its menu
lets an article belong to several lists. "New list" in that menu creates a private list holding the article,
or reuses a same-named list. Names are trimmed, 1–60 characters, and compared ignoring case.

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
