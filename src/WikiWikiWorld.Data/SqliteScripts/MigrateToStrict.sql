PRAGMA foreign_keys = OFF;
BEGIN TRANSACTION;

-- ==========================================
-- 1. Rename existing tables to temp tables
-- ==========================================
ALTER TABLE Users RENAME TO Users_Old;
ALTER TABLE Roles RENAME TO Roles_Old;
ALTER TABLE UserRoles RENAME TO UserRoles_Old;
ALTER TABLE UserClaims RENAME TO UserClaims_Old;
ALTER TABLE UserLogins RENAME TO UserLogins_Old;
ALTER TABLE RoleClaims RENAME TO RoleClaims_Old;
ALTER TABLE UserTokens RENAME TO UserTokens_Old;
ALTER TABLE ArticleRevisions RENAME TO ArticleRevisions_Old;
ALTER TABLE FileRevisions RENAME TO FileRevisions_Old;
ALTER TABLE ArticleCultureLinks RENAME TO ArticleCultureLinks_Old;
ALTER TABLE CopyrightStatus RENAME TO CopyrightStatus_Old;
ALTER TABLE DownloadUrlStatus RENAME TO DownloadUrlStatus_Old;
ALTER TABLE DownloadUrls RENAME TO DownloadUrls_Old;
ALTER TABLE DownloadUrlNotes RENAME TO DownloadUrlNotes_Old;
ALTER TABLE ArticleTalkSubjects RENAME TO ArticleTalkSubjects_Old;
ALTER TABLE ArticleTalkSubjectPosts RENAME TO ArticleTalkSubjectPosts_Old;
ALTER TABLE BookmarkLists RENAME TO BookmarkLists_Old;
ALTER TABLE Bookmarks RENAME TO Bookmarks_Old;
ALTER TABLE PublicationUploadCopyrightExemptions RENAME TO PublicationUploadCopyrightExemptions_Old;
ALTER TABLE PublicationUploadQueueStatuses RENAME TO PublicationUploadQueueStatuses_Old;
ALTER TABLE PublicationUploadQueue RENAME TO PublicationUploadQueue_Old;

-- ==========================================
-- 2. Recreate tables with STRICT keyword
-- ==========================================
CREATE TABLE Users (
    Id TEXT NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    UserName TEXT NULL,
    NormalizedUserName TEXT NULL,
    Email TEXT NULL,
    NormalizedEmail TEXT NULL,
    EmailConfirmed INTEGER NOT NULL,
    PasswordHash TEXT NULL,
    SecurityStamp TEXT NULL,
    ConcurrencyStamp TEXT NULL,
    PhoneNumber TEXT NULL,
    PhoneNumberConfirmed INTEGER NOT NULL DEFAULT 0,
    TwoFactorEnabled INTEGER NOT NULL,
    LockoutEnd TEXT NULL,
    LockoutEnabled INTEGER NOT NULL,
    AccessFailedCount INTEGER NOT NULL,
    ProfilePicGuid TEXT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL
) STRICT;

CREATE TABLE Roles (
    Id TEXT NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name TEXT NULL,
    NormalizedName TEXT NULL,
    ConcurrencyStamp TEXT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL
) STRICT;

