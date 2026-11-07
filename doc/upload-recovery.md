# Revision-specific upload recovery

Implemented 2026-11-06 for the Razor article create/replacement handlers and the API article create/file replacement handlers. The additive production schema upgrade was applied on 2026-11-06 at 19:32 UTC, followed by application deployment/restart at 20:02 UTC. Production HTTPS/browser checks and an isolated actual-image upload/restart-recovery check passed. Existing development databases have not been upgraded. Applying the change to any other existing database remains an explicit maintenance operation.

## Storage and commit contract

Before opening the metadata transaction, `FileUploadStorageService` stages bytes at:

```text
sitefiles/{SiteId}/images/_staging/v2/{CanonicalFileId:D}.{StagingUploadId:N}{extension}.tmp
```

Every writer saves the same `StagingUploadId` on its new `FileRevision`, inside the transaction creating that revision. A partial unique index prevents two revisions from claiming the same non-null identifier, including deleted revisions. The identifier is internal metadata excluded from JSON and retained after finalization. Existing rows have a null identifier.

Only after commit are bytes moved into the established served layout:

```text
sitefiles/{SiteId}/images/{CanonicalFileId}/{RevisionId}{extension}
```

URLs and final paths are unchanged. Moves never overwrite an existing destination. Repeating a completed move is harmless if staging is absent and the final file exists. If both staging and destination remain, automatic recovery retains staging and reports a conflict rather than deciding which bytes to discard.

If copying into staging fails or is cancelled, the partial file is removed before any caller can commit its identifier. Later catch-path cleanup checks the database for the exact identifier before deleting anything; a committed or soft-deleted revision protects its staging, even when a request reports an uncertain outcome.

## Startup recovery and retention

Startup enumerates staged files rather than scanning only current file revisions. Each v2 file is matched by identifier, canonical file, extension, and an article association on the physical site. The association check includes historical and deleted article rows; soft deletion must not erase the evidence of the site's ownership.

- A visible committed file revision is finalized even if it is historical.
- A soft-deleted file revision retains its staged bytes and reports the claim; recovery does not introduce a new deleted-content retention policy.
- An unclaimed, well-formed v2 attempt is removed only after `FileStorage:StaleTempFileAgeMinutes` (default 60). Recent unclaimed attempts are left in place.
- A mismatched claim, malformed filename, missing site association, or filesystem failure retains staging and reports the path and reason.
- Pre-upgrade files directly under `_staging` are retained and reported regardless of age. Their random suffix was never committed with a revision, so the app cannot safely assign ownership from modification times.

`Program.cs` logs retained-file issues as structured warnings, including the staging path and reason. It does not log uploaded content. Resolve permission/disk/conflict problems and retry startup recovery after preserving the affected files. A database/schema error still fails startup rather than running cleanup without the ability to check claims.

For legacy staging, preserve a copy and inspect revision metadata and any independent upload records. A single matching canonical ID/extension is not proof that the bytes belong to a particular revision. This change intentionally provides no automatic legacy reassignment/deletion tool. Already-lost bytes cannot be reconstructed by this fix.

## Existing-database upgrade

Follow [database maintenance](database-maintenance.md), including its consistent backup and verification requirements. There is no startup migration. Use the three reviewed scripts under `src/WikiWikiWorld.Data/SqliteScripts/`:

1. `CheckFileRevisionStagingUploadIdBefore.sql`: requires the existing table and absence of the column/index.
2. `AddFileRevisionStagingUploadId.sql`: adds nullable `StagingUploadId TEXT` and its partial unique index in one transaction.
3. `VerifyFileRevisionStagingUploadId.sql`: checks the column, index, and foreign-key integrity using the runner's one-row/one-cell integer assertion contract.

For an authorized host operation, after staging the scripts in the documented maintenance directory:

```bash
/home/deploy/db-maintenance/db-update.sh production \
  /opt/wiki/data/app.db \
  /home/deploy/db-maintenance/AddFileRevisionStagingUploadId.sql \
  /home/deploy/db-maintenance/VerifyFileRevisionStagingUploadId.sql \
  /home/deploy/db-maintenance/CheckFileRevisionStagingUploadIdBefore.sql
```

Apply the additive schema before deploying the new binary. Preserve all existing staging before the upgrade and reconcile legacy warnings afterward. The older binary can use the additive schema, but creates legacy-format uploads; it cannot populate the new claim. `Create.sql` already includes the new column/index for fresh databases.

For rollback, retain the additive column and index. New-format bytes live under `_staging/v2`; the previous implementation only matches/deletes files directly under `_staging`, so it does not claim or age-delete v2 files. It also cannot recover them: preserve those files until running the new recovery code again. Existing legacy staging remains subject to the old binary's unsafe recovery rules, so preserve it before starting an older binary. No schema downgrade or byte deletion is needed for a binary rollback.

## Production schema operation: 2026-11-06

Authorized separately from application deployment and applied at 19:32:28 UTC.

