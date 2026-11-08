-- Apply once through the database maintenance runner with the companion checks.
-- Existing rows retain every value and start without an introduction.
BEGIN IMMEDIATE;
ALTER TABLE BookmarkLists ADD COLUMN Description TEXT NULL;
COMMIT;
