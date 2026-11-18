-- Apply once to an existing database using deploy/db-update.sh.
-- Adds partial covering indexes on ArticleRevisions for scalable HTML and XML sitemap queries.
BEGIN IMMEDIATE;

CREATE INDEX IX_ArticleRevisions_Sitemap_Html
    ON ArticleRevisions (SiteId, Culture, Type, Title COLLATE NOCASE, Id, UrlSlug, DisplayTitle)
    WHERE IsCurrent AND DateDeleted IS NULL;

CREATE INDEX IX_ArticleRevisions_Sitemap_Xml
    ON ArticleRevisions (SiteId, Culture, Type, Id, UrlSlug, DateCreated)
    WHERE IsCurrent AND DateDeleted IS NULL;

COMMIT;
