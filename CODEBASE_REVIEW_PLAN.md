# Codebase Review Plan

## Purpose

This document captures the current architectural review of the `WikiWikiWorld` codebase, including the main correctness risks, overengineering concerns, and the highest-value improvements to make next. It is intended to serve as a stable reference for future cleanup work.

## Overall Assessment

The codebase is not fundamentally unsound. The core application shape is reasonable:

- ASP.NET Core Razor Pages for the web UI
- EF Core with SQLite for persistence
- A content/revision model that maps well to the wiki domain

However, parts of the infrastructure are more complex than the app currently needs. The main overengineering is concentrated in:

- global EF query-filter infrastructure for tenancy and culture
- a specification layer used for relatively simple queries
- a response-wide HTML prettification middleware
- routing and site-resolution behavior distributed across rewrite rules, request services, and base page model behavior

This complexity creates hidden behavior and raises the cost of debugging and maintenance.

## Confirmed Findings

### 1. Global tenant and culture filters do not honor "null means unscoped"

Severity: High

The data layer contract says that `null` site or culture context should allow cross-site or cross-culture operations. The implemented global query filters do not behave that way.

Current behavior:

- `ArticleRevision` uses `e.SiteId == _currentSiteId`
- `ArticleRevision` uses `e.Culture == _currentCulture`
- similar site filters exist on other entities

If `_currentSiteId` or `_currentCulture` is `null`, the filters do not become disabled. They instead exclude all rows. That means non-request code paths can silently see zero data rather than unscoped data.

Likely impact:

- background jobs
- migrations or tooling
- ad hoc admin code
- any future non-HTTP workflow

Relevant files:

- `src/WikiWikiWorld.Data/WikiWikiWorldDbContext.cs`
- `src/WikiWikiWorld.Data/ISiteContextService.cs`
- `src/WikiWikiWorld.Web/Services/SiteContextService.cs`

### 2. Culture-selector root-domain flow is likely broken

Severity: High

The site resolver explicitly supports root domains that do not resolve to a culture and are supposed to render a culture selector page. But the shared page-model base class always calls `ResolveSiteAndCulture()`, which throws when there is no culture.

Current behavior:

- `SiteResolverService` supports root-domain culture selector handling
- `BasePageModel.OnPageHandlerExecuting` always calls `ResolveSiteAndCulture()`
- `CultureSelectModel` inherits from `BasePageModel`

This creates an architectural contradiction. The service supports the scenario, but the page inheritance path likely fails before the page can execute.

Likely impact:

- root-domain culture-selector page may fail at runtime
- future pages that intentionally run without a culture will be fragile

Relevant files:

- `src/WikiWikiWorld.Web/Pages/BasePageModel.cs`
- `src/WikiWikiWorld.Web/Pages/CultureSelect.cshtml.cs`
- `src/WikiWikiWorld.Web/Services/SiteResolverService.cs`
- `src/WikiWikiWorld.Web/appsettings.Production.json`

### 3. API article updates are not atomic

Severity: High

The article update API performs two separate saves:

1. mark current revision as not current
2. insert the new current revision

If the second save fails, the article is left without a current revision. Under concurrent requests, two callers can also race through this path.

Likely impact:

- missing current revision
- inconsistent history state
- concurrency bugs during simultaneous edits

Relevant file:

- `src/WikiWikiWorld.Web/Controllers/Api/ArticleApiController.cs`

### 4. Article creation with file upload is not atomic

Severity: High

The create/edit page persists file-related state in multiple steps:

1. write file to disk
2. insert `FileRevision`
3. insert `ArticleRevision`

If the operation fails after step 2, the system can retain an uploaded file and file revision without any article pointing to it.

Likely impact:

- orphaned files on disk
- orphaned file-revision rows
- harder cleanup and auditing

Relevant file:

- `src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs`

### 5. HTML prettification middleware is costly and risky for production use

Severity: Medium

Every HTML response is buffered into memory, parsed into a DOM, rewritten, and then serialized again. That is a large amount of work for whitespace cleanup and formatting.

Current behavior:

- captures the full response body
- reparses it with AngleSharp
- normalizes text nodes
- re-emits the final HTML

This increases:

- memory pressure
- response latency
- the chance of subtle output changes in edge cases

It also looks mismatched with the application's needs. This kind of behavior is better suited to development tooling or static-output generation than the live request pipeline.

Relevant files:

- `src/WikiWikiWorld.Web/HtmlPrettifyMiddleware.cs`
- `src/WikiWikiWorld.Web/InlineAwarePrettyFormatter.cs`
- `src/WikiWikiWorld.Web/Program.cs`

### 6. Response compression is configured but not enabled

Severity: Medium

`AddResponseCompression()` is registered, but the middleware pipeline never calls `UseResponseCompression()`.

Likely impact:

- expected production optimization is missing
- the app pays infrastructure complexity cost without receiving the intended behavior

Relevant file:

- `src/WikiWikiWorld.Web/Program.cs`

### 7. Build hygiene is weak and release publish appears brittle

Severity: Medium

The repository currently tolerates warnings and has checked-in evidence of unresolved build issues.

Current behavior:

- `TreatWarningsAsErrors` is set to `false`
- the checked-in warnings file shows XML documentation warnings
- the checked-in publish output shows ReadyToRun / `avx512f` publish failure

Likely impact:

- regressions are easier to introduce
- release/publish behavior may be unreliable or environment-specific
- documentation quality drifts over time

Relevant files:

