# Download URL notes foreign-key repair

`DownloadUrlNotes.DownloadUrlId` must reference `DownloadUrls.Id`, with cascade
deletion. Both development and production previously referenced the nonexistent
`DownloadUrls_old` table. The historical copyright/status migration creates the
notes table before renaming and replacing its parent; SQLite rewrites the existing
foreign key during that rename. The stale reference prevents note insertion and
can also prevent physical user deletion through the notes table's other cascade.
An empty notes table allows `foreign_key_check` to pass despite the defect.
`MigrateDownloadUrlsCopyrightStatus.sql` now creates the notes table after
replacing the parent, preventing this defect on future executions of that upgrade.

## Application and schema procedure

Schema ownership remains manual SQL; follow [database maintenance](database-maintenance.md).
Fresh databases use the corrected `SqliteScripts/Create.sql`, which also matches
the deployed non-STRICT tables, integer article/file type columns, and column
order after the previously applied additive upgrades. These creation-script
corrections do not alter existing databases. Existing databases
use these three scripts from `src/WikiWikiWorld.Data/SqliteScripts/`:

- `CheckDownloadUrlNotesForeignKeyBefore.sql`
- `RepairDownloadUrlNotesForeignKey.sql`
- `VerifyDownloadUrlNotesForeignKey.sql`

The precheck requires the legacy relationship, valid existing parents, the known
notes schema/index, and no incoming foreign keys or extra indexes/triggers. A
failed precheck requires investigation, not bypassing it. Apply once:

```bash
/home/deploy/db-maintenance/db-update.sh production /opt/wiki/data/app.db \
  /home/deploy/db-maintenance/RepairDownloadUrlNotesForeignKey.sql \
  /home/deploy/db-maintenance/VerifyDownloadUrlNotesForeignKey.sql \
  /home/deploy/db-maintenance/CheckDownloadUrlNotesForeignKeyBefore.sql
```

For development on Windows, use the same runner implementation:

```powershell
python -B deploy/db_update.py development data/app.db `
  src/WikiWikiWorld.Data/SqliteScripts/RepairDownloadUrlNotesForeignKey.sql `
  src/WikiWikiWorld.Data/SqliteScripts/VerifyDownloadUrlNotesForeignKey.sql `
  --precheck src/WikiWikiWorld.Data/SqliteScripts/CheckDownloadUrlNotesForeignKeyBefore.sql
```

The migration rebuilds only the child table inside one `BEGIN IMMEDIATE`
transaction, with foreign keys enabled. It checks every copied column before
replacing the old table, recreates the index, and retains AUTOINCREMENT history,
including IDs of previously deleted notes. Invalid existing references abort
the copy and roll back the transaction. No other table's contents or schema is
changed. The table remains non-STRICT to preserve its established contract.

The running application is compatible: table/column names and types stay the
same, and the corrected relationship implements the intended behavior. Writes
serialize behind the short transaction; an application release or restart is not
required. Afterward, compare table contents/schema with the maintenance backup,
run the supplied assertions, and check development/production HTTP responses.
Test note insertion, orphan rejection and both cascades on a disposable backup
copy, not by adding/deleting accounts or downloads in the live database.

The runner records a consistent backup before changing the schema. A failed
uncommitted transaction rolls back when its connection closes; committed changes
are not automatically restored. Restoring a backup requires stopped writers and
a separate recovery decision because it discards later writes. Retain the
operation backups until their recovery purpose has ended.

## Verification

```powershell
python -B -m unittest discover -s deploy -p 'test*.py' -v
```

The repair tests use the full fresh application schema and reconstructed legacy
schemas. They cover empty and populated repairs, Unicode note preservation,
deleted identity history, backup contents, both cascades, orphan rejection,
rollback, repeat refusal, and fresh-database behavior. The historical upgrade
regression checks that its statement order no longer creates the defective
relationship. Run these tests in the documented production maintenance image
as UID/GID 1654 before application.

## Applied operation: 2026-11-07

The repair was applied with the companion precheck and verification through the
documented maintenance runner, under the user's instruction to fix both databases:

| Environment | Database | Backup | Application time (UTC) |
| --- | --- | --- | --- |
| Development | `E:/source/WikiWikiWorld/data/app.db` | `E:/source/WikiWikiWorld/data/.db-backups/app.db-20261107T180339Z-nt3f7b9y.db` | 18:03:39 |
| Production | `/opt/wiki/data/app.db` | `/opt/wiki/data/.db-backups/app.db-20261107T180352Z-0hm869yo.db` | 18:03:52 |

Production SQL and test files are staged under
`/home/deploy/db-maintenance/download-url-notes-20261107/`; the operation used the
three scripts in its `src/WikiWikiWorld.Data/SqliteScripts/` directory and the
existing `/home/deploy/db-maintenance/db-update.sh` runner. The runner files matched
the repository hashes. The maintenance image was the installed `python:3.14-slim`,
image ID `sha256:0b566a9f30a60730df33c26ac62dedb55393d8814645cd03543b67bf8987f3a1`.
The production backup is owned by UID/GID 1654 with mode 600. Retain both backups.

Before live application, the repair passed against consistent disposable copies
of both actual databases. After application, every row in all 26 tables and every
`sqlite_sequence` entry matched the respective maintenance backup. The only schema
change was the corrected notes foreign key; both live schemas matched fresh
`Create.sql` databases. Both notes tables retained their existing count of zero.
The runner's integrity, foreign-key and verification assertions passed in both
environments, and the legacy precheck correctly became false after the repair.

Both development and production home pages returned HTTPS 200. The existing
production container remained running; no image deployment or restart was needed.
No synthetic notes, accounts or downloads were added to the live databases.

Verification: 14 local Python maintenance tests and 11 existing .NET schema tests
passed. All five regression tests passed in the production maintenance image;
the four covering the live repair passed before application, and the test for
the corrected historical migration passed after that source change. The .NET
tests used `BuildProjectReferences=false` to reuse the
running development application's existing build while loading the updated SQL;
no C# source was changed. Existing ImageSharp audit warnings remained unchanged.
