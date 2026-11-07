-- Apply once through deploy/db-update.sh after a consistent backup.
-- Existing revisions retain NULL: their legacy staging cannot be assigned safely from filenames alone.
BEGIN IMMEDIATE;
ALTER TABLE FileRevisions ADD COLUMN StagingUploadId TEXT NULL;
CREATE UNIQUE INDEX UX_FileRevisions_StagingUploadId ON FileRevisions (StagingUploadId) WHERE StagingUploadId IS NOT NULL;
COMMIT;
