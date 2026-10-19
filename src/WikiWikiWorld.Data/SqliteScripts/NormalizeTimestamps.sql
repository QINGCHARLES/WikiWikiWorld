-- Migration Script: Normalize SQLite ISO 8601 Timestamps
-- Aligns SQLite text timestamp columns with EF Core's SQLite parameter representation ("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz").
-- 1. Replaces 'T' delimiter with space ' '.
-- 2. Replaces 'Z' UTC indicator with '+00:00'.
-- 3. Strips trailing zeros from subsecond precision before timezone offset (+/-) to ensure lexicographical comparisons succeed.

PRAGMA foreign_keys = OFF;
BEGIN TRANSACTION;

-- Users
UPDATE Users SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE Users SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE Users SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE Users SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE Users SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE Users SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- Roles
UPDATE Roles SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE Roles SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE Roles SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE Roles SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE Roles SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE Roles SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- ArticleRevisions
UPDATE ArticleRevisions SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE ArticleRevisions SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE ArticleRevisions SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE ArticleRevisions SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE ArticleRevisions SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE ArticleRevisions SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- FileRevisions
UPDATE FileRevisions SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE FileRevisions SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE FileRevisions SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE FileRevisions SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE FileRevisions SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE FileRevisions SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- ArticleCultureLinks
UPDATE ArticleCultureLinks SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE ArticleCultureLinks SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE ArticleCultureLinks SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE ArticleCultureLinks SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE ArticleCultureLinks SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE ArticleCultureLinks SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- ArticleTalkSubjects
UPDATE ArticleTalkSubjects SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE ArticleTalkSubjects SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE ArticleTalkSubjects SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE ArticleTalkSubjects SET DateModified = replace(DateModified, 'T', ' ') WHERE DateModified LIKE '%T%';
UPDATE ArticleTalkSubjects SET DateModified = replace(DateModified, 'Z', '+00:00') WHERE DateModified LIKE '%Z%';
UPDATE ArticleTalkSubjects SET DateModified = substr(DateModified, 1, instr(DateModified, '.') - 1) || rtrim(rtrim(substr(DateModified, instr(DateModified, '.'), max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10 - instr(DateModified, '.')), '0'), '.') || substr(DateModified, max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10) WHERE DateModified LIKE '%.%0+%' OR DateModified LIKE '%.%0-%';

UPDATE ArticleTalkSubjects SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE ArticleTalkSubjects SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE ArticleTalkSubjects SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- ArticleTalkSubjectPosts
UPDATE ArticleTalkSubjectPosts SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE ArticleTalkSubjectPosts SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE ArticleTalkSubjectPosts SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE ArticleTalkSubjectPosts SET DateModified = replace(DateModified, 'T', ' ') WHERE DateModified LIKE '%T%';
UPDATE ArticleTalkSubjectPosts SET DateModified = replace(DateModified, 'Z', '+00:00') WHERE DateModified LIKE '%Z%';
UPDATE ArticleTalkSubjectPosts SET DateModified = substr(DateModified, 1, instr(DateModified, '.') - 1) || rtrim(rtrim(substr(DateModified, instr(DateModified, '.'), max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10 - instr(DateModified, '.')), '0'), '.') || substr(DateModified, max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10) WHERE DateModified LIKE '%.%0+%' OR DateModified LIKE '%.%0-%';

UPDATE ArticleTalkSubjectPosts SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE ArticleTalkSubjectPosts SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE ArticleTalkSubjectPosts SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- DownloadUrlNotes
UPDATE DownloadUrlNotes SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE DownloadUrlNotes SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE DownloadUrlNotes SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

-- DownloadUrls
UPDATE DownloadUrls SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE DownloadUrls SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE DownloadUrls SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE DownloadUrls SET DateModified = replace(DateModified, 'T', ' ') WHERE DateModified LIKE '%T%';
UPDATE DownloadUrls SET DateModified = replace(DateModified, 'Z', '+00:00') WHERE DateModified LIKE '%Z%';
UPDATE DownloadUrls SET DateModified = substr(DateModified, 1, instr(DateModified, '.') - 1) || rtrim(rtrim(substr(DateModified, instr(DateModified, '.'), max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10 - instr(DateModified, '.')), '0'), '.') || substr(DateModified, max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10) WHERE DateModified LIKE '%.%0+%' OR DateModified LIKE '%.%0-%';

