-- Migration Script: Add CopyrightStatus, DownloadUrlStatus, DownloadUrlNotes tables
-- and update DownloadUrls with new columns
-- Run this script against your existing database to apply schema changes

-- Start transaction
BEGIN TRANSACTION;

-- Create new CopyrightStatus table
CREATE TABLE CopyrightStatus (
    Id INTEGER NOT NULL CONSTRAINT PK_CopyrightStatus PRIMARY KEY AUTOINCREMENT,
    Status TEXT NOT NULL
);

-- Insert default copyright status values
INSERT INTO CopyrightStatus (Status) VALUES ('COPYRIGHTHOLDER');
INSERT INTO CopyrightStatus (Status) VALUES ('SHAREPERMITTED');
INSERT INTO CopyrightStatus (Status) VALUES ('PUBLICDOMAIN');
INSERT INTO CopyrightStatus (Status) VALUES ('UNKNOWN');

-- Create new DownloadUrlStatus table
CREATE TABLE DownloadUrlStatus (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrlStatus PRIMARY KEY AUTOINCREMENT,
    Status TEXT NOT NULL
);

-- Insert default download URL status values
INSERT INTO DownloadUrlStatus (Status) VALUES ('RECEIVED');
INSERT INTO DownloadUrlStatus (Status) VALUES ('VERIFIED');
INSERT INTO DownloadUrlStatus (Status) VALUES ('REJECTED');
INSERT INTO DownloadUrlStatus (Status) VALUES ('DEPLOYED');
INSERT INTO DownloadUrlStatus (Status) VALUES ('DEPLOYERROR');
INSERT INTO DownloadUrlStatus (Status) VALUES ('UNKNOWN');

-- Create new DownloadUrlNotes table
CREATE TABLE DownloadUrlNotes (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrlNotes PRIMARY KEY AUTOINCREMENT,
    DownloadUrlId INTEGER NOT NULL,
    UserId TEXT NOT NULL,
    Culture TEXT NOT NULL,
    Text TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    CONSTRAINT FK_DownloadUrlNotes_DownloadUrls_DownloadUrlId FOREIGN KEY (DownloadUrlId) REFERENCES DownloadUrls (Id) ON DELETE CASCADE,
    CONSTRAINT FK_DownloadUrlNotes_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

CREATE INDEX IX_DownloadUrlNotes_DownloadUrlId ON DownloadUrlNotes (DownloadUrlId);

-- SQLite requires recreating table to rename columns and add new ones
-- Step 1: Rename old table
ALTER TABLE DownloadUrls RENAME TO DownloadUrls_old;

-- Step 2: Create new table with updated schema
CREATE TABLE DownloadUrls (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrls PRIMARY KEY AUTOINCREMENT,
    SiteId INTEGER NOT NULL,
    HashSha256 TEXT NOT NULL,
    Filename TEXT NOT NULL,
    OriginalFilename TEXT NOT NULL,
    MimeType TEXT NOT NULL,
    FileSizeBytes INTEGER NOT NULL,
    DownloadUrls TEXT NULL,
    Quality INTEGER NULL,
    NeedsOcr INTEGER NULL,
    IsComplete INTEGER NULL,
    Description TEXT NULL,
    FilenameChanged INTEGER NOT NULL,
    NeedsRedeployment INTEGER NOT NULL,
    CopyrightStatusId INTEGER NULL,
    DownloadUrlStatusId INTEGER NULL,
    UploadedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateModified TEXT NULL,
    DateDeleted TEXT NULL
);

-- Step 3: Copy data from old table to new table
-- OriginalFilename is defaulted to Filename for existing rows
-- FilenameChanged and NeedsRedeployment default to 0 (false)
-- CopyrightStatusId is set to UNKNOWN for existing records
-- DownloadUrlStatusId is set to UNKNOWN for existing records
-- CreatedByUserId is renamed to UploadedByUserId
INSERT INTO DownloadUrls (
    Id, SiteId, HashSha256, Filename, OriginalFilename, MimeType, FileSizeBytes,
    DownloadUrls, Quality, NeedsOcr, IsComplete, Description,
    FilenameChanged, NeedsRedeployment, CopyrightStatusId, DownloadUrlStatusId,
    UploadedByUserId, DateCreated, DateModified, DateDeleted
)
SELECT
    Id, SiteId, HashSha256, Filename, Filename, MimeType, FileSizeBytes,
    DownloadUrls, Quality, NeedsOcr, IsComplete, NULL,
    0, 0,
    (SELECT Id FROM CopyrightStatus WHERE Status = 'UNKNOWN'),
    (SELECT Id FROM DownloadUrlStatus WHERE Status = 'UNKNOWN'),
    CreatedByUserId, DateCreated, DateModified, DateDeleted
FROM DownloadUrls_old;

-- Step 4: Drop old table
DROP TABLE DownloadUrls_old;

-- Step 5: Recreate index
CREATE INDEX IX_DownloadUrls_SiteId_HashSha256 ON DownloadUrls (SiteId, HashSha256);

-- Commit transaction
COMMIT;
