# Plan to fix the state-machine findings

Prepared 2026-11-06. All four workstreams are implemented and tested in source: upload recovery, recipient autocomplete, the admin redirect picker, and the user-page editor fallback. The upload production schema upgrade was applied on 2026-11-06 at 19:32 UTC; all four workstreams were deployed at 20:02 UTC as `release-20261106T195612Z-8f6705dfc19f`, with production checks passed. Existing development database upgrades remain separate. Combined verification: 274 Release application tests, 25 JavaScript regression tests, and isolated Chromium checks. See [upload recovery implementation, verification, and rollout](upload-recovery.md) and [browser test coverage](../tests/browser/README.md).

## How many bugs?

The previous summary grouped four findings into three affected areas:

| Finding | Area | Problem | Evidence |
| --- | --- | --- | --- |
| F01 | Upload recovery | Recovery could substitute bytes and overlooked superseded revisions. | Both failure scenarios reproduced; fixed and covered by SQLite/filesystem regression tests. |
| F02 | Recipient autocomplete | Clearing or changing the query can allow an older search to repopulate suggestions. | Reproduced with the actual script in an isolated Node harness. |
| F03 | Recipient autocomplete | Arrow navigation with an empty result list throws. | Reproduced with the same harness. |
| F04 | Admin redirect picker | Old article-search or culture responses can overwrite newer context. | Reproduced with deliberately reordered responses; fixed and covered by 13 script tests and Chromium fixture checks. |

There is also **F05, a separate editor fallback defect**: the intended missing-user-page creation branch cannot run. A nonempty slug makes `IsEditMode` true, the missing article returns 404, and the later branch requires both a nonempty slug and `!IsEditMode`. The article viewer directs an owner to this editor when automatic user-page creation fails. That fallback therefore leads to another 404 rather than a creation form. This failure was reproduced through real Razor handlers and SQLite. The repaired viewer-to-editor path is covered by a provisioning-save fault-injection test.

That makes **five identified defects across four workstreams**. All five findings have now been reproduced directly and fixed in source; handler and isolated-browser checks do not constitute a production or authenticated full-app browser test. The inventory's concerns about retry/tracker behavior and current-revision uniqueness remain investigation items, not additional confirmed bugs. The inventory was not a comprehensive bug audit of every workflow.

## 1. Make upload recovery identify the exact revision