CREATE TABLE UserRoles (
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES Roles (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE UserClaims (
    Id INTEGER NOT NULL CONSTRAINT PK_UserClaims PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL,
    ClaimType TEXT NULL,
    ClaimValue TEXT NULL,
    CONSTRAINT FK_UserClaims_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE UserLogins (
    LoginProvider TEXT NOT NULL,
    ProviderKey TEXT NOT NULL,
    ProviderDisplayName TEXT NULL,
    UserId TEXT NOT NULL,
    CONSTRAINT PK_UserLogins PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_UserLogins_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE RoleClaims (
    Id INTEGER NOT NULL CONSTRAINT PK_RoleClaims PRIMARY KEY AUTOINCREMENT,
    RoleId TEXT NOT NULL,
    ClaimType TEXT NULL,
    ClaimValue TEXT NULL,
    CONSTRAINT FK_RoleClaims_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES Roles (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE UserTokens (
    UserId TEXT NOT NULL,
    LoginProvider TEXT NOT NULL,
    Name TEXT NOT NULL,
    Value TEXT NULL,
    CONSTRAINT PK_UserTokens PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_UserTokens_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE ArticleRevisions (
    Id INTEGER NOT NULL CONSTRAINT PK_ArticleRevisions PRIMARY KEY AUTOINCREMENT,
    CanonicalArticleId TEXT NOT NULL,
    SiteId INTEGER NOT NULL,
    Culture TEXT NOT NULL,
    Title TEXT NOT NULL,
    DisplayTitle TEXT NULL,
    UrlSlug TEXT NOT NULL,
    IsCurrent INTEGER NOT NULL,
    Type TEXT NOT NULL,
    Text TEXT NOT NULL,
    CanonicalFileId TEXT NULL,
    RevisionReason TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_ArticleRevisions_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE FileRevisions (
    Id INTEGER NOT NULL CONSTRAINT PK_FileRevisions PRIMARY KEY AUTOINCREMENT,
    CanonicalFileId TEXT NOT NULL,
    IsCurrent INTEGER NOT NULL,
    Type TEXT NOT NULL,
    Filename TEXT NOT NULL,
    MimeType TEXT NOT NULL,
    FileSizeBytes INTEGER NOT NULL,
    ImageWidthPixels INTEGER NULL,
    ImageHeightPixels INTEGER NULL,
    Source TEXT NULL,
    RevisionReason TEXT NOT NULL,
    SourceAndRevisionReasonCulture TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_FileRevisions_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE ArticleCultureLinks (
    Id INTEGER NOT NULL CONSTRAINT PK_ArticleCultureLinks PRIMARY KEY AUTOINCREMENT,
    SiteId INTEGER NOT NULL,
    CanonicalArticleId TEXT NOT NULL,
    ArticleCultureLinkGroupId TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DeletedByUserId TEXT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_ArticleCultureLinks_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE,
    CONSTRAINT FK_ArticleCultureLinks_Users_DeletedByUserId FOREIGN KEY (DeletedByUserId) REFERENCES Users (Id)
) STRICT;

CREATE TABLE CopyrightStatus (
    Id INTEGER NOT NULL CONSTRAINT PK_CopyrightStatus PRIMARY KEY AUTOINCREMENT,
    Status TEXT NOT NULL
) STRICT;

CREATE TABLE DownloadUrlStatus (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrlStatus PRIMARY KEY AUTOINCREMENT,
    Status TEXT NOT NULL
) STRICT;

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
) STRICT;

CREATE TABLE DownloadUrlNotes (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrlNotes PRIMARY KEY AUTOINCREMENT,
    DownloadUrlId INTEGER NOT NULL,
    UserId TEXT NOT NULL,
    Culture TEXT NOT NULL,
    Text TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    CONSTRAINT FK_DownloadUrlNotes_DownloadUrls_DownloadUrlId FOREIGN KEY (DownloadUrlId) REFERENCES DownloadUrls (Id) ON DELETE CASCADE,
    CONSTRAINT FK_DownloadUrlNotes_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE ArticleTalkSubjects (
    Id INTEGER NOT NULL CONSTRAINT PK_ArticleTalkSubjects PRIMARY KEY AUTOINCREMENT,
    SiteId INTEGER NOT NULL,
    CanonicalArticleId TEXT NOT NULL,
    Subject TEXT NOT NULL,
    UrlSlug TEXT NOT NULL,
    HasBeenEdited INTEGER NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateModified TEXT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_ArticleTalkSubjects_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE ArticleTalkSubjectPosts (
    Id INTEGER NOT NULL CONSTRAINT PK_ArticleTalkSubjectPosts PRIMARY KEY AUTOINCREMENT,
    ArticleTalkSubjectId INTEGER NOT NULL,
    ParentTalkSubjectPostId INTEGER NULL,
    Text TEXT NOT NULL,
    HasBeenEdited INTEGER NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateModified TEXT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_ArticleTalkSubjectPosts_ArticleTalkSubjects_ArticleTalkSubjectId FOREIGN KEY (ArticleTalkSubjectId) REFERENCES ArticleTalkSubjects (Id) ON DELETE CASCADE,
    CONSTRAINT FK_ArticleTalkSubjectPosts_ArticleTalkSubjectPosts_ParentTalkSubjectPostId FOREIGN KEY (ParentTalkSubjectPostId) REFERENCES ArticleTalkSubjectPosts (Id),
    CONSTRAINT FK_ArticleTalkSubjectPosts_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE BookmarkLists (
    Id INTEGER NOT NULL CONSTRAINT PK_BookmarkLists PRIMARY KEY AUTOINCREMENT,
    SiteId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    IsDefault INTEGER NOT NULL DEFAULT 0,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateModified TEXT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_BookmarkLists_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE Bookmarks (
    Id INTEGER NOT NULL CONSTRAINT PK_Bookmarks PRIMARY KEY AUTOINCREMENT,
    BookmarkListId INTEGER NOT NULL,
    SiteId INTEGER NOT NULL,
    CanonicalArticleId TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_Bookmarks_BookmarkLists_BookmarkListId FOREIGN KEY (BookmarkListId) REFERENCES BookmarkLists (Id) ON DELETE CASCADE,
    CONSTRAINT FK_Bookmarks_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
) STRICT;

CREATE TABLE PublicationUploadCopyrightExemptions (
    Id INTEGER NOT NULL CONSTRAINT PK_PublicationUploadCopyrightExemptions PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL
) STRICT;

CREATE TABLE PublicationUploadQueueStatuses (
    Id INTEGER NOT NULL CONSTRAINT PK_PublicationUploadQueueStatuses PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL
) STRICT;

CREATE TABLE PublicationUploadQueue (
    Id INTEGER NOT NULL CONSTRAINT PK_PublicationUploadQueue PRIMARY KEY AUTOINCREMENT,
    SiteId INTEGER NOT NULL,
    Culture TEXT NOT NULL,
    CanonicalArticleId TEXT NULL,
    DownloadUrl TEXT NOT NULL,
    SitePassword TEXT NULL,
    ArchivePassword TEXT NULL,
    CopyrightExemptionId INTEGER NOT NULL,
    StatusId INTEGER NOT NULL,
    SubmitterNotes TEXT NULL,
    ReviewerNotes TEXT NULL,
    RejectionReason TEXT NULL,
    SubmittedByUserId TEXT NOT NULL,
    LastUpdatedByUserId TEXT NULL,
    DateCreated TEXT NOT NULL,
    DateStatusChanged TEXT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_PublicationUploadQueue_Users_SubmittedByUserId
        FOREIGN KEY (SubmittedByUserId) REFERENCES Users (Id) ON DELETE CASCADE,
    CONSTRAINT FK_PublicationUploadQueue_Users_LastUpdatedByUserId
        FOREIGN KEY (LastUpdatedByUserId) REFERENCES Users (Id),
    CONSTRAINT FK_PublicationUploadQueue_CopyrightExemption
        FOREIGN KEY (CopyrightExemptionId) REFERENCES PublicationUploadCopyrightExemptions (Id),
    CONSTRAINT FK_PublicationUploadQueue_Status
        FOREIGN KEY (StatusId) REFERENCES PublicationUploadQueueStatuses (Id)
) STRICT;

-- ==========================================
-- 3. Copy data from old tables to new tables
-- ==========================================
INSERT INTO Users SELECT * FROM Users_Old;
INSERT INTO Roles SELECT * FROM Roles_Old;
INSERT INTO UserRoles SELECT * FROM UserRoles_Old;
INSERT INTO UserClaims SELECT * FROM UserClaims_Old;
INSERT INTO UserLogins SELECT * FROM UserLogins_Old;
INSERT INTO RoleClaims SELECT * FROM RoleClaims_Old;
INSERT INTO UserTokens SELECT * FROM UserTokens_Old;
INSERT INTO ArticleRevisions SELECT * FROM ArticleRevisions_Old;
INSERT INTO FileRevisions SELECT * FROM FileRevisions_Old;
INSERT INTO ArticleCultureLinks SELECT * FROM ArticleCultureLinks_Old;
INSERT INTO CopyrightStatus SELECT * FROM CopyrightStatus_Old;
INSERT INTO DownloadUrlStatus SELECT * FROM DownloadUrlStatus_Old;
INSERT INTO DownloadUrls SELECT * FROM DownloadUrls_Old;
INSERT INTO DownloadUrlNotes SELECT * FROM DownloadUrlNotes_Old;
INSERT INTO ArticleTalkSubjects SELECT * FROM ArticleTalkSubjects_Old;
INSERT INTO ArticleTalkSubjectPosts SELECT * FROM ArticleTalkSubjectPosts_Old;
INSERT INTO BookmarkLists SELECT * FROM BookmarkLists_Old;
INSERT INTO Bookmarks SELECT * FROM Bookmarks_Old;
INSERT INTO PublicationUploadCopyrightExemptions SELECT * FROM PublicationUploadCopyrightExemptions_Old;
INSERT INTO PublicationUploadQueueStatuses SELECT * FROM PublicationUploadQueueStatuses_Old;
INSERT INTO PublicationUploadQueue SELECT * FROM PublicationUploadQueue_Old;

-- ==========================================
-- 4. Drop old tables
-- ==========================================
DROP TABLE Users_Old;
DROP TABLE Roles_Old;
DROP TABLE UserRoles_Old;
DROP TABLE UserClaims_Old;
DROP TABLE UserLogins_Old;
DROP TABLE RoleClaims_Old;
DROP TABLE UserTokens_Old;
DROP TABLE ArticleRevisions_Old;
DROP TABLE FileRevisions_Old;
DROP TABLE ArticleCultureLinks_Old;
DROP TABLE CopyrightStatus_Old;
DROP TABLE DownloadUrlStatus_Old;
DROP TABLE DownloadUrls_Old;
DROP TABLE DownloadUrlNotes_Old;
DROP TABLE ArticleTalkSubjects_Old;
DROP TABLE ArticleTalkSubjectPosts_Old;
DROP TABLE BookmarkLists_Old;
DROP TABLE Bookmarks_Old;
DROP TABLE PublicationUploadCopyrightExemptions_Old;
DROP TABLE PublicationUploadQueueStatuses_Old;
DROP TABLE PublicationUploadQueue_Old;

-- ==========================================
-- 5. Recreate all indexes
-- ==========================================
CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON Users (NormalizedUserName);
CREATE INDEX IX_Users_NormalizedEmail ON Users (NormalizedEmail);
CREATE UNIQUE INDEX IX_Roles_NormalizedName ON Roles (NormalizedName);
CREATE INDEX IX_UserRoles_RoleId ON UserRoles (RoleId);
CREATE INDEX IX_UserClaims_UserId ON UserClaims (UserId);
CREATE INDEX IX_UserLogins_UserId ON UserLogins (UserId);
CREATE INDEX IX_RoleClaims_RoleId ON RoleClaims (RoleId);
CREATE INDEX IX_ArticleRevisions_CreatedByUserId ON ArticleRevisions (CreatedByUserId);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_DateCreated ON ArticleRevisions (SiteId, Culture, UrlSlug, DateCreated);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_IsCurrent ON ArticleRevisions (SiteId, Culture, UrlSlug, IsCurrent);
CREATE INDEX IX_FileRevisions_CanonicalFileId_DateCreated ON FileRevisions (CanonicalFileId, DateCreated DESC);
CREATE INDEX IX_FileRevisions_CreatedByUserId ON FileRevisions (CreatedByUserId);
CREATE INDEX IX_ArticleCultureLinks_CreatedByUserId ON ArticleCultureLinks (CreatedByUserId);
CREATE INDEX IX_ArticleCultureLinks_DeletedByUserId ON ArticleCultureLinks (DeletedByUserId);
CREATE INDEX IX_ArticleCultureLinks_SiteId_CanonicalArticleId ON ArticleCultureLinks (SiteId, CanonicalArticleId);
CREATE INDEX IX_DownloadUrls_SiteId_HashSha256 ON DownloadUrls (SiteId, HashSha256);
CREATE INDEX IX_DownloadUrlNotes_DownloadUrlId ON DownloadUrlNotes (DownloadUrlId);
CREATE INDEX IX_ArticleTalkSubjects_CreatedByUserId ON ArticleTalkSubjects (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjects_SiteId_CanonicalArticleId ON ArticleTalkSubjects (SiteId, CanonicalArticleId);
CREATE INDEX IX_ArticleTalkSubjectPosts_ArticleTalkSubjectId ON ArticleTalkSubjectPosts (ArticleTalkSubjectId);
CREATE INDEX IX_ArticleTalkSubjectPosts_CreatedByUserId ON ArticleTalkSubjectPosts (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjectPosts_ParentTalkSubjectPostId ON ArticleTalkSubjectPosts (ParentTalkSubjectPostId);
CREATE INDEX IX_ArticleTalkSubjectPosts_SubjectId_ParentId_Active ON ArticleTalkSubjectPosts (ArticleTalkSubjectId, ParentTalkSubjectPostId);
CREATE INDEX IX_BookmarkLists_CreatedByUserId ON BookmarkLists (CreatedByUserId);
CREATE INDEX IX_BookmarkLists_SiteId_CreatedByUserId ON BookmarkLists (SiteId, CreatedByUserId);
CREATE INDEX IX_Bookmarks_BookmarkListId ON Bookmarks (BookmarkListId);
CREATE INDEX IX_Bookmarks_CreatedByUserId ON Bookmarks (CreatedByUserId);
CREATE INDEX IX_Bookmarks_SiteId_CanonicalArticleId_CreatedByUserId ON Bookmarks (SiteId, CanonicalArticleId, CreatedByUserId);
CREATE UNIQUE INDEX UX_Bookmarks_BookmarkListId_CanonicalArticleId_Active
    ON Bookmarks (BookmarkListId, CanonicalArticleId) WHERE DateDeleted IS NULL;
CREATE UNIQUE INDEX UX_PublicationUploadCopyrightExemptions_Code
    ON PublicationUploadCopyrightExemptions (Code);
CREATE UNIQUE INDEX UX_PublicationUploadQueueStatuses_Code
    ON PublicationUploadQueueStatuses (Code);
CREATE INDEX IX_PublicationUploadQueue_SubmittedByUserId ON PublicationUploadQueue (SubmittedByUserId);
CREATE INDEX IX_PublicationUploadQueue_StatusId_DateCreated ON PublicationUploadQueue (StatusId, DateCreated);
CREATE INDEX IX_PublicationUploadQueue_SiteId_CanonicalArticleId ON PublicationUploadQueue (SiteId, CanonicalArticleId);

-- ==========================================
-- 6. Perform integrity checks and commit
-- ==========================================
PRAGMA foreign_key_check;

COMMIT;
PRAGMA foreign_keys = ON;
