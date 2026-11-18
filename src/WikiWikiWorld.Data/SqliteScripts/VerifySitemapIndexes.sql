-- Verification after applying AddSitemapIndexes.sql via deploy/db-update.sh.
SELECT EXISTS (
    SELECT 1 FROM pragma_index_list('ArticleRevisions') WHERE name = 'IX_ArticleRevisions_Sitemap_Html'
) AND EXISTS (
    SELECT 1 FROM pragma_index_list('ArticleRevisions') WHERE name = 'IX_ArticleRevisions_Sitemap_Xml'
);
