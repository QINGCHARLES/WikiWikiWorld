-- Apply once through the maintenance runner with the companion precheck/verification.
-- Existing memberships, list descriptions and indexes remain unchanged.
BEGIN IMMEDIATE;
ALTER TABLE Bookmarks ADD COLUMN Notes TEXT NULL;
COMMIT;
