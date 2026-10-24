-- Apply once to an existing database, after a consistent backup, with sqlite3 -bail.
-- Check first that no article has more than one active link row; the unique index fails otherwise:
--   SELECT SiteId, CanonicalArticleId, COUNT(*) FROM ArticleCultureLinks
--   WHERE DateDeleted IS NULL GROUP BY SiteId, CanonicalArticleId HAVING COUNT(*) > 1;
-- Fresh Create.sql databases already include this change.
BEGIN IMMEDIATE;
CREATE INDEX IX_ArticleCultureLinks_GroupId_Active ON ArticleCultureLinks (ArticleCultureLinkGroupId) WHERE DateDeleted IS NULL;
CREATE UNIQUE INDEX UX_ArticleCultureLinks_SiteId_CanonicalArticleId_Active ON ArticleCultureLinks (SiteId, CanonicalArticleId) WHERE DateDeleted IS NULL;
COMMIT;
