# Database maintenance

Schema ownership remains reviewed, manually applied SQL. Use `deploy/db-update.sh`
on the Linux Docker host for schema changes and deliberate data repairs. This is
not an automatic startup migration system. Use application services/API for content
edits so revisions, category indexing and other application rules are applied.

## Agent workflow

1. Read the feature's schema procedure and inspect the current schema. Prepare the
   migration, an optional precheck, and a required verification query. Review whether
   the currently running application is compatible with the change.
2. Test on disposable representative data, including existing rows. For changes
   incompatible with the running app, arrange a maintenance window and stop writers
   before backup/application; the command does not stop or restart the app.
3. Follow production authorization already given for the task. If production changes
   are not authorized, obtain approval for the prepared change before applying it.
4. Run the command once. On failure, inspect the error and actual database state;
   do not improvise ownership changes, another container image, or another write path.
5. Report the environment, database path, backup path, SQL file and verification
   outcome. Check the affected application behavior after deployment.

## Host setup and command

The documented production host is reached with `ssh magazedia`. Its database is
`/opt/wiki/data/app.db`, owned by `1654:1654`. The deploy user already has Docker
access. No new sudo rule or group-write permission is needed.

Copy `deploy/db-update.sh`, `deploy/db_update.py` and the reviewed SQL/check files
to the host (keep the two runner files together). For example, from the repository
root in PowerShell, stage the runner without executing a database update:

```powershell
ssh magazedia 'mkdir -p /home/deploy/db-maintenance'
scp deploy/db-update.sh deploy/db_update.py magazedia:/home/deploy/db-maintenance/
ssh magazedia 'chmod 755 /home/deploy/db-maintenance/db-update.sh'
```

The standard maintenance image is `python:3.14-slim`, the image previously used on
this host. Provision it with `docker pull python:3.14-slim` if absent. The runner
uses `--pull=never`, so an update does not silently download a different runtime.
It runs as `1654:1654` with networking disabled, mounts SQL/runner files read-only,
and mounts the whole database directory for SQLite WAL/journal access. Staged runner
and SQL files must be readable by UID 1654 (normally mode 644).

After staging the SQL and obtaining any required production authorization:

```bash
/home/deploy/db-maintenance/db-update.sh production \
  /opt/wiki/data/app.db \
  /home/deploy/db-maintenance/update.sql \
  /home/deploy/db-maintenance/verify.sql \
  /home/deploy/db-maintenance/precheck.sql
```

Arguments are `production|development|test DATABASE SQL VERIFY_SQL [PRECHECK_SQL]`.
Paths must exist. The environment is a required label, not host auto-detection;
check the host and database path before running. For a Linux test fixture, make its
directory writable by UID 1654 and use the same command with `test`.

## SQL and checks

Migration files contain SQLite SQL, not sqlite3 CLI dot commands. They own their
transaction boundaries: use `BEGIN IMMEDIATE; ... COMMIT;` for an atomic update,
as the existing additive scripts do. The runner enables foreign keys and FULL
synchronous durability before execution, without changing journal mode. It stops
at the first SQL error. An open transaction is rolled back on connection close;
earlier explicit commits are not undone. A missing COMMIT is an error.

Precheck and verification files each contain **one read-only query returning exactly
one row and one column equal to integer `1`**. A query printing a schema or returning
no rows is not an assertion. These queries run with `query_only` enabled. For example,
before adding bookmark visibility:

```sql
SELECT NOT EXISTS (
    SELECT 1 FROM pragma_table_info('BookmarkLists') WHERE name = 'IsPublic'
);
```

Afterward, a verification query can assert the column and index exist:

```sql
SELECT EXISTS (
    SELECT 1 FROM pragma_table_info('BookmarkLists')
    WHERE name = 'IsPublic' AND type = 'INTEGER' AND "notnull" = 1 AND dflt_value = '0'
) AND EXISTS (
    SELECT 1 FROM pragma_index_list('Bookmarks')
    WHERE name = 'IX_Bookmarks_List_Recent_Active'
);
```

Include relevant data-preservation assertions in the prepared query. Preconditions
are operator checks, not locks against app writes; constraints and the migration's
transaction must enforce requirements that matter during concurrent writes.

## Backups, failures and recovery

Before any migration SQL, the runner creates a consistent SQLite backup through
the backup API, checks its integrity, and prints its path. Production backups live
at `/opt/wiki/data/.db-backups/app.db-<UTC timestamp>-<unique suffix>.db`; the printed
container path `/data/.db-backups/...` maps to that host directory. New backup files
are mode 600 and the folder is mode 700, owned by UID 1654. This directory is outside
the publicly served `sitefiles` and `.well-known` paths. No database ownership changes
are made. These local maintenance snapshots supplement the existing scheduled backup;
they are not automatically rotated or included in its archive. Retain/delete them
deliberately according to the operation's recovery needs and available disk space.

After SQL execution, success requires `integrity_check`, `foreign_key_check`, and
the supplied verification query to pass. Any failure returns nonzero; the command
never restores automatically. Verification occurs after SQL commits, so a failure
can mean the update was applied but its verification failed. Inspect before retrying;
existing additive scripts intentionally fail if reapplied. An application rollback
can usually retain additive schema. Restoring a backup loses subsequent writes and
requires a deliberate recovery decision with writers stopped.

## Local verification

The Python runner is also usable directly on Windows for disposable test databases
and authorized development updates, using the current user's file permissions:

```powershell
python deploy/db_update.py test C:/Temp/fixture/app.db update.sql verify.sql --precheck precheck.sql
python -B -m unittest discover -s deploy -p test_db_update.py -v
```

Python 3.12+ suffices for these tests. Production uses the documented container.
Runner tests exercise WAL backup contents, transaction rollback, verification failure,
prechecks, missing files, and the actual bookmark migration. On a Docker host, repeat
the fixture test through the shell wrapper before first production use.

Verified 2026-10-24: nine local runner tests passed. An isolated `/tmp` fixture on
the documented Docker host also passed through `db-update.sh` using the installed
`python:3.14-slim` image (image ID
`sha256:0b566a9f30a60730df33c26ac62dedb55393d8814645cd03543b67bf8987f3a1`).
The smoke test checked migration success, rejection by a failed precheck, backup
contents, UID 1654 ownership and mode 600. The temporary fixture was removed; no
production database update or persistent runner installation was performed.

Implementation references: [Python sqlite3 backup and transaction APIs](https://docs.python.org/3/library/sqlite3.html)
and [Docker run options](https://docs.docker.com/reference/cli/docker/container/run/).
