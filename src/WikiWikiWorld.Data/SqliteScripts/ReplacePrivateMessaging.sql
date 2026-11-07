-- Incompatible cutover: stop the old application and other writers first.
-- Inventory found no legacy discussions in either environment. Abort rather than
-- guessing which new rows might be private if that precondition has changed.
BEGIN IMMEDIATE;

CREATE TEMP TABLE PrivateMessagingCutoverGuard (Safe INTEGER NOT NULL CHECK (Safe = 1));
INSERT INTO PrivateMessagingCutoverGuard
SELECT NOT EXISTS (SELECT 1 FROM ArticleTalkSubjects)
   AND NOT EXISTS (SELECT 1 FROM ArticleTalkSubjectPosts);

CREATE TABLE PrivateConversations (
    Id TEXT NOT NULL CONSTRAINT PK_PrivateConversations PRIMARY KEY,
    SiteId INTEGER NOT NULL,
    Culture TEXT NOT NULL,
    Subject TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    CONSTRAINT FK_PrivateConversations_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE RESTRICT
) STRICT;

CREATE INDEX IX_PrivateConversations_CreatedByUserId ON PrivateConversations (CreatedByUserId);

CREATE TABLE PrivateConversationParticipants (
    ConversationId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    CONSTRAINT PK_PrivateConversationParticipants PRIMARY KEY (ConversationId, UserId),
    CONSTRAINT FK_PrivateConversationParticipants_PrivateConversations_ConversationId FOREIGN KEY (ConversationId) REFERENCES PrivateConversations (Id) ON DELETE CASCADE,
    CONSTRAINT FK_PrivateConversationParticipants_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE RESTRICT
) STRICT;

CREATE INDEX IX_PrivateConversationParticipants_UserId_ConversationId ON PrivateConversationParticipants (UserId, ConversationId);

CREATE TABLE PrivateMessages (
    Id INTEGER NOT NULL CONSTRAINT PK_PrivateMessages PRIMARY KEY AUTOINCREMENT,
    ConversationId TEXT NOT NULL,
    AuthorUserId TEXT NOT NULL,
    Text TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    CONSTRAINT FK_PrivateMessages_PrivateConversations_ConversationId FOREIGN KEY (ConversationId) REFERENCES PrivateConversations (Id) ON DELETE CASCADE,
    CONSTRAINT FK_PrivateMessages_PrivateConversationParticipants_ConversationId_AuthorUserId FOREIGN KEY (ConversationId, AuthorUserId) REFERENCES PrivateConversationParticipants (ConversationId, UserId) ON DELETE RESTRICT
) STRICT;

CREATE INDEX IX_PrivateMessages_ConversationId_Id ON PrivateMessages (ConversationId, Id);
CREATE INDEX IX_PrivateMessages_ConversationId_AuthorUserId ON PrivateMessages (ConversationId, AuthorUserId);

ALTER TABLE ArticleTalkSubjects DROP COLUMN HasBeenEdited;
ALTER TABLE ArticleTalkSubjects DROP COLUMN DateModified;
DROP TABLE PrivateMessagingCutoverGuard;
COMMIT;
