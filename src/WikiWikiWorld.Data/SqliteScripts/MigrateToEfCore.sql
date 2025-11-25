PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- Users
CREATE TABLE Users_New (
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
);

INSERT INTO Users_New (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, ProfilePicGuid, DateCreated, DateDeleted)
SELECT Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, ProfilePicGuid, DateCreated, DateDeleted
FROM Users;

DROP TABLE Users;
ALTER TABLE Users_New RENAME TO Users;

CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON Users (NormalizedUserName);
CREATE INDEX IX_Users_NormalizedEmail ON Users (NormalizedEmail);

-- Roles
CREATE TABLE Roles_New (
    Id TEXT NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name TEXT NULL,
    NormalizedName TEXT NULL,
    ConcurrencyStamp TEXT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL
);

INSERT INTO Roles_New (Id, Name, NormalizedName, ConcurrencyStamp, DateCreated, DateDeleted)
SELECT Id, Name, NormalizedName, ConcurrencyStamp, DateCreated, DateDeleted
FROM Roles;

DROP TABLE Roles;
ALTER TABLE Roles_New RENAME TO Roles;

CREATE UNIQUE INDEX IX_Roles_NormalizedName ON Roles (NormalizedName);

-- UserRoles
CREATE TABLE UserRoles_New (
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES Roles (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

INSERT INTO UserRoles_New (UserId, RoleId)
SELECT UserId, RoleId
FROM UserRoles;

DROP TABLE UserRoles;
ALTER TABLE UserRoles_New RENAME TO UserRoles;

CREATE INDEX IX_UserRoles_RoleId ON UserRoles (RoleId);

-- UserClaims (New Table for Identity)
CREATE TABLE UserClaims (
    Id INTEGER NOT NULL CONSTRAINT PK_UserClaims PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL,
    ClaimType TEXT NULL,
    ClaimValue TEXT NULL,
    CONSTRAINT FK_UserClaims_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

CREATE INDEX IX_UserClaims_UserId ON UserClaims (UserId);

-- UserLogins (New Table for Identity)
CREATE TABLE UserLogins (
    LoginProvider TEXT NOT NULL,
    ProviderKey TEXT NOT NULL,
    ProviderDisplayName TEXT NULL,
    UserId TEXT NOT NULL,
    CONSTRAINT PK_UserLogins PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_UserLogins_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

CREATE INDEX IX_UserLogins_UserId ON UserLogins (UserId);

-- RoleClaims (New Table for Identity)
CREATE TABLE RoleClaims (
    Id INTEGER NOT NULL CONSTRAINT PK_RoleClaims PRIMARY KEY AUTOINCREMENT,
    RoleId TEXT NOT NULL,
    ClaimType TEXT NULL,
    ClaimValue TEXT NULL,
    CONSTRAINT FK_RoleClaims_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES Roles (Id) ON DELETE CASCADE
);

CREATE INDEX IX_RoleClaims_RoleId ON RoleClaims (RoleId);

-- UserTokens (New Table for Identity)
CREATE TABLE UserTokens (
    UserId TEXT NOT NULL,
    LoginProvider TEXT NOT NULL,
    Name TEXT NOT NULL,
    Value TEXT NULL,
    CONSTRAINT PK_UserTokens PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_UserTokens_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

-- ArticleRevisions
CREATE TABLE ArticleRevisions_New (
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
);

INSERT INTO ArticleRevisions_New (Id, CanonicalArticleId, SiteId, Culture, Title, DisplayTitle, UrlSlug, IsCurrent, Type, Text, CanonicalFileId, RevisionReason, CreatedByUserId, DateCreated, DateDeleted)
SELECT Id, CanonicalArticleId, SiteId, Culture, Title, DisplayTitle, UrlSlug, IsCurrent, Type, Text, CanonicalFileId, RevisionReason, CreatedByUserId, DateCreated, DateDeleted
FROM ArticleRevisions;

DROP TABLE ArticleRevisions;
ALTER TABLE ArticleRevisions_New RENAME TO ArticleRevisions;

CREATE INDEX IX_ArticleRevisions_CreatedByUserId ON ArticleRevisions (CreatedByUserId);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_DateCreated ON ArticleRevisions (SiteId, Culture, UrlSlug, DateCreated);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_IsCurrent ON ArticleRevisions (SiteId, Culture, UrlSlug, IsCurrent);

-- FileRevisions
CREATE TABLE FileRevisions_New (
    Id INTEGER NOT NULL CONSTRAINT PK_FileRevisions PRIMARY KEY AUTOINCREMENT,
    CanonicalFileId TEXT NOT NULL,
    IsCurrent INTEGER NOT NULL,
    Type TEXT NOT NULL,
    Filename TEXT NOT NULL,
    MimeType TEXT NOT NULL,
    FileSizeBytes INTEGER NOT NULL,
    Source TEXT NULL,
    RevisionReason TEXT NOT NULL,
    SourceAndRevisionReasonCulture TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_FileRevisions_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
);

INSERT INTO FileRevisions_New (Id, CanonicalFileId, IsCurrent, Type, Filename, MimeType, FileSizeBytes, Source, RevisionReason, SourceAndRevisionReasonCulture, CreatedByUserId, DateCreated, DateDeleted)
SELECT Id, CanonicalFileId, IsCurrent, Type, Filename, MimeType, FileSizeBytes, Source, RevisionReason, SourceAndRevisionReasonCulture, CreatedByUserId, DateCreated, DateDeleted
FROM FileRevisions;

DROP TABLE FileRevisions;
ALTER TABLE FileRevisions_New RENAME TO FileRevisions;

CREATE INDEX IX_FileRevisions_CanonicalFileId_DateCreated ON FileRevisions (CanonicalFileId, DateCreated DESC);
CREATE INDEX IX_FileRevisions_CreatedByUserId ON FileRevisions (CreatedByUserId);

-- ArticleCultureLinks
CREATE TABLE ArticleCultureLinks_New (
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
);

INSERT INTO ArticleCultureLinks_New (Id, SiteId, CanonicalArticleId, ArticleCultureLinkGroupId, CreatedByUserId, DateCreated, DeletedByUserId, DateDeleted)
SELECT Id, SiteId, CanonicalArticleId, ArticleCultureLinkGroupId, CreatedByUserId, DateCreated, DeletedByUserId, DateDeleted
FROM ArticleCultureLinks;

DROP TABLE ArticleCultureLinks;
ALTER TABLE ArticleCultureLinks_New RENAME TO ArticleCultureLinks;

CREATE INDEX IX_ArticleCultureLinks_CreatedByUserId ON ArticleCultureLinks (CreatedByUserId);
CREATE INDEX IX_ArticleCultureLinks_DeletedByUserId ON ArticleCultureLinks (DeletedByUserId);
CREATE INDEX IX_ArticleCultureLinks_SiteId_CanonicalArticleId ON ArticleCultureLinks (SiteId, CanonicalArticleId);

-- DownloadUrls
CREATE TABLE DownloadUrls_New (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrls PRIMARY KEY AUTOINCREMENT,
    SiteId INTEGER NOT NULL,
    HashSha256 TEXT NOT NULL,
    Filename TEXT NOT NULL,
    MimeType TEXT NOT NULL,
    FileSizeBytes INTEGER NOT NULL,
    DownloadUrls TEXT NULL,
    Quality INTEGER NULL,
    NeedsOcr INTEGER NULL,
    IsComplete INTEGER NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateModified TEXT NULL,
    DateDeleted TEXT NULL
);

INSERT INTO DownloadUrls_New (Id, SiteId, HashSha256, Filename, MimeType, FileSizeBytes, DownloadUrls, Quality, NeedsOcr, IsComplete, CreatedByUserId, DateCreated, DateModified, DateDeleted)
SELECT Id, SiteId, HashSha256, Filename, MimeType, FileSizeBytes, DownloadUrls, Quality, NeedsOcr, IsComplete, CreatedByUserId, DateCreated, DateModified, DateDeleted
FROM DownloadUrls;

DROP TABLE DownloadUrls;
ALTER TABLE DownloadUrls_New RENAME TO DownloadUrls;

CREATE INDEX IX_DownloadUrls_SiteId_HashSha256 ON DownloadUrls (SiteId, HashSha256);

-- ArticleTalkSubjects
CREATE TABLE ArticleTalkSubjects_New (
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
);

INSERT INTO ArticleTalkSubjects_New (Id, SiteId, CanonicalArticleId, Subject, UrlSlug, HasBeenEdited, CreatedByUserId, DateCreated, DateModified, DateDeleted)
SELECT Id, SiteId, CanonicalArticleId, Subject, UrlSlug, HasBeenEdited, CreatedByUserId, DateCreated, DateModified, DateDeleted
FROM ArticleTalkSubjects;

DROP TABLE ArticleTalkSubjects;
ALTER TABLE ArticleTalkSubjects_New RENAME TO ArticleTalkSubjects;

CREATE INDEX IX_ArticleTalkSubjects_CreatedByUserId ON ArticleTalkSubjects (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjects_SiteId_CanonicalArticleId ON ArticleTalkSubjects (SiteId, CanonicalArticleId);

-- ArticleTalkSubjectPosts
CREATE TABLE ArticleTalkSubjectPosts_New (
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
);

INSERT INTO ArticleTalkSubjectPosts_New (Id, ArticleTalkSubjectId, ParentTalkSubjectPostId, Text, HasBeenEdited, CreatedByUserId, DateCreated, DateModified, DateDeleted)
SELECT Id, ArticleTalkSubjectId, ParentTalkSubjectPostId, Text, HasBeenEdited, CreatedByUserId, DateCreated, DateModified, DateDeleted
FROM ArticleTalkSubjectPosts;

DROP TABLE ArticleTalkSubjectPosts;
ALTER TABLE ArticleTalkSubjectPosts_New RENAME TO ArticleTalkSubjectPosts;

CREATE INDEX IX_ArticleTalkSubjectPosts_ArticleTalkSubjectId ON ArticleTalkSubjectPosts (ArticleTalkSubjectId);
CREATE INDEX IX_ArticleTalkSubjectPosts_CreatedByUserId ON ArticleTalkSubjectPosts (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjectPosts_ParentTalkSubjectPostId ON ArticleTalkSubjectPosts (ParentTalkSubjectPostId);
CREATE INDEX IX_ArticleTalkSubjectPosts_SubjectId_ParentId_Active ON ArticleTalkSubjectPosts (ArticleTalkSubjectId, ParentTalkSubjectPostId);

COMMIT;
PRAGMA foreign_keys = ON;
