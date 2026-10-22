# Category membership schema

This step adds storage and EF mapping only. Article saves, the backfill command, and
`{{CategoryArticles}}` rendering are not implemented yet.

`ArticleCategoryMemberships` stores `ArticleRevisionId` (INTEGER, required) and
`CategorySlug` (TEXT, required). The primary key is `(ArticleRevisionId, CategorySlug)`;
the category lookup index is `(CategorySlug, ArticleRevisionId)`. Slugs are immutable
identities without `category:`; display names are independent. No label conversion,
case folding, or category-page existence check is performed by this mapping.

The revision foreign key cascades on physical deletion. Soft deletion and replacement
by a newer revision preserve membership rows. There is no foreign key to a category
page: membership can predate that page. Queries must join filtered `ArticleRevisions`
to enforce site, culture and soft-deletion scope, and select `IsCurrent` for current
listings. The membership DbSet itself is unfiltered and includes historical rows.

## Application procedure

The project uses manually applied SQLite scripts, not EF migrations or automatic
startup schema updates. The database operator applies
`src/WikiWikiWorld.Data/SqliteScripts/AddArticleCategoryMemberships.sql` once to an
existing database before enabling any membership writes. Use a consistent SQLite
backup first and a runner that stops on errors. For the SQLite shell:

```text
sqlite3 -bail /path/to/app.db ".read /path/to/AddArticleCategoryMemberships.sql"
```

The script enables foreign keys before its transaction, creates the empty STRICT
table and index, and commits without changing existing article rows. It deliberately
fails if the table already exists; do not rerun it after successful application.
On failure, close the connection to roll back an uncommitted transaction and diagnose
the error before retrying. Verify with `.schema ArticleCategoryMemberships` and
`PRAGMA foreign_key_check(ArticleCategoryMemberships);` (which must return no rows).

Fresh databases use the updated `SqliteScripts/Create.sql` instead; do not run the
upgrade script afterward. Historical conversion scripts remain unchanged.

An application rollback can leave the additive table in place. Do not drop a populated
table or restore an old backup over subsequent writing merely to roll back code.
No existing development or production database was modified by this implementation.

## Verification

`dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~ArticleCategoryMembershipSchemaTests`

Tests exercise EF-created, fresh SQL, and upgraded SQLite schemas: existing article
preservation, EF writes/reads, duplicate/null/orphan rejection, indexed category
lookup, membership retention on soft deletion, and cascading physical deletion.
