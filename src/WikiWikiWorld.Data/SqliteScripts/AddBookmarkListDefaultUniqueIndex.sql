-- Apply once to an existing database using a runner that stops on errors.
-- Enforces at most one active default bookmark list per user per site. Fails (and changes nothing)
-- if duplicates already exist; find them with the query below and resolve them first.
--   SELECT SiteId, CreatedByUserId, COUNT(*) FROM BookmarkLists
--   WHERE IsDefault = 1 AND DateDeleted IS NULL GROUP BY SiteId, CreatedByUserId HAVING COUNT(*) > 1;
BEGIN IMMEDIATE;

CREATE UNIQUE INDEX UX_BookmarkLists_SiteId_CreatedByUserId_Default_Active
    ON BookmarkLists (SiteId, CreatedByUserId) WHERE IsDefault = 1 AND DateDeleted IS NULL;

COMMIT;
