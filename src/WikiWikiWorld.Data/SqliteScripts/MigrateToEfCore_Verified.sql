PRAGMA foreign_keys = OFF;

-- BEGIN TRANSACTION;

-- =================================================================================================
-- USERS
-- =================================================================================================
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

INSERT INTO Users_New 
(Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, ProfilePicGuid, DateCreated, DateDeleted)
SELECT 
    -- Convert BLOB Guid to String Guid (Format: 00000000-0000-0000-0000-000000000000)
    CASE 
        WHEN length(Id) = 16 AND typeof(Id) = 'blob' THEN 
            lower(
                substr(hex(Id), 7, 2) || substr(hex(Id), 5, 2) || substr(hex(Id), 3, 2) || substr(hex(Id), 1, 2) || '-' ||
                substr(hex(Id), 11, 2) || substr(hex(Id), 9, 2) || '-' ||
                substr(hex(Id), 15, 2) || substr(hex(Id), 13, 2) || '-' ||
                substr(hex(Id), 17, 4) || '-' ||
                substr(hex(Id), 21, 12)
            )
        ELSE Id 
    END,
    UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, 
    -- ProfilePicGuid is nullable, might be string or blob
    CASE 
        WHEN ProfilePicGuid IS NOT NULL AND length(ProfilePicGuid) = 16 AND typeof(ProfilePicGuid) = 'blob' THEN 
            lower(
                substr(hex(ProfilePicGuid), 7, 2) || substr(hex(ProfilePicGuid), 5, 2) || substr(hex(ProfilePicGuid), 3, 2) || substr(hex(ProfilePicGuid), 1, 2) || '-' ||
                substr(hex(ProfilePicGuid), 11, 2) || substr(hex(ProfilePicGuid), 9, 2) || '-' ||
                substr(hex(ProfilePicGuid), 15, 2) || substr(hex(ProfilePicGuid), 13, 2) || '-' ||
                substr(hex(ProfilePicGuid), 17, 4) || '-' ||
                substr(hex(ProfilePicGuid), 21, 12)
            )
        ELSE ProfilePicGuid
    END,
    DateCreated, DateDeleted
FROM Users;

DROP TABLE Users;
ALTER TABLE Users_New RENAME TO Users;

CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON Users (NormalizedUserName);
CREATE INDEX IX_Users_NormalizedEmail ON Users (NormalizedEmail);

-- =================================================================================================
-- ROLES
-- =================================================================================================
CREATE TABLE Roles_New (
    Id TEXT NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name TEXT NULL,
    NormalizedName TEXT NULL,
    ConcurrencyStamp TEXT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL
);

INSERT INTO Roles_New (Id, Name, NormalizedName, ConcurrencyStamp, DateCreated, DateDeleted)
SELECT 
    CASE 
        WHEN length(Id) = 16 AND typeof(Id) = 'blob' THEN 
            lower(
                substr(hex(Id), 7, 2) || substr(hex(Id), 5, 2) || substr(hex(Id), 3, 2) || substr(hex(Id), 1, 2) || '-' ||
                substr(hex(Id), 11, 2) || substr(hex(Id), 9, 2) || '-' ||
                substr(hex(Id), 15, 2) || substr(hex(Id), 13, 2) || '-' ||
                substr(hex(Id), 17, 4) || '-' ||
                substr(hex(Id), 21, 12)
            )
        ELSE Id 
    END,
    Name, NormalizedName, ConcurrencyStamp, DateCreated, DateDeleted
FROM Roles;

DROP TABLE Roles;
ALTER TABLE Roles_New RENAME TO Roles;

CREATE UNIQUE INDEX IX_Roles_NormalizedName ON Roles (NormalizedName);

