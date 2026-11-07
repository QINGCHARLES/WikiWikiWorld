# Private messaging

Private messages use separate tables in the same SQLite database. Public article
and user-page discussions remain in `ArticleTalkSubjects` / `ArticleTalkSubjectPosts`
and are explicitly labelled public.

## Behavior and authorization

- `/message/new` starts a conversation; `/@username/message/new` prefills editable
  recipients. Enter up to 20 other usernames separated by commas. Identity resolves
  names case-insensitively; duplicate accounts are collapsed and the sender is included.
- Participants are fixed at creation. Everyone listed can read and reply. There is
  no member management, edit/delete, read-receipt or per-user archive state yet.
- `/message/{guid}` GET and reply POST require a live authenticated account and
  participant membership in the resolved site **and culture**. Unknown IDs, outsiders
  and foreign scopes return 404. Operators have no membership override.
- `/@username/messages` is the owner's inbox; `/sent` lists conversations they
  started. Replies do not move conversations between folders. Folders order by the
  latest message. Folders/threads return at most 50 rows per page; threads open on
  the latest chronological page. Pagination uses lowercase `pagenumber`.
- Subjects are encoded plain text. Bodies use the restricted discussion Markdown
  pipeline: raw HTML disabled, no article directives/event attributes, and a
  `data-ignore` rendering container. Forms work without JavaScript; autocomplete
  enhances the final comma-separated recipient token.
- Private pages use `no-store,no-cache` and `noindex`. Razor antiforgery protects
  writes. Sender/site/culture come from the authenticated request, not form fields.

`PrivateMessagingService` authorizes before loading bodies and rechecks membership
in reply transactions. Creation saves the conversation, participants and first
message atomically. The existing SQLite execution strategy owns lock retries;
failed private writes detach rolled-back entities before retry. Private rows never
enter public profile/activity/search queries, and sending never provisions articles.

## Schema and cleanup

`PrivateConversations` owns site, culture, subject, creator and creation time.
`PrivateConversationParticipants` has primary key `(ConversationId, UserId)`.
`PrivateMessages` has a composite foreign key `(ConversationId, AuthorUserId)` to
membership, enforcing member authorship even for direct inserts. Message IDs
provide stable ordering and anchors. Account foreign keys restrict physical account
deletion; existing account soft deletion remains available.

The old inbox/sent talk queries, recipient-article inference and three sole-use
specifications are removed: `ArticleTalkSubjectsByCreatorSpec`,
`ArticleTalkSubjectsByCanonicalIdSpec`, `ArticleRevisionsByCanonicalIdsSpec`.
Unused subject `HasBeenEdited` / `DateModified` columns and model references are
removed. Post edit metadata is still rendered; public soft-delete fields, author
relationships and article association remain. Historical pre-cutover migrations
retain their original contracts: do not replay `MigrateToEfCore_Verified.sql` or
`NormalizeTimestamps.sql` against the replacement schema.

## Existing-database cutover

Schema ownership remains manual SQL. Fresh databases use `Create.sql`; existing
databases use `ReplacePrivateMessaging.sql` with `CheckPrivateMessagingBefore.sql`
and `VerifyPrivateMessaging.sql` through the [maintenance runner](database-maintenance.md).
The runner makes a consistent backup and checks integrity/foreign keys. The SQL
owns one `BEGIN IMMEDIATE` transaction and intentionally fails if reapplied.
Verification asserts an empty initial cutover, not ongoing message-table emptiness.

**Stop the old application and other writers first.** Removing subject columns is
incompatible with the old writer. Prepare the new release, stop writers, apply the
SQL, then start the new release. There are no automatic startup migrations.

The 2026-11-06 inventory found zero subjects/posts, including deleted rows, in both
development `E:/source/WikiWikiWorld/data/app.db` and production `/opt/wiki/data/app.db`.
No legacy migration or deletion was needed. Both the precheck and in-transaction
guard require that this remains true. If rows have appeared, stop and classify
them before a separately reviewed private-message purge. Never indiscriminately
delete public discussions or whole recovery archives. Inspected database copies
were empty too; scheduled archive/cloud contents were not inspected.

After staging the SQL files and stopping writers:

```bash
/home/deploy/db-maintenance/db-update.sh production /opt/wiki/data/app.db \
  /home/deploy/db-maintenance/ReplacePrivateMessaging.sql \
  /home/deploy/db-maintenance/VerifyPrivateMessaging.sql \
  /home/deploy/db-maintenance/CheckPrivateMessagingBefore.sql
```

Rollback requires stopped writers and a deliberate restore of the recorded backup
together with the old application. Restoring loses subsequent writes; prepare a
reviewed forward repair if those writes must be preserved. Never restart the old
release against the trimmed schema. No automatic restore/down-migration is provided.

## Verification

```powershell
dotnet test tests/WikiWikiWorld.Web.Tests -c Release --no-restore
node --test tests/browser/recipient-autocomplete.test.cjs tests/browser/admin-article-picker.test.cjs
```

SQLite tests cover multi-recipient membership, outsiders, scope filters, atomic
invalid/canceled writes, paging, Razor handlers, caching, and EF/fresh SQL/upgraded
schemas. Constraints reject nonmember authors and duplicate membership. Populated
public discussions abort cutover without losing rows or changing their schema.

Authenticated Chromium checks on 2026-11-06 used four synthetic accounts and a
fresh disposable database on development ports 17443/17080. They verified both
recipients, inbox links, native compose/reply without JavaScript, subject/body
encoding, cache headers, outsider GET/POST 404s, antiforgery 400s, no private bodies
on public talk pages, and a 390px-wide conversation without horizontal overflow.
These were not production, screen-reader or other-engine checks. The full .NET
suite passed 296 cases; the two script suites passed 27. Existing ImageSharp
NuGet vulnerability warnings remain outside this messaging change.

## Operation status

- Development cutover applied through `deploy/db_update.py` on 2026-11-06 at
  23:36 UTC to `E:/source/WikiWikiWorld/data/app.db`, with no application listener
  on the usual development/preview ports. Backup:
  `E:/source/WikiWikiWorld/data/.db-backups/app.db-20261106T233639Z-mjiff210.db`.
  Precheck, integrity, foreign keys and cutover verification passed. All 23 existing
  application tables retained their row counts; the three new private tables are
  empty. No legacy messages existed to delete. The separate development upload
  staging migration was subsequently applied at 23:59 UTC before release activation;
  see [its operation record](upload-recovery.md#development-schema-operation-2026-11-06).
- Production cutover/deployment has not been performed. The old release must not
  run against this trimmed schema; coordinate schema and release activation.
