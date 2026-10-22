# Bookmark lists

Each user can have several bookmark lists per site. The article page shows signed-in users a split button:
the leading half toggles the default list ("Bookmarks", created on first use), and the chevron opens a menu
with a checkable row per list. The chevron's badge counts the non-default lists that hold the article.
"New list…" is a placeholder and does nothing yet; anonymous visitors keep the single button.

Clicks post with Datastar and carry the state the user asked for (`Saved=true|false`, the opposite of what
the page showed), not a toggle. Repeated or concurrent clicks with the same intent (a double click, a
second tab, mirrored Polypane panes) therefore settle on that state; a concurrent duplicate insert hits the
unique index and is treated as already saved. The server re-reads the user's lists and returns
`_BookmarkState`, whose top-level elements (button, chevron, list rows, status) are patched by id. The
surrounding `<details>` is not in the response, so an open menu stays open. Non-default lists are named by
id; the server checks the list belongs to the current user and site before changing anything.

Not done yet: creating, renaming, deleting and reordering lists, and grouping by list on
`/@user/bookmarks` (which currently lists every bookmark, so an article in two lists appears twice).

## Schema: one default list per user per site

`UX_BookmarkLists_SiteId_CreatedByUserId_Default_Active` is a partial unique index on
`BookmarkLists (SiteId, CreatedByUserId) WHERE IsDefault = 1 AND DateDeleted IS NULL`. It stops two
simultaneous first bookmarks from creating two default lists; the losing request catches the violation
and uses the winner's list.

The database operator applies
`src/WikiWikiWorld.Data/SqliteScripts/AddBookmarkListDefaultUniqueIndex.sql` once to an existing
database, after a consistent backup, using a runner that stops on errors:

```text
sqlite3 -bail /path/to/app.db ".read /path/to/AddBookmarkListDefaultUniqueIndex.sql"
```

Run the duplicate check in the script's header first; the index cannot be created while duplicates
exist. Verify with `.indexes BookmarkLists`. Fresh databases get the index from `Create.sql`; do not
run the upgrade script afterward. Rolling back the code can leave the index in place.

Applied: development `data/app.db` on 2026-10-22. Production: not yet applied.
