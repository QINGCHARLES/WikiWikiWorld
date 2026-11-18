-- Precheck before applying AddSitemapIndexes.sql via deploy/db-update.sh.
SELECT NOT EXISTS (
    SELECT 1 FROM pragma_index_list('ArticleRevisions') WHERE name = 'IX_ArticleRevisions_Sitemap_Html'
) AND NOT EXISTS (
    SELECT 1 FROM pragma_index_list('ArticleRevisions') WHERE name = 'IX_ArticleRevisions_Sitemap_Xml'
);
