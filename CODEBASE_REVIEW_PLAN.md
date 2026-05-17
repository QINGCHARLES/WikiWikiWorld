# Codebase Review Status

Last reviewed: 2026-05-14.

This document tracks cleanup and architecture findings for the WikiWikiWorld codebase. Magazedia is the current configured implementation, not the identity of the reusable platform. This replaces the older review snapshot with current status based on source inspection after the recent repository hygiene pass.

Per project decision, `HtmlPrettifyMiddleware` is intentionally retained for now and is not treated as an active cleanup target in this document.

## Current Assessment

The core shape remains reasonable:

- ASP.NET Core Razor Pages for the web UI.
- EF Core with SQLite for persistence.
- A revision-based wiki content model.
- Markdig extensions for domain-specific article markup.

The main remaining cleanup themes are:

- Make site/culture scoping explicit and predictable.
- Tighten file upload finalization and cleanup behavior.
- Simplify startup configuration in `Program.cs`.
- Keep build, runtime data, and documentation hygiene tight.

## Current Findings

### 1. Global tenant and culture filtering has explicit scoped and unscoped modes

Status: Resolved for current behavior.

Severity: Closed unless future admin tooling needs a different factory pattern.

`WikiWikiWorldDbContext` now distinguishes between normal site/culture-scoped contexts and intentional cross-site/cross-culture contexts.

Current behavior:

- Web request contexts use `SiteContextService`, which requires `SiteResolverService.ResolveSiteAndCulture()` to succeed.
- If a web request cannot resolve site or culture, context creation fails loudly.
- Query filters apply site and culture constraints unless `AllowCrossSiteAndCultureQueries` is explicitly true.
- Design-time/options-only contexts are intentionally unscoped for migrations and tooling.

Relevant files:

- `src/WikiWikiWorld.Data/WikiWikiWorldDbContext.cs`
- `src/WikiWikiWorld.Data/ISiteContextService.cs`
- `src/WikiWikiWorld.Web/Services/SiteContextService.cs`

Remaining follow-up:

- Future admin/tooling contexts that need cross-site/cross-culture behavior should provide an explicit `ISiteContextService` implementation with `AllowCrossSiteAndCultureQueries` set to true.

### 2. Culture-selector root-domain flow appears repaired

Status: Previously true, now likely resolved.

Severity: Closed unless runtime testing proves otherwise.

`BasePageModel` now has `AllowsCultureSelectorRootDomain`, and `CultureSelectModel` overrides it to true. That means the culture selector can use `ResolveSiteAndCultureWithRootCheck()` instead of the stricter `ResolveSiteAndCulture()` path.

Remaining validation:

- Run the app locally and verify the root-domain culture selector URL renders correctly.

Relevant files:

- `src/WikiWikiWorld.Web/Pages/BasePageModel.cs`
- `src/WikiWikiWorld.Web/Pages/CultureSelect.cshtml.cs`
- `src/WikiWikiWorld.Web/Services/SiteResolverService.cs`

### 3. Article revision write workflows now share one service

Status: Previously true, now resolved for edit, revert, and delete paths.

Severity: Closed for API and Razor Page edit/revert/delete workflows.

`ArticleRevisionService` now owns the shared transactional workflow for article revision state changes. The API controller and Razor Page models call the same service for:

- replacing the current revision during edits
- reverting to the previous revision
- soft-deleting an article revision set

The shared workflow uses:

- `Context.Database.CreateExecutionStrategy()`
- `BeginImmediateTransactionAsync()`
- one `SaveChangesAsync()`
- explicit transaction commit

Relevant files:

- `src/WikiWikiWorld.Web/Services/ArticleRevisionService.cs`
- `src/WikiWikiWorld.Web/Controllers/Api/ArticleApiController.cs`
- `src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs`
- `src/WikiWikiWorld.Web/Pages/Article/Delete.cshtml.cs`

### 4. Article creation with file upload is partially improved, but still has filesystem/database edge cases