-- =================================================================================================
-- USER ROLES
-- =================================================================================================
CREATE TABLE UserRoles_New (
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES Roles (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

INSERT INTO UserRoles_New (UserId, RoleId)
SELECT 
    CASE 
        WHEN length(UserId) = 16 AND typeof(UserId) = 'blob' THEN 
            lower(
                substr(hex(UserId), 7, 2) || substr(hex(UserId), 5, 2) || substr(hex(UserId), 3, 2) || substr(hex(UserId), 1, 2) || '-' ||
                substr(hex(UserId), 11, 2) || substr(hex(UserId), 9, 2) || '-' ||
                substr(hex(UserId), 15, 2) || substr(hex(UserId), 13, 2) || '-' ||
                substr(hex(UserId), 17, 4) || '-' ||
                substr(hex(UserId), 21, 12)
            )
        ELSE UserId 
    END,
    CASE 
        WHEN length(RoleId) = 16 AND typeof(RoleId) = 'blob' THEN 
            lower(
                substr(hex(RoleId), 7, 2) || substr(hex(RoleId), 5, 2) || substr(hex(RoleId), 3, 2) || substr(hex(RoleId), 1, 2) || '-' ||
                substr(hex(RoleId), 11, 2) || substr(hex(RoleId), 9, 2) || '-' ||
                substr(hex(RoleId), 15, 2) || substr(hex(RoleId), 13, 2) || '-' ||
                substr(hex(RoleId), 17, 4) || '-' ||
                substr(hex(RoleId), 21, 12)
            )
        ELSE RoleId 
    END
FROM UserRoles;

DROP TABLE UserRoles;
ALTER TABLE UserRoles_New RENAME TO UserRoles;

CREATE INDEX IX_UserRoles_RoleId ON UserRoles (RoleId);

-- =================================================================================================
-- IDENTITY TABLES (NEW)
-- =================================================================================================
CREATE TABLE UserClaims (
    Id INTEGER NOT NULL CONSTRAINT PK_UserClaims PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL,
    ClaimType TEXT NULL,
    ClaimValue TEXT NULL,
    CONSTRAINT FK_UserClaims_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);
CREATE INDEX IX_UserClaims_UserId ON UserClaims (UserId);

CREATE TABLE UserLogins (
    LoginProvider TEXT NOT NULL,
    ProviderKey TEXT NOT NULL,
    ProviderDisplayName TEXT NULL,
    UserId TEXT NOT NULL,
    CONSTRAINT PK_UserLogins PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_UserLogins_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);
CREATE INDEX IX_UserLogins_UserId ON UserLogins (UserId);

CREATE TABLE RoleClaims (
    Id INTEGER NOT NULL CONSTRAINT PK_RoleClaims PRIMARY KEY AUTOINCREMENT,
    RoleId TEXT NOT NULL,
    ClaimType TEXT NULL,
    ClaimValue TEXT NULL,
    CONSTRAINT FK_RoleClaims_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES Roles (Id) ON DELETE CASCADE
);
CREATE INDEX IX_RoleClaims_RoleId ON RoleClaims (RoleId);

CREATE TABLE UserTokens (
    UserId TEXT NOT NULL,
    LoginProvider TEXT NOT NULL,
    Name TEXT NOT NULL,
    Value TEXT NULL,
    CONSTRAINT PK_UserTokens PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_UserTokens_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

-- =================================================================================================
-- ARTICLE REVISIONS
-- =================================================================================================
CREATE TABLE ArticleRevisions_New (
    Id INTEGER NOT NULL CONSTRAINT PK_ArticleRevisions PRIMARY KEY AUTOINCREMENT,
    CanonicalArticleId TEXT NOT NULL,
    SiteId INTEGER NOT NULL,
    Culture TEXT NOT NULL,
    Title TEXT NOT NULL,
    DisplayTitle TEXT NULL,
    UrlSlug TEXT NOT NULL,
    IsCurrent INTEGER NOT NULL,
    Type INTEGER NOT NULL, -- Changed from TEXT to INTEGER for EF Core
    Text TEXT NOT NULL,
    CanonicalFileId TEXT NULL,
    RevisionReason TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL,
    CONSTRAINT FK_ArticleRevisions_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE CASCADE
);

INSERT INTO ArticleRevisions_New 
(Id, CanonicalArticleId, SiteId, Culture, Title, DisplayTitle, UrlSlug, IsCurrent, Type, Text, CanonicalFileId, RevisionReason, CreatedByUserId, DateCreated, DateDeleted)
SELECT 
    Id, 
    CASE 
        WHEN length(CanonicalArticleId) = 16 AND typeof(CanonicalArticleId) = 'blob' THEN 
            lower(
                substr(hex(CanonicalArticleId), 7, 2) || substr(hex(CanonicalArticleId), 5, 2) || substr(hex(CanonicalArticleId), 3, 2) || substr(hex(CanonicalArticleId), 1, 2) || '-' ||
                substr(hex(CanonicalArticleId), 11, 2) || substr(hex(CanonicalArticleId), 9, 2) || '-' ||
                substr(hex(CanonicalArticleId), 15, 2) || substr(hex(CanonicalArticleId), 13, 2) || '-' ||
                substr(hex(CanonicalArticleId), 17, 4) || '-' ||
                substr(hex(CanonicalArticleId), 21, 12)
            )
        ELSE CanonicalArticleId 
    END,
    SiteId, Culture, Title, DisplayTitle, UrlSlug, IsCurrent, 
    -- Convert Type String to Int if necessary, or just cast
    CASE 
        WHEN typeof(Type) = 'text' AND Type = 'Article' THEN 0
        WHEN typeof(Type) = 'text' AND Type = 'Category' THEN 1
        WHEN typeof(Type) = 'text' AND Type = 'File' THEN 2
        WHEN typeof(Type) = 'text' AND Type = 'User' THEN 3
        WHEN typeof(Type) = 'text' AND Type = 'Info' THEN 4
        ELSE CAST(Type AS INTEGER)
    END,
    Text, 
    CASE 
        WHEN CanonicalFileId IS NOT NULL AND length(CanonicalFileId) = 16 AND typeof(CanonicalFileId) = 'blob' THEN 
            lower(
                substr(hex(CanonicalFileId), 7, 2) || substr(hex(CanonicalFileId), 5, 2) || substr(hex(CanonicalFileId), 3, 2) || substr(hex(CanonicalFileId), 1, 2) || '-' ||
                substr(hex(CanonicalFileId), 11, 2) || substr(hex(CanonicalFileId), 9, 2) || '-' ||
                substr(hex(CanonicalFileId), 15, 2) || substr(hex(CanonicalFileId), 13, 2) || '-' ||
                substr(hex(CanonicalFileId), 17, 4) || '-' ||
                substr(hex(CanonicalFileId), 21, 12)
            )
        ELSE CanonicalFileId 
    END,
    RevisionReason, 
    CASE 
        WHEN length(CreatedByUserId) = 16 AND typeof(CreatedByUserId) = 'blob' THEN 
            lower(
                substr(hex(CreatedByUserId), 7, 2) || substr(hex(CreatedByUserId), 5, 2) || substr(hex(CreatedByUserId), 3, 2) || substr(hex(CreatedByUserId), 1, 2) || '-' ||
                substr(hex(CreatedByUserId), 11, 2) || substr(hex(CreatedByUserId), 9, 2) || '-' ||
                substr(hex(CreatedByUserId), 15, 2) || substr(hex(CreatedByUserId), 13, 2) || '-' ||
                substr(hex(CreatedByUserId), 17, 4) || '-' ||
                substr(hex(CreatedByUserId), 21, 12)
            )
        ELSE CreatedByUserId 
    END,
    DateCreated, DateDeleted
FROM ArticleRevisions;

DROP TABLE ArticleRevisions;
ALTER TABLE ArticleRevisions_New RENAME TO ArticleRevisions;

CREATE INDEX IX_ArticleRevisions_CreatedByUserId ON ArticleRevisions (CreatedByUserId);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_DateCreated ON ArticleRevisions (SiteId, Culture, UrlSlug, DateCreated);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_IsCurrent ON ArticleRevisions (SiteId, Culture, UrlSlug, IsCurrent);

-- =================================================================================================
-- FILE REVISIONS
-- =================================================================================================
CREATE TABLE FileRevisions_New (
    Id INTEGER NOT NULL CONSTRAINT PK_FileRevisions PRIMARY KEY AUTOINCREMENT,
    CanonicalFileId TEXT NOT NULL,
    IsCurrent INTEGER NOT NULL,
    Type INTEGER NOT NULL, -- Changed from TEXT to INTEGER for EF Core
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

INSERT INTO FileRevisions_New 
(Id, CanonicalFileId, IsCurrent, Type, Filename, MimeType, FileSizeBytes, Source, RevisionReason, SourceAndRevisionReasonCulture, CreatedByUserId, DateCreated, DateDeleted)
SELECT 
    Id, 
    CASE 
        WHEN length(CanonicalFileId) = 16 AND typeof(CanonicalFileId) = 'blob' THEN 
            lower(
                substr(hex(CanonicalFileId), 7, 2) || substr(hex(CanonicalFileId), 5, 2) || substr(hex(CanonicalFileId), 3, 2) || substr(hex(CanonicalFileId), 1, 2) || '-' ||
                substr(hex(CanonicalFileId), 11, 2) || substr(hex(CanonicalFileId), 9, 2) || '-' ||
                substr(hex(CanonicalFileId), 15, 2) || substr(hex(CanonicalFileId), 13, 2) || '-' ||
                substr(hex(CanonicalFileId), 17, 4) || '-' ||
                substr(hex(CanonicalFileId), 21, 12)
            )
        ELSE CanonicalFileId 
    END,
    IsCurrent,
    CASE 
        WHEN typeof(Type) = 'text' AND Type = 'Article' THEN 0
        WHEN typeof(Type) = 'text' AND Type = 'Category' THEN 1
        WHEN typeof(Type) = 'text' AND Type = 'File' THEN 2
        WHEN typeof(Type) = 'text' AND Type = 'User' THEN 3
        WHEN typeof(Type) = 'text' AND Type = 'Info' THEN 4
        ELSE CAST(Type AS INTEGER)
    END,
    Filename, MimeType, FileSizeBytes, Source, RevisionReason, SourceAndRevisionReasonCulture, 
    CASE 
        WHEN length(CreatedByUserId) = 16 AND typeof(CreatedByUserId) = 'blob' THEN 
            lower(
                substr(hex(CreatedByUserId), 7, 2) || substr(hex(CreatedByUserId), 5, 2) || substr(hex(CreatedByUserId), 3, 2) || substr(hex(CreatedByUserId), 1, 2) || '-' ||
                substr(hex(CreatedByUserId), 11, 2) || substr(hex(CreatedByUserId), 9, 2) || '-' ||
                substr(hex(CreatedByUserId), 15, 2) || substr(hex(CreatedByUserId), 13, 2) || '-' ||
                substr(hex(CreatedByUserId), 17, 4) || '-' ||
                substr(hex(CreatedByUserId), 21, 12)
            )
        ELSE CreatedByUserId 
    END,
    DateCreated, DateDeleted
FROM FileRevisions;

DROP TABLE FileRevisions;
ALTER TABLE FileRevisions_New RENAME TO FileRevisions;

CREATE INDEX IX_FileRevisions_CanonicalFileId_DateCreated ON FileRevisions (CanonicalFileId, DateCreated DESC);
CREATE INDEX IX_FileRevisions_CreatedByUserId ON FileRevisions (CreatedByUserId);

-- =================================================================================================
-- ARTICLE CULTURE LINKS
-- =================================================================================================
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

INSERT INTO ArticleCultureLinks_New 
(Id, SiteId, CanonicalArticleId, ArticleCultureLinkGroupId, CreatedByUserId, DateCreated, DeletedByUserId, DateDeleted)
SELECT 
    Id, SiteId, 
    CASE 
        WHEN length(CanonicalArticleId) = 16 AND typeof(CanonicalArticleId) = 'blob' THEN 
            lower(
                substr(hex(CanonicalArticleId), 7, 2) || substr(hex(CanonicalArticleId), 5, 2) || substr(hex(CanonicalArticleId), 3, 2) || substr(hex(CanonicalArticleId), 1, 2) || '-' ||
                substr(hex(CanonicalArticleId), 11, 2) || substr(hex(CanonicalArticleId), 9, 2) || '-' ||
                substr(hex(CanonicalArticleId), 15, 2) || substr(hex(CanonicalArticleId), 13, 2) || '-' ||
                substr(hex(CanonicalArticleId), 17, 4) || '-' ||
                substr(hex(CanonicalArticleId), 21, 12)
            )
        ELSE CanonicalArticleId 
    END,
    CASE 
        WHEN length(ArticleCultureLinkGroupId) = 16 AND typeof(ArticleCultureLinkGroupId) = 'blob' THEN 
            lower(
                substr(hex(ArticleCultureLinkGroupId), 7, 2) || substr(hex(ArticleCultureLinkGroupId), 5, 2) || substr(hex(ArticleCultureLinkGroupId), 3, 2) || substr(hex(ArticleCultureLinkGroupId), 1, 2) || '-' ||
                substr(hex(ArticleCultureLinkGroupId), 11, 2) || substr(hex(ArticleCultureLinkGroupId), 9, 2) || '-' ||
                substr(hex(ArticleCultureLinkGroupId), 15, 2) || substr(hex(ArticleCultureLinkGroupId), 13, 2) || '-' ||
                substr(hex(ArticleCultureLinkGroupId), 17, 4) || '-' ||
                substr(hex(ArticleCultureLinkGroupId), 21, 12)
            )
        ELSE ArticleCultureLinkGroupId 
    END,
    CASE 
        WHEN length(CreatedByUserId) = 16 AND typeof(CreatedByUserId) = 'blob' THEN 
            lower(
                substr(hex(CreatedByUserId), 7, 2) || substr(hex(CreatedByUserId), 5, 2) || substr(hex(CreatedByUserId), 3, 2) || substr(hex(CreatedByUserId), 1, 2) || '-' ||
                substr(hex(CreatedByUserId), 11, 2) || substr(hex(CreatedByUserId), 9, 2) || '-' ||
                substr(hex(CreatedByUserId), 15, 2) || substr(hex(CreatedByUserId), 13, 2) || '-' ||
                substr(hex(CreatedByUserId), 17, 4) || '-' ||
                substr(hex(CreatedByUserId), 21, 12)
            )
        ELSE CreatedByUserId 
    END,
    DateCreated, 
    CASE 
        WHEN DeletedByUserId IS NOT NULL AND length(DeletedByUserId) = 16 AND typeof(DeletedByUserId) = 'blob' THEN 
            lower(
                substr(hex(DeletedByUserId), 7, 2) || substr(hex(DeletedByUserId), 5, 2) || substr(hex(DeletedByUserId), 3, 2) || substr(hex(DeletedByUserId), 1, 2) || '-' ||
                substr(hex(DeletedByUserId), 11, 2) || substr(hex(DeletedByUserId), 9, 2) || '-' ||
                substr(hex(DeletedByUserId), 15, 2) || substr(hex(DeletedByUserId), 13, 2) || '-' ||
                substr(hex(DeletedByUserId), 17, 4) || '-' ||
                substr(hex(DeletedByUserId), 21, 12)
            )
        ELSE DeletedByUserId 
    END,
    DateDeleted
FROM ArticleCultureLinks;

DROP TABLE ArticleCultureLinks;
ALTER TABLE ArticleCultureLinks_New RENAME TO ArticleCultureLinks;

CREATE INDEX IX_ArticleCultureLinks_CreatedByUserId ON ArticleCultureLinks (CreatedByUserId);
CREATE INDEX IX_ArticleCultureLinks_DeletedByUserId ON ArticleCultureLinks (DeletedByUserId);
CREATE INDEX IX_ArticleCultureLinks_SiteId_CanonicalArticleId ON ArticleCultureLinks (SiteId, CanonicalArticleId);

-- =================================================================================================
-- DOWNLOAD URLS
-- =================================================================================================
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

INSERT INTO DownloadUrls_New 
(Id, SiteId, HashSha256, Filename, MimeType, FileSizeBytes, DownloadUrls, Quality, NeedsOcr, IsComplete, CreatedByUserId, DateCreated, DateModified, DateDeleted)
SELECT 
    Id, SiteId, HashSha256, Filename, MimeType, FileSizeBytes, DownloadUrls, Quality, NeedsOcr, IsComplete, 
    CASE 
        WHEN length(CreatedByUserId) = 16 AND typeof(CreatedByUserId) = 'blob' THEN 
            lower(
                substr(hex(CreatedByUserId), 7, 2) || substr(hex(CreatedByUserId), 5, 2) || substr(hex(CreatedByUserId), 3, 2) || substr(hex(CreatedByUserId), 1, 2) || '-' ||
                substr(hex(CreatedByUserId), 11, 2) || substr(hex(CreatedByUserId), 9, 2) || '-' ||
                substr(hex(CreatedByUserId), 15, 2) || substr(hex(CreatedByUserId), 13, 2) || '-' ||
                substr(hex(CreatedByUserId), 17, 4) || '-' ||
                substr(hex(CreatedByUserId), 21, 12)
            )
        ELSE CreatedByUserId 
    END,
    DateCreated, DateModified, DateDeleted
FROM DownloadUrls;

DROP TABLE DownloadUrls;
ALTER TABLE DownloadUrls_New RENAME TO DownloadUrls;

CREATE INDEX IX_DownloadUrls_SiteId_HashSha256 ON DownloadUrls (SiteId, HashSha256);

-- =================================================================================================
-- ARTICLE TALK SUBJECTS
-- =================================================================================================
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

INSERT INTO ArticleTalkSubjects_New 
(Id, SiteId, CanonicalArticleId, Subject, UrlSlug, HasBeenEdited, CreatedByUserId, DateCreated, DateModified, DateDeleted)
SELECT 
    Id, SiteId, 
    CASE 
        WHEN length(CanonicalArticleId) = 16 AND typeof(CanonicalArticleId) = 'blob' THEN 
            lower(
                substr(hex(CanonicalArticleId), 7, 2) || substr(hex(CanonicalArticleId), 5, 2) || substr(hex(CanonicalArticleId), 3, 2) || substr(hex(CanonicalArticleId), 1, 2) || '-' ||
                substr(hex(CanonicalArticleId), 11, 2) || substr(hex(CanonicalArticleId), 9, 2) || '-' ||
                substr(hex(CanonicalArticleId), 15, 2) || substr(hex(CanonicalArticleId), 13, 2) || '-' ||
                substr(hex(CanonicalArticleId), 17, 4) || '-' ||
                substr(hex(CanonicalArticleId), 21, 12)
            )
        ELSE CanonicalArticleId 
    END,
    Subject, UrlSlug, HasBeenEdited, 
    CASE 
        WHEN length(CreatedByUserId) = 16 AND typeof(CreatedByUserId) = 'blob' THEN 
            lower(
                substr(hex(CreatedByUserId), 7, 2) || substr(hex(CreatedByUserId), 5, 2) || substr(hex(CreatedByUserId), 3, 2) || substr(hex(CreatedByUserId), 1, 2) || '-' ||
                substr(hex(CreatedByUserId), 11, 2) || substr(hex(CreatedByUserId), 9, 2) || '-' ||
                substr(hex(CreatedByUserId), 15, 2) || substr(hex(CreatedByUserId), 13, 2) || '-' ||
                substr(hex(CreatedByUserId), 17, 4) || '-' ||
                substr(hex(CreatedByUserId), 21, 12)
            )
        ELSE CreatedByUserId 
    END,
    DateCreated, DateModified, DateDeleted
FROM ArticleTalkSubjects;

DROP TABLE ArticleTalkSubjects;
ALTER TABLE ArticleTalkSubjects_New RENAME TO ArticleTalkSubjects;

CREATE INDEX IX_ArticleTalkSubjects_CreatedByUserId ON ArticleTalkSubjects (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjects_SiteId_CanonicalArticleId ON ArticleTalkSubjects (SiteId, CanonicalArticleId);

-- =================================================================================================
-- ARTICLE TALK SUBJECT POSTS
-- =================================================================================================
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

INSERT INTO ArticleTalkSubjectPosts_New 
(Id, ArticleTalkSubjectId, ParentTalkSubjectPostId, Text, HasBeenEdited, CreatedByUserId, DateCreated, DateModified, DateDeleted)
SELECT 
    Id, ArticleTalkSubjectId, ParentTalkSubjectPostId, Text, HasBeenEdited, 
    CASE 
        WHEN length(CreatedByUserId) = 16 AND typeof(CreatedByUserId) = 'blob' THEN 
            lower(
                substr(hex(CreatedByUserId), 7, 2) || substr(hex(CreatedByUserId), 5, 2) || substr(hex(CreatedByUserId), 3, 2) || substr(hex(CreatedByUserId), 1, 2) || '-' ||
                substr(hex(CreatedByUserId), 11, 2) || substr(hex(CreatedByUserId), 9, 2) || '-' ||
                substr(hex(CreatedByUserId), 15, 2) || substr(hex(CreatedByUserId), 13, 2) || '-' ||
                substr(hex(CreatedByUserId), 17, 4) || '-' ||
                substr(hex(CreatedByUserId), 21, 12)
            )
        ELSE CreatedByUserId 
    END,
    DateCreated, DateModified, DateDeleted
FROM ArticleTalkSubjectPosts;

DROP TABLE ArticleTalkSubjectPosts;
ALTER TABLE ArticleTalkSubjectPosts_New RENAME TO ArticleTalkSubjectPosts;

CREATE INDEX IX_ArticleTalkSubjectPosts_ArticleTalkSubjectId ON ArticleTalkSubjectPosts (ArticleTalkSubjectId);
CREATE INDEX IX_ArticleTalkSubjectPosts_CreatedByUserId ON ArticleTalkSubjectPosts (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjectPosts_ParentTalkSubjectPostId ON ArticleTalkSubjectPosts (ParentTalkSubjectPostId);
CREATE INDEX IX_ArticleTalkSubjectPosts_SubjectId_ParentId_Active ON ArticleTalkSubjectPosts (ArticleTalkSubjectId, ParentTalkSubjectPostId);

-- COMMIT;
PRAGMA foreign_keys = ON;