UPDATE DownloadUrls SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE DownloadUrls SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE DownloadUrls SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- BookmarkLists
UPDATE BookmarkLists SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE BookmarkLists SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE BookmarkLists SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE BookmarkLists SET DateModified = replace(DateModified, 'T', ' ') WHERE DateModified LIKE '%T%';
UPDATE BookmarkLists SET DateModified = replace(DateModified, 'Z', '+00:00') WHERE DateModified LIKE '%Z%';
UPDATE BookmarkLists SET DateModified = substr(DateModified, 1, instr(DateModified, '.') - 1) || rtrim(rtrim(substr(DateModified, instr(DateModified, '.'), max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10 - instr(DateModified, '.')), '0'), '.') || substr(DateModified, max(instr(substr(DateModified, 11), '+'), instr(substr(DateModified, 11), '-')) + 10) WHERE DateModified LIKE '%.%0+%' OR DateModified LIKE '%.%0-%';

UPDATE BookmarkLists SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE BookmarkLists SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE BookmarkLists SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- Bookmarks
UPDATE Bookmarks SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE Bookmarks SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE Bookmarks SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE Bookmarks SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE Bookmarks SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE Bookmarks SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- PublicationUploadQueue
UPDATE PublicationUploadQueue SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE PublicationUploadQueue SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE PublicationUploadQueue SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE PublicationUploadQueue SET DateStatusChanged = replace(DateStatusChanged, 'T', ' ') WHERE DateStatusChanged LIKE '%T%';
UPDATE PublicationUploadQueue SET DateStatusChanged = replace(DateStatusChanged, 'Z', '+00:00') WHERE DateStatusChanged LIKE '%Z%';
UPDATE PublicationUploadQueue SET DateStatusChanged = substr(DateStatusChanged, 1, instr(DateStatusChanged, '.') - 1) || rtrim(rtrim(substr(DateStatusChanged, instr(DateStatusChanged, '.'), max(instr(substr(DateStatusChanged, 11), '+'), instr(substr(DateStatusChanged, 11), '-')) + 10 - instr(DateStatusChanged, '.')), '0'), '.') || substr(DateStatusChanged, max(instr(substr(DateStatusChanged, 11), '+'), instr(substr(DateStatusChanged, 11), '-')) + 10) WHERE DateStatusChanged LIKE '%.%0+%' OR DateStatusChanged LIKE '%.%0-%';

UPDATE PublicationUploadQueue SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE PublicationUploadQueue SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE PublicationUploadQueue SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

-- ArticleDomainRedirects
UPDATE ArticleDomainRedirects SET DateCreated = replace(DateCreated, 'T', ' ') WHERE DateCreated LIKE '%T%';
UPDATE ArticleDomainRedirects SET DateCreated = replace(DateCreated, 'Z', '+00:00') WHERE DateCreated LIKE '%Z%';
UPDATE ArticleDomainRedirects SET DateCreated = substr(DateCreated, 1, instr(DateCreated, '.') - 1) || rtrim(rtrim(substr(DateCreated, instr(DateCreated, '.'), max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10 - instr(DateCreated, '.')), '0'), '.') || substr(DateCreated, max(instr(substr(DateCreated, 11), '+'), instr(substr(DateCreated, 11), '-')) + 10) WHERE DateCreated LIKE '%.%0+%' OR DateCreated LIKE '%.%0-%';

UPDATE ArticleDomainRedirects SET DateDeleted = replace(DateDeleted, 'T', ' ') WHERE DateDeleted LIKE '%T%';
UPDATE ArticleDomainRedirects SET DateDeleted = replace(DateDeleted, 'Z', '+00:00') WHERE DateDeleted LIKE '%Z%';
UPDATE ArticleDomainRedirects SET DateDeleted = substr(DateDeleted, 1, instr(DateDeleted, '.') - 1) || rtrim(rtrim(substr(DateDeleted, instr(DateDeleted, '.'), max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10 - instr(DateDeleted, '.')), '0'), '.') || substr(DateDeleted, max(instr(substr(DateDeleted, 11), '+'), instr(substr(DateDeleted, 11), '-')) + 10) WHERE DateDeleted LIKE '%.%0+%' OR DateDeleted LIKE '%.%0-%';

COMMIT;
PRAGMA foreign_keys = ON;
