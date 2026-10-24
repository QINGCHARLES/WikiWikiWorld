-- Apply once to an existing database, after a consistent backup, with sqlite3 -bail.
-- Check PRAGMA table_info(BookmarkLists) first: IsPublic must not exist yet.
-- No existing list becomes public. Fresh Create.sql databases already include this change.
BEGIN IMMEDIATE;
ALTER TABLE BookmarkLists ADD COLUMN IsPublic INTEGER NOT NULL DEFAULT 0 CHECK (IsPublic IN (0, 1));
CREATE INDEX IX_Bookmarks_List_Recent_Active ON Bookmarks (BookmarkListId, DateCreated DESC, Id DESC) WHERE DateDeleted IS NULL;
COMMIT;