Status: Partially resolved.

Severity: Medium.

The create path now stages uploads to a temporary file, writes `FileRevision` and `ArticleRevision` in one database transaction, finalizes the staged upload, and cleans up staged/final files on caught exceptions.

Residual risk:

- The file is moved to its final path before the transaction commit call. The catch path attempts cleanup, but a process crash between file move and rollback could still leave a file without committed database state.
- The XML docs currently say the final move happens after the database transaction succeeds, but the code moves before commit.

Relevant file:

- `src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs`

Recommended action:

- Prefer a clear finalize-after-commit pattern, with compensating cleanup if post-commit file finalization fails.
- Consider a small background cleanup task for stale `.tmp` files and orphaned final files.

### 5. Response compression is still registered but not enabled

Status: Still true.

Severity: Medium.

`Program.cs` calls `AddResponseCompression()`, but no `UseResponseCompression()` call exists in the request pipeline.

Likely impact:

- The app carries configuration that does not affect responses.
- The intended production optimization is absent.

Relevant file:

- `src/WikiWikiWorld.Web/Program.cs`

Recommended action:

- Add `App.UseResponseCompression()` in the correct middleware order, or remove the service registration if compression is intentionally not used.

### 6. Build hygiene is now improved

Status: Previously true, now resolved for normal build.

Severity: Closed for `dotnet build`.

Current state:

- `TreatWarningsAsErrors` is enabled.
- .NET analyzers and latest analysis level are enabled.
- stale checked-in build logs were removed.
- `dotnet build WikiWikiWorld.slnx` succeeds with zero warnings and zero errors.

Remaining validation:

- Release publish should still be tested and documented. The old ReadyToRun/`avx512f` evidence was in a stale log and no matching publish setting was found in current project files.

Relevant files:

- `Directory.Build.props`
- `README.md`

### 7. Startup composition still needs cleanup

Status: Still true.

Severity: Medium.

`Program.cs` still mixes many concerns:

- hosting and Kestrel setup
- data path setup
- EF Core/SQLite setup
- identity and JWT authentication
- OpenAPI/Scalar
- sitemap and robots endpoints
- rewrite rules
- static file serving
- security headers
- ImageSharp
- response prettification

It also still contains duplicate `AddMemoryCache()` calls.

Recommended action:

- Remove the duplicate cache registration.
- Move related startup groups into focused extension methods once behavior is stable.

### 8. Specification layer remains a maintainability question

Status: Still true as an architectural observation.

Severity: Low to Medium.

The specification pattern is widely used and consistent, but it adds ceremony for many simple queries. This is not a correctness bug. It is a maintainability tradeoff.

Recommended action:

- Keep for reused or complex queries.
- Avoid adding new one-off specifications for simple local queries unless they clearly improve reuse or readability.

## Recommended Improvement Order

### Phase 1: Correctness and Operational Safety

1. Tighten file upload finalization and cleanup behavior.
2. Add explicit admin/tooling context creation patterns when those workflows exist.

### Phase 2: Runtime Pipeline Cleanup

1. Enable or remove response compression.
2. Remove duplicate service registrations.
3. Split `Program.cs` into focused startup extension methods where it reduces noise.

### Phase 3: Architecture Simplification

1. Reassess global query filters after the null-context fix.
2. Reassess the specification layer for simple read paths.

### Phase 4: Release Verification

1. Add a documented release publish command.
2. Verify release publish in the target Linux deployment shape.
3. Add tests or smoke checks for culture-selector routing and revision writes.

## Definition Of Done For Cleanup Work

- root-domain culture-selector behavior works intentionally in a running app
- tenant and culture scoping behavior is explicit and correct
- article revision writes are transactional across API and Razor Page workflows
- file upload flows do not leave orphaned records or files under normal failure paths
- response compression is either enabled correctly or removed
- the solution builds cleanly with warnings as errors
- release publish succeeds with documented settings
- local runtime data and build outputs stay out of Git
