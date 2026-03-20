PRAGMA foreign_keys = ON;

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
);

CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON Users (NormalizedUserName);
CREATE INDEX IX_Users_NormalizedEmail ON Users (NormalizedEmail);

CREATE TABLE Roles (
    Id TEXT NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name TEXT NULL,
    NormalizedName TEXT NULL,
    ConcurrencyStamp TEXT NULL,
    DateCreated TEXT NOT NULL,
    DateDeleted TEXT NULL
);

CREATE UNIQUE INDEX IX_Roles_NormalizedName ON Roles (NormalizedName);

CREATE TABLE UserRoles (
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Roles_RoleId FOREIGN KEY (RoleId) REFERENCES Roles (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
);

CREATE INDEX IX_UserRoles_RoleId ON UserRoles (RoleId);

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
);

CREATE INDEX IX_ArticleRevisions_CreatedByUserId ON ArticleRevisions (CreatedByUserId);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_DateCreated ON ArticleRevisions (SiteId, Culture, UrlSlug, DateCreated);
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_IsCurrent ON ArticleRevisions (SiteId, Culture, UrlSlug, IsCurrent);

CREATE TABLE FileRevisions (
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

CREATE INDEX IX_FileRevisions_CanonicalFileId_DateCreated ON FileRevisions (CanonicalFileId, DateCreated DESC);
CREATE INDEX IX_FileRevisions_CreatedByUserId ON FileRevisions (CreatedByUserId);

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
);

CREATE INDEX IX_ArticleCultureLinks_CreatedByUserId ON ArticleCultureLinks (CreatedByUserId);
CREATE INDEX IX_ArticleCultureLinks_DeletedByUserId ON ArticleCultureLinks (DeletedByUserId);
CREATE INDEX IX_ArticleCultureLinks_SiteId_CanonicalArticleId ON ArticleCultureLinks (SiteId, CanonicalArticleId);

CREATE TABLE CopyrightStatus (
    Id INTEGER NOT NULL CONSTRAINT PK_CopyrightStatus PRIMARY KEY AUTOINCREMENT,
    Status TEXT NOT NULL
);

CREATE TABLE DownloadUrlStatus (
    Id INTEGER NOT NULL CONSTRAINT PK_DownloadUrlStatus PRIMARY KEY AUTOINCREMENT,
    Status TEXT NOT NULL
);

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

CREATE INDEX IX_DownloadUrls_SiteId_HashSha256 ON DownloadUrls (SiteId, HashSha256);

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
);

CREATE INDEX IX_ArticleTalkSubjects_CreatedByUserId ON ArticleTalkSubjects (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjects_SiteId_CanonicalArticleId ON ArticleTalkSubjects (SiteId, CanonicalArticleId);

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
);

CREATE INDEX IX_ArticleTalkSubjectPosts_ArticleTalkSubjectId ON ArticleTalkSubjectPosts (ArticleTalkSubjectId);
CREATE INDEX IX_ArticleTalkSubjectPosts_CreatedByUserId ON ArticleTalkSubjectPosts (CreatedByUserId);
CREATE INDEX IX_ArticleTalkSubjectPosts_ParentTalkSubjectPostId ON ArticleTalkSubjectPosts (ParentTalkSubjectPostId);
CREATE INDEX IX_ArticleTalkSubjectPosts_SubjectId_ParentId_Active ON ArticleTalkSubjectPosts (ArticleTalkSubjectId, ParentTalkSubjectPostId);
