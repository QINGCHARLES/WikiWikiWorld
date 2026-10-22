-- Apply once to an existing database using a runner that stops on errors.
-- No membership backfill or changes to existing article rows are performed here.
PRAGMA foreign_keys = ON;

BEGIN IMMEDIATE;

CREATE TABLE ArticleCategoryMemberships (
    ArticleRevisionId INTEGER NOT NULL,
    CategorySlug TEXT NOT NULL,
    CONSTRAINT PK_ArticleCategoryMemberships PRIMARY KEY (ArticleRevisionId, CategorySlug),
    CONSTRAINT FK_ArticleCategoryMemberships_ArticleRevisions_ArticleRevisionId
        FOREIGN KEY (ArticleRevisionId) REFERENCES ArticleRevisions (Id) ON DELETE CASCADE
) STRICT;

CREATE INDEX IX_ArticleCategoryMemberships_CategorySlug_ArticleRevisionId
    ON ArticleCategoryMemberships (CategorySlug, ArticleRevisionId);

COMMIT;