- `Directory.Build.props`
- `src/WikiWikiWorld.Web/build_warnings.txt`
- `src/WikiWikiWorld.Web/build_output.txt`

## Is The Codebase Overengineered?

Short answer: partially, yes.

The app is not overengineered everywhere. The domain model and overall solution layout are still reasonable. The overengineering is concentrated in infrastructure and cross-cutting concerns rather than in every feature.

The clearest examples are:

- global query filters combined with request-derived site/culture context
- a specification abstraction for many simple one-query use cases
- response-wide HTML prettification middleware
- domain resolution split across configuration, rewrite rules, request services, and base page execution

These layers are not wrong by themselves, but for the current size and maturity of the app they add enough indirection that ordinary behavior is harder to reason about.

## Recommended Improvement Order

### Phase 1: Correctness and Operational Safety

1. Fix tenant and culture filtering behavior
2. Fix root-domain culture-selector execution path
3. Make revision writes atomic
4. Make file upload plus article creation atomic where possible

Reason:

These items can create incorrect or inconsistent application state. They should be addressed before style or abstraction cleanup.

### Phase 2: Simplify Runtime Infrastructure

1. Remove or severely limit `HtmlPrettifyMiddleware`
2. Enable response compression properly or remove the registration
3. Reduce startup and hosting complexity in `Program.cs`

Reason:

These changes simplify production behavior, reduce hidden runtime cost, and make the request pipeline easier to reason about.

### Phase 3: Reduce Architectural Indirection

1. Reassess whether global query filters are the right tenancy mechanism
2. Reassess the specification layer for simple CRUD-style queries
3. Centralize article revision workflows into application services

Reason:

These changes improve maintainability and reduce scattered domain logic.

### Phase 4: Tighten Build and Engineering Discipline

1. Fix existing warnings
2. Re-enable warnings as errors
3. Fix or simplify the ReadyToRun / publish configuration
4. Add build verification steps for release publishing

Reason:

Once the system is behaviorally stable, stricter build hygiene will keep it stable.

## Suggested Concrete Next Tasks

### Task 1: Repair site and culture resolution model

Goals:

- allow root-domain culture selector pages to execute intentionally
- eliminate the contradiction between `SiteResolverService` and `BasePageModel`
- define one explicit pattern for pages that require culture versus pages that do not

Possible approach:

- split base page models into two variants:
  - a site-aware base type
  - a site-and-culture-required base type
- or make the current base model resolve using the root-check API and let each page opt into stricter behavior

### Task 2: Repair global query filter semantics

Goals:

- make `null` context truly mean unscoped access
- ensure background and non-request code paths do not silently return zero rows

Possible approach:

- change filters to short-circuit when current context is `null`
- or remove site/culture from global filters and make these constraints explicit in query entry points

### Task 3: Create a transactional article revision service

Goals:

- centralize create, edit, revert, and API update logic
- ensure exactly one current revision exists after successful writes
- reduce duplication between Razor Pages and API controller logic

Possible approach:

- introduce an application service for revision workflows
- execute each workflow inside a single transaction
- add uniqueness constraints if appropriate for current revisions

### Task 4: Rework file upload consistency

Goals:

- prevent orphaned file rows and filesystem artifacts
- make failure behavior predictable

Possible approach:

- write to a temporary location first
- persist database state transactionally
- move or finalize the file only after successful database commit
- add a cleanup path for failed operations

### Task 5: Remove production HTML prettification

Goals:

- reduce request overhead
- eliminate the risk of unintended HTML mutation

Possible approach:

- development-only middleware, if still useful
- or remove it entirely and rely on normal Razor output

### Task 6: Clean up host startup

Goals:

- make `Program.cs` easier to read and reason about
- reduce accidental environment coupling

Candidate cleanup:

- remove duplicate memory-cache registration
- remove unused service registrations
- move related startup concerns into focused extension methods
- make response compression either active or absent

### Task 7: Restore build rigor

Goals:

- keep the repo warning-free
- prevent release-only failures from lingering unnoticed

Candidate cleanup:

- fix XML documentation warnings
- set `TreatWarningsAsErrors=true`
- identify the source of the `avx512f` publish configuration
- simplify publish settings if aggressive optimization is not required

## Secondary Observations

These are not the highest-priority issues, but they are worth tracking:

- `Program.cs` contains duplicated `AddMemoryCache()` registration
- `Program.cs` contains a mix of hosting, security headers, rewrite configuration, sitemap endpoints, and data initialization in one file
- article revision workflows are split between page models and the API controller
- the specification pattern currently adds ceremony even for simple read paths
- README documentation is minimal and does not describe production/publish constraints

## Review Scope Notes

This review was based on static inspection of the codebase and existing build logs.

What was confirmed directly:

- architecture and startup composition
- EF Core context and global filters
- article create, edit, revert, and API update flows
- middleware behavior
- checked-in build warnings and publish failure logs

What remains to validate during implementation work:

- the root-domain culture-selector failure in a live running environment
- concurrency behavior under simultaneous article edits
- the exact source of the ReadyToRun `avx512f` publish configuration

## Definition Of Done For Cleanup Work

This review should be considered resolved only when all of the following are true:

- root-domain culture-selector behavior works intentionally
- tenant and culture scoping behavior is explicit and correct
- article revision writes are transactional
- file upload flows do not leave orphaned records or files
- HTML prettification is removed from production or justified and safely scoped
- response compression is either enabled correctly or removed
- the solution builds cleanly with no warnings
- release publish succeeds with documented settings