**Implementation status:** complete in source, verified by 25 focused tests and the full 260-test Release suite. Staging uses the versioned `_staging/v2` directory so older cleanup code cannot remove new-format uploads during binary rollback. The production schema upgrade was applied on 2026-11-06 at 19:32 UTC with backup and verification. The application was deployed/restarted at 20:02 UTC; actual-image isolated upload/restart recovery and production checks passed. Existing development database upgrades remain separate. See [the production operation record](upload-recovery.md#production-schema-operation-2026-11-06). The original agreed plan follows for reference.

**Priority: highest, because the failure path can lose historical upload bytes or associate the wrong bytes with metadata.**

### Desired behavior

An upload moves through staged → metadata committed → finalized. After an interruption, recovery either finds the exact committed revision's bytes or reports that it cannot safely do so. Becoming historical does not make an upload disposable.

### Proposed implementation

1. First reproduce the existing failure using a disposable SQLite database and temporary files: commit upload A, leave its bytes staged, commit upload B for the same canonical file, and run recovery. Also test a newer uncommitted staging candidate with the same extension.
2. Give each staged upload an identifier and persist that identifier on its `FileRevision` in the same transaction that creates the revision. The proposed minimum schema change is a nullable `StagingUploadId` column. It is internal recovery metadata, so preserve existing external JSON/API contracts.
3. Use a distinguishable, versioned staging filename containing this identifier. Recovery must match the identifier, canonical file, extension, and staging-site context; modification time must not decide ownership.
4. Update every file-creation entry point: Razor article create, Razor replacement upload, API article create with an attachment, and API file replacement. A fix in the storage service alone would leave some writers without the recovery identifier.
5. Recover committed historical revisions as well as current ones. Cleanup must recognize a committed claim independently of `IsCurrent`. Do not let ordinary staging cleanup implicitly change the existing deleted-content retention policy.
6. Make finalization safe to repeat. An existing final destination must be preserved; reconcile or report an unexpected duplicate rather than overwrite it blindly. An interrupted move should leave either recoverable staging or the final file.
7. Handle pre-upgrade staging explicitly. Existing files have no durable revision claim, so do not automatically assign them to the newest revision or delete them under the new rules. Retain and report them for reconciliation; already-finalized historical files remain usable.
8. Update the model, EF mapping, fresh-database SQL, an additive manual SQL upgrade, project references, and the recovery runbook together. Do not add startup migrations, background queues, a generic storage abstraction, or a state-machine package.

### Verification

Use real SQLite plus a temporary filesystem and compare the actual byte contents, not just file existence:

- Normal create and replacement through all four entry points.
- Commit succeeds but finalization fails; restart recovery places the exact bytes at the correct revision path.
- A becomes historical before recovery; both A and B retain their own bytes.
- Multiple staging candidates and an uncommitted candidate cannot be substituted for a committed revision.
- Repeated recovery and an already-existing destination do not overwrite valid data.
- Rolled-back uploads become eligible for normal age-based cleanup only when their new-format identity has no committed claim.
- Legacy staging remains retained and is reported as unresolved.
- Failure/cancellation during staging, commit, and finalization preserves the correct cleanup boundary.
- Upgrade a representative existing schema with the manual SQL, verify old rows still work, then exercise new uploads.

### Rollout

Prepare and test the additive SQL first. Existing rows can retain a null identifier. Apply schema changes through the documented manual procedure before deploying code that expects the column. Preserve unresolved staging and verify pending uploads before activating cleanup. Production application/deployment is a later approval step after the patch, migration, and checks are reviewable.

Rollback must account for older binaries' startup recovery: they must not run their old cleanup rules against pending new-format staging without a preservation/compatibility procedure. Leaving an additive nullable column in place is preferable to a destructive schema rollback.

**Done when:** restart recovery is deterministic per revision; historical pending bytes are preserved; cleanup cannot discard a committed upload; legacy ambiguity is visible and retained.

## 2. Fix both recipient-autocomplete defects together

**Implementation status:** complete in source. Twelve deterministic JavaScript tests pass, Chromium fixture checks cover keyboard/pointer/touch and JavaScript-disabled form submission, and all 260 Release application tests pass. The fix stays in the compose page's existing inline script; no schema changes or deployment were needed. The original agreed plan follows for reference.

**Priority: small, independently shippable fix.**

### Desired behavior

Only results for the current, still-active recipient search may appear. Empty lists have no selected item, and keyboard handling never indexes an absent option.

### Proposed implementation

1. Cancel the debounce before handling any new input, including an empty value.
2. Abort the previous fetch and invalidate its request generation immediately when input changes. Accept results only if their generation/query still matches the current search. Cancellation alone is insufficient because a response callback may already be queued.
3. Dismissal, recipient selection, and clearing invalidate pending work so a late response cannot reopen the list. Replace suggestions rather than append results from multiple requests.
4. Keep `currentFocus` at -1 when no option is active. Guard empty collections, normalize selection after a result replacement, and handle Enter only when the selected option exists. Use `Event.key` for the keys already supported.
5. Keep this local to recipient autocomplete, preserving the ordinary form submission. Do not introduce a general autocomplete framework or rewrite the page in Datastar for this fix.

### Verification

- Type then clear before the debounce and after a request starts: the list stays closed.
- Delay an older response until after a newer response: only the newer results remain.
- Dismiss or select while a request is pending: late results stay dismissed.
- Arrow Up/Down and Enter with zero results cause no exception or unwanted selection.
- Keyboard selection, pointer selection, result replacement, failed requests, and ordinary form submission still work.

Use a small deterministic script test for request ordering/selection, then verify actual keyboard and pointer behavior in Chromium. Avoid tests that merely check that an abort call or guard exists.

**Done when:** F02/F03 no longer reproduce and valid recipient selection still submits the selected username.

## 3. Prevent stale admin-picker responses

**Implementation status:** complete in source. Thirteen deterministic script tests pass, and Chromium checks verify reordered responses, keyboard selection, form values, clear/change behavior, and culture failure/retry. Three SQLite service cases confirm valid redirects succeed and wrong-site/wrong-culture targets remain refused. Culture controls and search stay unavailable after a failed culture lookup until retry or another site selection succeeds. No schema change. The original agreed plan follows for reference.

**Priority: independently shippable correctness fix.**

### Desired behavior

The selected site, culture options, search results, spinner, and selected article always describe the same current context.

### Proposed implementation

1. Track separate current requests for culture loading and article searching, using abort plus generation checks.
2. On site change, immediately invalidate culture/search requests, cancel the search timer, and clear the old target/results. Mark culture-dependent search unavailable until cultures for the current site arrive.
3. Apply a culture response only when it belongs to the current site and request generation. On culture change, immediately invalidate article search and selection.
4. Snapshot site, culture, and query for each search. Only its current matching response may render results or update pending/error UI.
5. Clearing a query, selecting an article, or clearing the selected target invalidates pending searches. Short queries and terminal failures clear the relevant spinner; old completions cannot clear a newer request's spinner.
6. Keep server-side site/culture/article validation as the final write contract. Preserve native submission and use lowercase query parameter names when touching generated requests.

### Verification

- Force site A's culture response to arrive after site B's; B's cultures remain selected.
- Force an older article result to arrive after a newer query, a culture/site change, a clear, or a selection; it cannot replace/reopen the current UI.
- Fail a culture fetch and an article fetch; controls recover without retaining an incompatible selection or stuck spinner.
- Create a valid redirect against disposable data; stale or manipulated submitted site/culture/article combinations are still refused by the server.

**Done when:** visible picker state stays consistent under deliberately reordered responses and existing server validation remains intact.

## 4. Repair the additional user-page creation fallback

**Implementation status:** complete in source. Eleven SQLite-backed handler cases cover the missing-owner form, viewer provisioning failure and successful manual fallback, validation-error draft/mode retention, existing user/ordinary edits, normal creation, file canonical routing, authentication, and ownership. Mode now follows `OriginalUrlSlug`; a prefilled creation slug does not imply edit. User-page creation forces the User type and excludes file attachments. Razor compiles in the Release suite; authenticated browser rendering of this fallback was not exercised. No schema change. The original agreed plan follows for reference.

**Separate small follow-up for F05, rather than hiding it in the autocomplete changes.**

1. Add a regression case for a signed-in owner whose account exists but whose user article is missing: the editor should offer creation instead of returning 404.
2. Resolve the requested slug and existing article first. An existing article means edit; a missing article that is the authenticated owner's own user slug means creation. An unrelated missing article retains its existing 404 behavior.
3. Derive the rendered mode and submitted `OriginalUrlSlug` from that resolved state, so a prefilled creation slug is not mistaken for an existing article. Preserve submitted text on validation errors.
4. Preserve ownership validation and force the User article type on POST. Verify a different user cannot create the target user's page through this fallback.
5. Test the viewer's failure redirect, direct editor entry, existing-user-page editing, anonymous access, another user's slug, ordinary article creation, and validation-error redisplay.

**Done when:** the documented fallback works without changing ordinary create/edit behavior or ownership rules.

## Delivery and completion checks

Use four focused changes corresponding to the workstreams above. The two frontend fixes can ship independently while the upload migration is prepared and tested. Preserve unrelated current working-tree changes.

For each change, add the behavioral regression checks described above, build on the existing .NET 10 target, run relevant tests first, then run the Release test suite once the combined changes are ready:

```powershell
dotnet test tests/WikiWikiWorld.Web.Tests -c Release
```

Verify affected browser flows with JavaScript enabled and disabled where the existing native form supports that fallback. Report remaining platform or fault-injection gaps explicitly. Update the inventory to link each resolved finding to its tests and implementation; do not mark a finding fixed merely because its happy path passes.
