-- Apply once through the maintenance runner with the companion precheck and verification.
-- Rebuild only the child table; keep foreign keys enabled and preserve rows and identity history.
BEGIN IMMEDIATE;

CREATE TEMP TABLE DownloadUrlNotesRepairState (
    PreviousSequence INTEGER NULL,
    Assertion INTEGER NOT NULL CHECK (Assertion = 1)
);

INSERT INTO DownloadUrlNotesRepairState (PreviousSequence, Assertion)
SELECT (SELECT seq FROM sqlite_sequence WHERE name = 'DownloadUrlNotes'),
    EXISTS (
        SELECT 1 FROM pragma_foreign_key_list('DownloadUrlNotes')
        WHERE "table" = 'DownloadUrls_old' AND "from" = 'DownloadUrlId' AND "to" = 'Id'
    );

CREATE TABLE DownloadUrlNotes_Repaired (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrlNotes PRIMARY KEY AUTOINCREMENT,
    DownloadUrlId INTEGER NOT NULL,
    UserId TEXT NOT NULL,
    Culture TEXT NOT NULL,
    Text TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    CONSTRAINT FK_DownloadUrlNotes_DownloadUrls_DownloadUrlId FOREIGN KEY (DownloadUrlId) REFERENCES DownloadUrls (Id) ON DELETE CASCADE,
    CONSTRAINT FK_DownloadUrlNotes_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

INSERT INTO DownloadUrlNotes_Repaired (Id, DownloadUrlId, UserId, Culture, Text, DateCreated)
SELECT Id, DownloadUrlId, UserId, Culture, Text, DateCreated FROM DownloadUrlNotes;

UPDATE DownloadUrlNotesRepairState SET Assertion = NOT EXISTS (
    SELECT Id, DownloadUrlId, UserId, Culture, Text, DateCreated FROM DownloadUrlNotes
    EXCEPT
    SELECT Id, DownloadUrlId, UserId, Culture, Text, DateCreated FROM DownloadUrlNotes_Repaired
) AND NOT EXISTS (
    SELECT Id, DownloadUrlId, UserId, Culture, Text, DateCreated FROM DownloadUrlNotes_Repaired
    EXCEPT
    SELECT Id, DownloadUrlId, UserId, Culture, Text, DateCreated FROM DownloadUrlNotes
);

DROP TABLE DownloadUrlNotes;
ALTER TABLE DownloadUrlNotes_Repaired RENAME TO DownloadUrlNotes;
CREATE INDEX IX_DownloadUrlNotes_DownloadUrlId ON DownloadUrlNotes (DownloadUrlId);

-- Retain the highest previously issued ID, including IDs whose notes were deleted.
DELETE FROM sqlite_sequence WHERE name = 'DownloadUrlNotes';
INSERT INTO sqlite_sequence (name, seq)
SELECT 'DownloadUrlNotes', PreviousSequence FROM DownloadUrlNotesRepairState
WHERE PreviousSequence IS NOT NULL;

DROP TABLE DownloadUrlNotesRepairState;
COMMIT;
