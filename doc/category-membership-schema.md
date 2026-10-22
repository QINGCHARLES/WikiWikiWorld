# Category membership schema

Storage, EF mapping, application save-time indexing, the backfill command, and
`{{CategoryArticles}}` rendering are implemented. See [authoring and pagination](category-articles.md).

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

## Save-time indexing

The web application's `CategoryMembershipSaveInterceptor` uses the shared Markdig
pipeline to extract categories from new revisions, changed source text, and revisions
being restored to current visibility. It stages distinct slugs with the revision in
the same EF save, so failed membership writes roll back the article write too.
Generated revision IDs flow through the EF relationship; membership navigation is
excluded from article JSON responses.

Explicit slugs retain their exact identity; deduplication uses ordinal comparison.
Code examples and invalid category directives do not become memberships. Ordinary
retirement and soft deletion preserve historical memberships. Loading or rendering an
article does not scan article text or write memberships.

This covers application writes through the registered EF context, including editor,
API, user-page creation, and revision restoration. Direct SQL, bulk EF updates, and
external Python import/maintenance scripts bypass the interceptor and require a
subsequent backfill.

## Backfill command

Run from a terminal, using the web application executable in maintenance mode. It
exits before website startup: no HTTP listener, TLS setup, startup migrations, or
application configuration/secrets are needed. The database path is explicit and must
already exist with the membership schema installed.

From the repository root, build and preview development changes:

```powershell
dotnet build src/WikiWikiWorld.Web -c Release
dotnet src/WikiWikiWorld.Web/bin/Release/net10.0/WikiWikiWorld.Web.dll backfill-categories E:/Source/WikiWikiWorld/data/app.db
```

Take a consistent SQLite backup, then run the same command with `--apply` appended.
Without `--apply`, the connection is read-only. Rerun without it afterward: additions
and removals should both be zero. No article text, identity, timestamps, or history
is changed. The command reports cumulative counts and returns a nonzero exit code
on failure or cancellation.

The scan includes historical and soft-deleted revisions across every site and
language. It reconciles distinct valid parsed slugs, adding missing memberships and
removing stale ones. Historical slugs are derived from each historical revision's
own text. Listing queries still filter to visible current revisions.

Revisions are read in ID order in batches of 100, bounded by the maximum revision ID
at startup. Each applied batch uses a SQLite immediate transaction and FULL
synchronous durability. The writer lock protects the batch's source text while it
is parsed and reconciled; ordinary app writes can briefly wait for that batch.
Cancellation or failure rolls back the current batch; previous batches remain
committed. Rerunning reconciles the full range safely without duplicate rows. Rows
created after the initial upper bound are handled by save-time indexing or another
backfill if an external writer created them. Dry-run totals are informational and
may change if writers are active.

For production, run the same maintenance entry point from a release containing this
code, against the mounted database, after taking a consistent backup. Deploy the
save-time indexing code before backfilling so normal future edits stay indexed.
The command does not create the table or deploy the website. Containers must mount
the existing database directory with the normal application write permissions;
invoke the executable directly because the chiseled runtime has no shell.

Development backfill on 2026-10-22 scanned 597 revisions and added 1,439 memberships.
A repeat dry run reported zero changes. Production has not been backfilled by this step.

## Verification

`dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~ArticleCategoryMembershipSchemaTests`

`dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~CategoryMembershipSaveTests`

`dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~CategoryMembershipBackfillTests`

Tests exercise EF-created, fresh SQL, and upgraded SQLite schemas: existing article
preservation, EF writes/reads, duplicate/null/orphan rejection, indexed category
lookup, membership retention on soft deletion, and cascading physical deletion.
