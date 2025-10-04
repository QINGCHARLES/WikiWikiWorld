PRAGMA foreign_keys = ON;

CREATE TABLE Users
(
	Id TEXT PRIMARY KEY NOT NULL,
	UserName TEXT NOT NULL UNIQUE COLLATE NOCASE, 
	NormalizedUserName TEXT NOT NULL UNIQUE COLLATE NOCASE, 
	Email TEXT NOT NULL UNIQUE, 
	NormalizedEmail TEXT NOT NULL UNIQUE, 
	EmailConfirmed INTEGER DEFAULT 0 NOT NULL CHECK (EmailConfirmed IN (0, 1)), 
	PasswordHash TEXT NOT NULL, 
	SecurityStamp TEXT NOT NULL, 
	ConcurrencyStamp TEXT NOT NULL, 
	TwoFactorEnabled INTEGER DEFAULT 0 NOT NULL CHECK (TwoFactorEnabled IN (0, 1)), 
	LockoutEnd TEXT NULL DEFAULT NULL, 
	LockoutEnabled INTEGER DEFAULT 1 NOT NULL CHECK (LockoutEnabled IN (0, 1)), 
	AccessFailedCount INTEGER DEFAULT 0 NOT NULL,
	ProfilePicGuid TEXT NULL DEFAULT NULL,
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DateDeleted TEXT NULL DEFAULT NULL
) STRICT;

CREATE INDEX IX_Users_NormalizedUserName_Active
ON Users (NormalizedUserName COLLATE NOCASE)
WHERE DateDeleted IS NULL;

CREATE TABLE Roles
(
	Id TEXT PRIMARY KEY NOT NULL, 
	Name TEXT NOT NULL UNIQUE, 
	NormalizedName TEXT NOT NULL UNIQUE, 
	ConcurrencyStamp TEXT NOT NULL, 
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DateDeleted TEXT NULL DEFAULT NULL
) STRICT;

CREATE INDEX IX_Roles_NormalizedName_Active
ON Roles (NormalizedName)
WHERE DateDeleted IS NULL;

CREATE TABLE UserRoles
(
	UserId TEXT NOT NULL, 
	RoleId TEXT NOT NULL, 
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DateDeleted TEXT NULL DEFAULT NULL,
	PRIMARY KEY (UserId, RoleId), 
	FOREIGN KEY (UserId) REFERENCES Users(Id),
	FOREIGN KEY (RoleId) REFERENCES Roles(Id)
) STRICT;

CREATE INDEX IX_UserRoles_UserId_RoleId_Active
ON UserRoles (UserId, RoleId)
WHERE DateDeleted IS NULL;

CREATE TABLE ArticleRevisions
(
	Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
	CanonicalArticleId TEXT NOT NULL, 
	SiteId INTEGER NOT NULL,
	Culture TEXT NOT NULL,
	Title TEXT NOT NULL,
	DisplayTitle TEXT NULL,
	UrlSlug TEXT NOT NULL,
	IsCurrent INTEGER NOT NULL CHECK (IsCurrent IN (0, 1)), 
	Type TEXT NOT NULL CHECK (Type IN ('ARTICLE', 'CATEGORY', 'FILE', 'USER', 'INFO')),
	Text TEXT NOT NULL,
	CanonicalFileId TEXT NULL,
	RevisionReason TEXT NOT NULL,
	CreatedByUserId TEXT NOT NULL, 
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DateDeleted TEXT NULL DEFAULT NULL, 
	FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
) STRICT;

-- Existing index for retrieving historical revisions efficiently
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_DateCreated
ON ArticleRevisions (SiteId, Culture, UrlSlug, DateCreated DESC)
WHERE DateDeleted IS NULL;

-- New index to optimize lookups based on IsCurrent = 1
CREATE INDEX IX_ArticleRevisions_SiteId_Culture_UrlSlug_IsCurrent
ON ArticleRevisions (SiteId, Culture, UrlSlug, IsCurrent)
WHERE IsCurrent = 1 AND DateDeleted IS NULL;


CREATE TABLE FileRevisions
(
	Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
	CanonicalFileId TEXT NOT NULL, 
	IsCurrent INTEGER NOT NULL CHECK (IsCurrent IN (0, 1)),
	Type TEXT NOT NULL CHECK (Type IN ('IMAGE2D', 'VIDEO', 'AUDIO')),
	Filename TEXT NOT NULL,
	MimeType TEXT NOT NULL,
	FileSizeBytes INTEGER NOT NULL CHECK (FileSizeBytes >= 0),
	Source TEXT NULL,
	RevisionReason TEXT NOT NULL,
	SourceAndRevisionReasonCulture TEXT NOT NULL,
	CreatedByUserId TEXT NOT NULL, 
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DateDeleted TEXT NULL DEFAULT NULL,
	FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
) STRICT;