- Host/environment: `ssh magazedia`, production database `/opt/wiki/data/app.db`.
- Applied `/home/deploy/db-maintenance/upload-recovery-20261106-G0duoP/AddFileRevisionStagingUploadId.sql` using the installed `db-update.sh`, with the matching `CheckFileRevisionStagingUploadIdBefore.sql` and `VerifyFileRevisionStagingUploadId.sql` from that directory. The installed runner and staged SQL hashes matched the repository files. The existing pinned maintenance image was used, without a pull.
- Consistent pre-upgrade backup: `/opt/wiki/data/.db-backups/app.db-20261106T193228Z-rfzljuoq.db` (3,944,448 bytes, UID/GID 1654, mode 600). Retain this snapshot; restoring it requires a separate recovery decision because later writes would be lost.
- Before applying, the exact SQL passed on an in-memory backup of the live database. No pending files were present in the two existing staging directories.
- The runner passed database integrity, foreign-key integrity, and the supplied schema/index verification. An independent read-only comparison against the pre-upgrade backup confirmed all original values of all 186 file revisions were unchanged; the new column was null for every existing revision.
- The existing application container/image/start time stayed unchanged, with zero restarts. The HTTPS homepage returned HTTP 200 before and after. No application deployment, image push, upload, or content write was performed.

The production schema step is complete; do not reapply the additive SQL there. Application deployment and runtime verification were subsequently completed as recorded below. For a binary rollback, retain the additive column and index as described above.

## Production application release: 2026-11-06

Published and deployed the current working tree at 20:02:33 UTC, after separate authorization to publish, deploy, and restart.

- Release: `release-20261106T195612Z-8f6705dfc19f`, also published as `latest`.
- Exact deployed reference: `ghcr.io/qingcharles/wikiwikiworld@sha256:476fae9d7634fd6df28b334cd52c72ae1a88d6606dea2cae3f3a0685e5cf9a48`.
- The tag includes the base Git commit; it represents the prepared working tree, including uncommitted changes, rather than that commit alone. Archive SHA256: `455ab05f26364a9c81c144bccc701ab1ab2c7e4b472dd1bf265fb68dee90612d`.
- Built with SDK container publishing for `linux-x64`, `noble-chiseled-extra`, and full globalization. Because this workstation had no Docker/registry login, the SDK produced an archive; its checksum was checked on the server, then the host's existing Docker login published it to GHCR. No credentials were copied between machines. The immutable release was pulled successfully before replacing production.
- The actual image ran as UID 1654 on loopback-only test ports using a consistent copied database and copied uploads. An authenticated API replacement upload saved a non-null staging claim, correct 2x2 PNG dimensions, and exact bytes. With that test container stopped, its final bytes were moved back to the matching staging path to model interrupted finalization; restart recovered the exact bytes successfully. These writes occurred only on the isolated copy.
- Before release, all 274 Release application tests and 25 JavaScript tests passed. Existing ImageSharp advisory warnings remained; no warning settings or dependencies were changed for deployment.
- `/home/deploy/update.sh` deployed the digest above, recreating the production container with the established data/certificate mounts. It is running without automatic restarts. HTTPS checks passed for the main/culture sites, search, the helper, an existing file article and API metadata, and login redirects for protected pages. The existing SVG still served its original 3,911 bytes. Production database integrity and foreign keys passed with 186 file revisions and no smoke-test revision.
- External Chromium checks passed for the homepage, helper, and existing file article with loaded images and no page errors, plus the homepage without JavaScript. Authenticated recipient/admin/editor browser scenarios were covered by the earlier isolated script/handler checks, not repeated against production. No production messages, redirects, user articles, or uploads were created for verification.
- The temporary smoke container, copied data/uploads, and host transfer/test files were removed. Host audit metadata remains at `/home/deploy/releases/release-20261106T195612Z-8f6705dfc19f/release.json`; the archive/checksum remains under the Git-ignored local `.local/releases/` directory.

The previous image is retained locally on the host as `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261106T195612Z` (image ID `sha256:f6631dba800255d7ac9fb5d4a1a5325b1b73bd2a5dad231c749a64f5737b028c`). If rollback is needed, use the documented update script with that image and preserve staging according to the rollback guidance above; retain the additive database schema.

## Development schema operation: 2026-11-06

Applied the previously pending additive upload-staging migration to
`E:/source/WikiWikiWorld/data/app.db` on 2026-11-06 at 23:59 UTC before activating
the combined release. The standard `deploy/db_update.py` runner applied
`AddFileRevisionStagingUploadId.sql` with its precheck and verification. Backup:
`E:/source/WikiWikiWorld/data/.db-backups/app.db-20261106T235922Z-rs0g_cpr.db`.
Integrity, foreign keys and schema verification passed. This complements the
already-applied private-messaging development cutover; neither migration runs
automatically at application startup.

## Verification

```powershell
dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter 'FullyQualifiedName~FileUploadRecoveryTests|FullyQualifiedName~FileUploadSchemaTests'
dotnet test tests/WikiWikiWorld.Web.Tests -c Release
```

The 25 focused cases use real SQLite and disposable filesystem roots. They reproduce both original failures; test normal and failed post-commit finalization in all four upload entry points; restart recovery with a fresh context; check historical byte contents, conflicting final paths, repeatability, rolled-back/recent/legacy staging, deleted claims, context mismatches, partial-copy failure, cancellation, and JSON compatibility; and exercise EF, fresh SQL, and populated existing-schema upgrades, including duplicate-claim rejection.

At initial source verification, all 260 Release tests passed on Windows with .NET SDK 10.0.401; the combined suite subsequently passed all 274 tests after the other state-machine fixes. Existing ImageSharp advisory warnings remain. At initial source verification, no existing database had been migrated. The production schema operation and application release above were completed afterward; existing development databases were not upgraded. No physical power-loss or operating-system crash was injected. The filesystem tests model an interrupted upload by retaining committed staging and creating actual move conflicts.