CREATE INDEX IX_FileRevisions_CanonicalFileId_DateCreated
ON FileRevisions (CanonicalFileId, DateCreated DESC)
WHERE DateDeleted IS NULL;

CREATE TABLE ArticleCultureLinks
(
	Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
	SiteId INTEGER NOT NULL,
	CanonicalArticleId TEXT NOT NULL,
	ArticleCultureLinkGroupId TEXT NOT NULL,
	CreatedByUserId TEXT NOT NULL, 
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DeletedByUserId TEXT NULL, 
	DateDeleted TEXT NULL DEFAULT NULL, 
	FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
	FOREIGN KEY (DeletedByUserId) REFERENCES Users(Id)
) STRICT;

CREATE INDEX IX_ArticleCultureLinks_SiteId_CanonicalId_Active
ON ArticleCultureLinks (SiteId, CanonicalArticleId)
WHERE DateDeleted IS NULL;

CREATE TABLE DownloadUrls
(
	Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
	SiteId INTEGER NOT NULL,
	HashSha256 TEXT NOT NULL,
	Filename TEXT NOT NULL,
	MimeType TEXT NOT NULL,
	FileSizeBytes INTEGER NOT NULL CHECK (FileSizeBytes >= 0),
	DownloadUrls TEXT NULL,
	Quality INTEGER NULL CHECK (Quality IN (1, 2, 3, 4, 5)), 
	NeedsOcr INTEGER NULL CHECK (NeedsOcr IN (0, 1)), 
	IsComplete INTEGER NULL CHECK (IsComplete IN (0, 1)), 
	CreatedByUserId TEXT NOT NULL, 
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DateModified TEXT NULL DEFAULT NULL, 
	DateDeleted TEXT NULL DEFAULT NULL 
) STRICT;

CREATE INDEX IX_DownloadUrls_SiteId_HashSha256_Active
ON DownloadUrls (SiteId, HashSha256)
WHERE DateDeleted IS NULL;

DROP INDEX IF EXISTS IX_ArticleTalkSubjects_SiteId_ArticleId_Active;

CREATE TABLE ArticleTalkSubjects
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    SiteId INTEGER NOT NULL,
    CanonicalArticleId TEXT NOT NULL,  
    Subject TEXT NOT NULL,
    UrlSlug TEXT NOT NULL,
    HasBeenEdited INTEGER DEFAULT 0 NOT NULL CHECK (HasBeenEdited IN (0, 1)),
    CreatedByUserId TEXT NOT NULL,       
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
    DateModified TEXT NULL DEFAULT NULL, 
    DateDeleted TEXT NULL DEFAULT NULL,  
    FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
) STRICT;

CREATE INDEX IX_ArticleTalkSubjects_SiteId_CanonicalArticleId_Active
ON ArticleTalkSubjects (SiteId, CanonicalArticleId)
WHERE DateDeleted IS NULL;

CREATE TABLE ArticleTalkSubjectPosts
(
	Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
	ArticleTalkSubjectId INTEGER NOT NULL,
	ParentTalkSubjectPostId INTEGER NULL,
	Text TEXT NOT NULL,
	HasBeenEdited INTEGER DEFAULT 0 NOT NULL CHECK (HasBeenEdited IN (0, 1)),
	CreatedByUserId TEXT NOT NULL, 
	DateCreated TEXT DEFAULT (substr(strftime('%Y-%m-%dT%H:%M:%f000', 'now'), 1, 26) || '+00:00') NOT NULL,
	DateModified TEXT NULL DEFAULT NULL, 
	DateDeleted TEXT NULL DEFAULT NULL, 
	FOREIGN KEY (ArticleTalkSubjectId) REFERENCES ArticleTalkSubjects(Id),
	FOREIGN KEY (ParentTalkSubjectPostId) REFERENCES ArticleTalkSubjectPosts(Id),
	FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
) STRICT;

CREATE INDEX IX_ArticleTalkSubjectPosts_SubjectId_ParentId_Active
ON ArticleTalkSubjectPosts (ArticleTalkSubjectId, ParentTalkSubjectPostId)
WHERE DateDeleted IS NULL;
