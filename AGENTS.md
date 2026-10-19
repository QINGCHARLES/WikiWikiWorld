# AGENTS.md - v15

## Defaults and existing projects

* **Runtime:** .NET 10 / C# 14 is the default for all new projects and standalone examples. In an existing project, respect its current target until an upgrade is agreed.
* **Older .NET projects:** ask whether to upgrade to .NET 10 unless I have already answered for this scope. First inspect the project and propose the smallest compatible change: usually update `TargetFramework`/`TargetFrameworks`, preserving platform qualifiers, then adjust only required SDK, packages, workloads, or incompatible code. Avoid migration tools and broad refactoring when ordinary changes suffice. Continue compatible work on the current target while awaiting a decision. Build and verify an approved upgrade; changing the TFM alone does not prove compatibility.
* **Windows desktop:** use WinUI 3 with the Windows App SDK for new Windows desktop apps; develop and run them on Windows. Maintain existing WinForms/WPF apps unless a UI migration is agreed. Do not default new apps to WinForms.
* **Web:** ASP.NET Core Razor Pages, developed on Windows and deployed as chiseled Docker containers on bare-metal Ubuntu. Use the brief deployment conventions below.
* **Mobile:** .NET MAUI for Android/iOS/iPadOS; follow the platform SDK, workload, and build-host requirements recorded by the project.
* **Data:** EF Core 10 + SQLite for new projects. Existing Dapper projects remain supported under the data-access transition policy below.
* A runtime upgrade does not authorize a UI-framework migration, an ORM migration, or a deployment redesign. Treat their scopes separately.

## Instruction priority and project context

Resolve conflicts among my instructions and preferences in this order:

1. Correctness, data preservation, and required framework, platform, integration, and compatibility contracts constrain implementation. Never break a required contract or risk data loss merely to satisfy a convention.
2. My explicit task instructions override my standing preferences and defaults within those constraints. Apply an override already given without asking me to confirm it again.
3. Applicable local instructions and project-specific rules refine these generic defaults. Use the project's architecture, established patterns, and agreed migration scope.
4. Apply the remaining global rules and style preferences within those constraints. Style does not outrank correctness or compatibility.

Before planning or editing, read `PROJECT.md` if present, applicable local instructions, and relevant build/configuration files. Read existing references governing the affected area. If context is inaccessible, identify the missing fact; ask only when it materially affects the task, and continue independent work.

`PROJECT.md` should record the mission, layout, current stack, partially migrated areas, agreed migration scope, verification commands, browser/platform support, and deployment/schema procedures. Describe how to obtain approved test credentials rather than storing reusable secrets. Keep temporary pins, custom durability mechanisms, and server-specific values in project references; do not invent components described by an inaccessible reference.

If a material conflict remains unresolved by this order, explain it and ask for the specific decision needed to proceed. Briefly disclose material exceptions in the result.

## Engineering judgment and scope

* Act as a lead engineer: check the underlying goal, whether the suggested method solves it, whether a standard API or official SDK fits better, and any concrete fragility or maintenance cost.
* Present a **💡 Recommended Alternative** before implementation when a better approach materially changes my requested method, scope, dependencies, behavior, cost, or architecture. Explain the trade-off and ask which approach to take unless I have already chosen. Decide routine implementation details yourself.
* Use the minimum complete solution. Touch only what the task requires, preserve unrelated user changes, and clean up artifacts introduced by your work. Do not perform speculative cleanup or migrations.
* Introduce interfaces/layers only for demonstrated value: another implementation, an exercised test seam, or known volatility. Start with direct queries; extract proven reuse into expressions/methods. Use specifications only when their composition or reuse justifies them.
* Accept intentional coupling to the established stack. Do not abstract for a hypothetical database swap.
* Proceed with reasonable, reversible assumptions for minor ambiguity. Ask when correctness, scope, required contracts, or irreversible decisions depend on the answer; do not repeat answered questions.
* Build, inspect, and prepare a concrete result before seeking any final approval that is actually needed. Obtain approval for destructive or production actions when the task has not already authorized them.
* Avoid generic logging, security, privacy, and ethics commentary. Flag concrete issues relevant to the requested change, including data-loss risks; keep necessary diagnostics useful and avoid exposing secrets.

## Runtime and API verification

Do not reject modern APIs based on model memory. Check the actual target, SDK/language and package versions, namespaces, overloads, and diagnostics. Check database-provider translation separately from API availability. Consult official documentation for unresolved version-sensitive questions. Diagnose failures before proposing dependency changes; never silently upgrade or downgrade to make an example work.

These released capabilities are valid on the .NET 10 baseline, subject to their required packages and context:

|Capability|Context|
|-|-|
|`extension(Type Receiver)` blocks and `field`-backed properties|C# 14|
|`TypedResults.ServerSentEvents(...)`|ASP.NET Core 10 server-to-client streaming|
|`Builder.Services.AddValidation()`|ASP.NET Core 10 Minimal API validation|
|`Microsoft.AspNetCore.JsonPatch.SystemTextJson`|Stable .NET 10-compatible JSON Patch package|
|`LeftJoin(...)` / `RightJoin(...)`|.NET 10 LINQ operators with EF Core 10 translation support|
|Named `HasQueryFilter(...)` and `IgnoreQueryFilters(\["FilterName"])`|EF Core 10|

Use the compiler's default language version for the target (.NET 10 defaults to C# 14). Do not enable preview language features or prerelease packages without an agreed need. Older projects awaiting an upgrade must use capabilities supported by their actual target.

References when needed: [C# 14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14), [EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew).

## Naming, formatting, and build conventions

* Prefer explicit types. Use `var` only when the type is truly obvious or the language requires it, such as an anonymous type.
* Use **PascalCase** for new, owned identifiers: types, members, variables, parameters, generic parameters, and Razor/XAML names. Exceptions are simple loop counters and other useful single-character variables, including lambda parameters.
* Use descriptive names, including longer names when helpful. Preserve public/named-argument contracts, external JSON/config schemas, database mappings, framework/browser-required names, and generated code. Avoid unrelated renames.
* Include units in owned numeric names: `DelaySeconds`, `TimeoutMs`, `FileSizeBytes`, `BufferSizeKiB`, `DistanceMeters`, `WeightKg`, `TempCelsius`. Prefer bytes; define `KB`/`MB` as decimal bytes and `KiB`/`MiB` as binary bytes. Avoid ambiguous `Kb`/`Mb`. Apply this to owned config/database/API fields without breaking existing contracts. Omit redundant units for types such as `TimeSpan`, date/time types, indices, and counters.
* Use tabs and opening braces on their own line in C#. A single-line `if` that only returns may omit braces. Use file-scoped namespaces; use block scope only when a file genuinely needs multiple namespaces.
* Enable nullability, warnings as errors, .NET analyzers, implicit usings, and deterministic builds. Use `AnalysisLevel=latest` with the repository-selected SDK; set `ContinuousIntegrationBuild=true` only in CI. Preserve platform-qualified TFMs such as `net10.0-windows` and project-specific `TargetFrameworks`; do not force plain `net10.0` through shared props.
* Record the chosen SDK and roll-forward policy in `global.json` where appropriate, and keep CI aligned. Keep compatible package/tool versions consistent without unrelated upgrades or warning suppression.
* Express mechanical rules through the repository's `.editorconfig`, analyzers, and build configuration where supported. Follow file-format requirements for JSON, YAML, shell, and other non-C# files; do not force C# indentation rules onto them.
* Check explicit, implicit, and generated global usings, including `GlobalUsings.cs` and project `Using` items. Avoid duplicates. Keep genuinely common namespaces global and specialized ones local; no percentage census. Remove confirmed unused usings in edited files.
* Prefer `.slnx` for new solutions with compatible tooling. `.sln` remains supported; migrate existing solutions only for a task-related reason, updating dependent tooling together.
* Prefer `dotnet` CLI where supported; use required Windows/mobile build hosts. Show PowerShell and Bash variants only when differences matter. Use forward slashes in web/config paths and `Path` APIs for filesystem paths, preserving rooted-path semantics.

## Modern C# and collections

* Prefer readable modern syntax: primary constructors, target-typed `new()`, pattern matching, switch expressions for values, expression-bodied members, and suitable `field`-backed properties. Prefer `is null` / `is not null` for null tests.
* Prefer C# 14 extension blocks for new extensions inside a non-nested, nongeneric static class. Preserve public signatures and verify callers when changing overload resolution. Use `OverloadResolutionPriority` only for a demonstrated selection problem.
* Use `sealed` unless inheritance is intended or required. Make helpers and local functions `static` when they need no instance state/capture and no framework or interface contract requires an instance member.
* Prefer matching guard helpers, such as `ArgumentNullException.ThrowIfNull(Value)` and `ArgumentException.ThrowIfNullOrWhiteSpace(Text)`. Use explicit argument exceptions when no helper expresses the required contract.
* Prefer using declarations when method scope is the right lifetime; use an explicit block when resources, locks, or transactions should end earlier. Use `await using` for asynchronous disposal where appropriate.
* Prefer ranges over equivalent `Substring` calls when clearer. Use spans for suitable parsing/validation and measured hot paths. Async/iterator span locals must obey compiler ref-safety rules and never survive `await`/`yield` suspension. Do not capture spans or store them in ordinary class/struct fields. Use strings or suitable memory types for async signatures/retained buffers; preserve useful string overloads and null behavior.
* Consider `params ReadOnlySpan<T>`, other supported `params` collections, `readonly record struct` value objects, and immutable/frozen collections when justified. Account for boxing, copying, and construction costs rather than assuming a performance improvement.
* C# 14 null-conditional assignment, supported lambda modifiers, and `nameof` on unbound generics are available when clearer. Use `"\\e"` for ANSI escape characters. Use less common features such as inline arrays, ref constraints, or partial members only for an actual requirement.

Never return null for collection results. Empty means successful absence of results, not failure/cancellation. Use a non-collection result type when unavailable data must be distinguished from an empty result.

Use collection expressions when the target type is supported and known. For ordinary in-memory collections:

|Intent|Preferred form|
|-|-|
|Empty list|`List<Article> Articles = \[];`|
|Materialize list|`List<Article> Result = \[.. Articles];`|
|Materialize array|`Article\[] Result = \[.. Articles];`|
|Default-comparer set|`HashSet<Item> Result = \[.. Items];`|
|Empty concrete dictionary|`Dictionary<string, int> Counts = \[];`|
|Copy a concrete dictionary and its comparer|`Dictionary<string, int> Copy = new(Counts, Counts.Comparer);`|
|Empty synchronous sequence|`Enumerable.Empty<T>()` or a target-typed `\[]`|
|Empty asynchronous sequence on .NET 10|`AsyncEnumerable.Empty<T>()`|

Keep `ToHashSet(Comparer)` and `ToDictionary(...)` for required comparers/projections/duplicate-key behavior. Use a constructor for dictionary copies, not spread-copy syntax. Preserve EF asynchronous materialization. When target inference fails, use an explicit API result type, typed local, or cast; retain LINQ when clearer.

## XML documentation and human readability

* AI-authored code must be comprehensible to human maintainers. Require XML documentation for every new or meaningfully changed type, constructor, method **including private methods**, property, event, and indexer.
* Maintain comments and backfill meaningfully changed members. Touching a file or reformatting it does not trigger unrelated backfilling. Handle that separately; do not hand-edit generated members.
* Explain purpose and meaningful behavior, side effects, and non-obvious constraints. A comment that merely expands the name, such as "Parses the index" for `ParseIndex`, is insufficient.
* Keep comments accurate when behavior, signatures, nullability, units, defaults, constraints, or documented exceptions change.
* Use `<summary>` for purpose/behavior; match `<param>`/`<typeparam>` names exactly. Describe non-void results with `<returns>`, including async completion/results. Document relevant exceptions that actually propagate, use `<remarks>` for nuance, `<inheritdoc/>` for matching inherited contracts, and `<see cref="Type.Member"/>` for references.

## Async, synchronization, and performance

* Use async for asynchronous I/O and coordination. Keep ordinary in-memory work synchronous; completed tasks are acceptable for existing framework/interface contracts. Offload expensive CPU work when needed for UI responsiveness; do not routinely wrap server work in `Task.Run`.
* Accept and propagate `CancellationToken CancellationToken` for cancellable async operations you own. Use `HttpContext.RequestAborted` in request work. Respect framework signatures, propagate cancellation rather than reporting success, and avoid `async void` except required event handlers.
* Use `System.Threading.Lock` for synchronous mutual exclusion. Neither `lock` nor an `EnterScope()` lifetime may span an `await`. For asynchronous mutual exclusion, use a suitable async primitive, commonly `SemaphoreSlim.WaitAsync(CancellationToken)`, with release in `finally` after successful acquisition.
* Use `Task.WhenAll` only for independent, concurrency-safe operations with bounded resources. Await sequentially when ordering, limits, or a shared `DbContext` requires it. Prefer `ParallelOptions.MaxDegreeOfParallelism` for `Parallel.ForEachAsync`; use a shared semaphore only for an additional purpose.
* Use channels for actual producer/consumer needs, with appropriate capacity/completion. Use `PeriodicTimer` for periodic ticks and cancellable `Task.Delay` for delay-after-work or backoff. Avoid `Thread.Sleep` in async/UI flows.
* Prefer readable `foreach` loops. Change loops, allocation patterns, or data structures for demonstrated performance needs; preserve semantics and measure consequential claims. Do not assume a `for` loop is faster over arrays/spans.

Reference when needed: [synchronous lock semantics](https://learn.microsoft.com/en-us/dotnet/api/system.threading.lock?view=net-10.0).

## Data access

### Transition and ownership

* Use EF Core 10 + SQLite for new projects. Maintain Dapper during ordinary maintenance and feature work in existing Dapper areas. Convert only within the migration scope already agreed for the task or an approved project plan.
* `PROJECT.md` identifies the current stack and partially migrated areas. Preserve each area's established stack outside the agreed scope. EF-specific rules do not require introducing EF components into Dapper code, and an ORM migration does not follow automatically from a runtime upgrade.
* Keep data access in established service/query boundaries rather than page markup or handlers. Do not add generic repositories, specifications, or interfaces without demonstrated value. Parameterize SQL values, including in retained Dapper queries.

### Contexts and queries

* A `DbContext` is a short-lived unit of work and is not thread-safe. Never run overlapping operations on one context, even if queries look independent. Await its operation before reusing it.
* Use scoped contexts for normal web requests. Use an appropriate scope or `IDbContextFactory<TContext>` for background jobs, desktop/mobile operations, and independent concurrent units of work. Dispose contexts you create; do not retain one in a singleton or for application lifetime.
* Project needed columns to DTOs in the query. Use `AsNoTracking()` for read-only entity results. Entity-free projections already avoid tracking; DTOs containing entities can still track them. Track entities when the unit of work updates them.
* Keep filtering, projection, ordering, and bounded paging in `IQueryable` before materialization. Avoid unnecessary graphs, round trips, and accidental client evaluation. Use deterministic paging order with a tie-breaker when needed.
* Prefer `Any()`/`AnyAsync()` for existence queries. In-memory `Chunk`, `MaxBy`/`MinBy`, and collection expressions are not universal EF optimizations. Check changed query shapes for translation and SQL; do not hide translation failures with client evaluation.
* Use .NET 10 `LeftJoin`/`RightJoin` where clearer and supported by the provider/query shape. For EF Core 10 soft-delete or tenant filters, use named filters and selectively disable only the intended filter, such as `IgnoreQueryFilters(\["SoftDelete"])`. Preserve required tenant isolation; bypassing a filter is an explicit operation, not a default query workaround.
* Write LINQ in method syntax. Use query syntax only where `let`, group joins, or multi-source joins are materially clearer; do not mix styles within one query without need. Prefer navigation properties over explicit joins.
* Use raw SQL in EF areas only for constructs EF cannot translate (for example SQLite FTS5, recursive CTEs, window functions) or a measured query problem after inspecting the generated SQL. Use `FromSql`/`SqlQuery`/`ExecuteSql` with interpolated parameters; never pass concatenated or interpolated values to `FromSqlRaw`/`ExecuteSqlRaw`. Select explicit columns, not `*`, and keep raw SQL in the query/service boundary.
* Raw SQL is not compile-checked: cover each raw query with a test against the actual provider so renames and schema changes fail in tests, not production.
* Prefer `ExecuteUpdate`/`ExecuteDelete` for set-based writes. They and `ExecuteSql` bypass the change tracker; do not rely on entities already tracked by that context afterward.

### SQLite, schemas, and durability

* EF's SQLite provider uses `Microsoft.Data.Sqlite`; let the configured context/provider manage ordinary connection lifetimes. Centralize connection settings, foreign-key enforcement, and initialization rather than repeating PRAGMAs in handlers.
* Prefer WAL with normal/private cache for file-backed databases. Do not add `Cache=Shared` alongside WAL by default; special shared in-memory scenarios need an explicit reason.
* SQLite's ADO.NET async methods execute synchronously. Propagate cancellation where supported, but do not promise nonblocking database I/O or immediate interruption merely because an API is async. Keep units of work short and respect provider concurrency limits.
* Use EF migrations for schema evolution in new EF projects. For existing projects, follow the recorded schema owner and application procedure, including approved manual SQL. Do not silently introduce startup migrations, replace migration history, or mix `EnsureCreated` with a migrations-managed database.
* Plan Dapper-to-EF baseline migrations around existing schema/data. Review generated changes for data, constraints, and externally managed objects. Justify indexes by queries and required correctness constraints.
* Record who applies migrations and when. SQLite cannot generate EF standard idempotent scripts; use a supported procedure based on actual migration state. Test schema changes on representative disposable data and assess rollback/recovery before deployment.
* Default to `synchronous=FULL` for durable writes in new SQLite setups. Preserve the project's documented durability policy and its configuration owner. Relax durability only for explicitly classified, reconstructible data with accepted loss; drafts/autosaves and user writing are not disposable by default. Do not alter durability inside an active transaction or add per-write wrappers when centralized configuration already owns it.
* Document custom interceptors, durability scopes, and temporary pins per project, with affected versions, rationale, and verification-based removal criteria. Check resolved dependencies and the original failure before removing a workaround; a future major version alone is not evidence.

References when needed: [context lifetime](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/), [tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking), [SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations), [SQLite async](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

## DTOs, time, options, and serialization

* Use ordinary classes for EF entities and `record class` DTOs where value semantics fit. Prefer `required` for mandatory DTO initialization and `init`/get-only properties for immutability. `required` alone is not immutable; record `with` copies are shallow, including mutable children.
* Prefer `IReadOnlyList<T>`/`IReadOnlyDictionary<TKey, TValue>` results and `IEnumerable<T>` inputs when one pass suffices. Choose richer contracts for indexing, counts, or async streaming. Read-only interfaces do not guarantee immutable storage/elements.
* Use UTC for instants, normalizing before textual round-trip (`"O"`) formatting. Use invariant machine-readable persistence and appropriate cultures for user-facing values. Preserve date-only/time-only business meaning; formatting itself does not convert to UTC.
* Choose SQLite timestamp mappings that support required queries. `DateTimeOffset` comparison/ordering has limitations; use UTC `DateTime` or tested conversions where needed. Preserve UTC meaning on round trips; do not force text formatting into every EF mapping.
* Use validated options, including data annotations and `ValidateOnStart()` where startup failure is appropriate. Prefer target-typed object initializers for values; retain framework builders such as `UseSqlite` where required.
* Prefer `System.Text.Json`. Reuse preconfigured options per contract; preserve required naming/null behavior and omit nulls where allowed. Do not allocate options per call or force unrelated APIs to share them. Consider source generation for relevant performance/publishing needs.
* Use exception filters for selection criteria, not mandatory logging. Follow the project handling boundary; do not swallow errors or lose stack information when rethrowing.

## ASP.NET Core and Razor Pages

* Prefer sealed, primary-constructor `PageModel` classes where compatible. Parameters are not automatically readonly fields; use explicit readonly storage when required. Preserve framework-required inheritance/members.
* Use async handlers when they await asynchronous work, and synchronous handlers otherwise. Bind only the fields needed; enable GET binding with `SupportsGet = true` only intentionally. Prefer feature organization and reuse through partials within the established layout.
* Use Minimal API `AddValidation()` with its required package when appropriate; disable per-endpoint validation only deliberately. Use keyed services for actual multiple-implementation selection needs.
* Prefer `TypedResults.ServerSentEvents(...)` for suitable unidirectional updates. Select polling, SSE, or WebSockets according to required behavior.
* For JSON Patch, prefer `Microsoft.AspNetCore.JsonPatch.SystemTextJson` with a compatible package and endpoint binding. A `PATCH` request does not inherently require JSON Patch. Restrict updates to the intended input surface and apply validation before persistence.
* Use structured logs and scopes for meaningful operational context when logging is needed. Do not add logging boilerplate to every small method or leave temporary debugging output behind.

## HTTP clients, retries, and idempotency

* Centralize external-service configuration. Prefer typed factory clients for service integrations and named clients for dynamic selection or simpler configuration. Match lifetimes to consumers and reuse underlying connection pools.
* The default factory `SocketsHttpHandler` sets `PooledConnectionLifetime` from `HandlerLifetime` on supported platforms. Inspect actual handlers before overriding; configure custom long-lived clients for real DNS needs. Do not assume factory lifetimes are infinite.
* Isolate cookie containers by account/session. Avoid factory pooling that unintentionally shares/discards cookies. Dedicated long-lived clients may fit; a globally shared cookie container does not provide isolation.
* Add resilience when the integration needs it, using one configured handler per client. `AddStandardResilienceHandler()` provides rate limiting, total/attempt timeouts, retries, and circuit breaking; hedging is a separate deliberate policy. Do not stack retry layers or enable duplicate attempts for writes without justified replay/idempotency semantics. `DisableForUnsafeHttpMethods()` is available to disable retries for unsafe HTTP methods.
* Set operation-appropriate budgets. For a resilience pipeline, configure `TotalRequestTimeout.Timeout` and `AttemptTimeout.Timeout`, and ensure `HttpClient.Timeout` does not unintentionally pre-empt them; an infinite client timeout is appropriate only when another mechanism owns the required budget. Pass caller cancellation. Add a linked per-call deadline when needed, rather than mandating a second timeout for every request.
* Distinguish short API calls, uploads/downloads, and long-lived streams. Specify whether a deadline covers connection/headers, body consumption, idle periods, or the whole operation. Stream large responses and deserialize from the stream, passing cancellation through body processing. Do not assume a headers-only timeout bounds later reads.
* Add idempotency where retrying writes risks duplicate effects. Scope keys by caller/tenant and operation, bind request fingerprints, coordinate dedupe and writes atomically, and define concurrent requests, replay, mismatches, and retention. Keep storage details project-specific; do not mandate a dedupe table for every write.

References when needed: [factory handler behavior](https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/default-handler), [HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience), [client lifetimes and cookies](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines).

## Website deployment: essential conventions

The standard is a chiseled Docker container on a bare-metal Ubuntu host. Keep host provisioning, actual domains, registry values, schedules, and recovery procedures in the project's deployment runbook.

* **Build:** use SDK container publishing (`dotnet publish -t:PublishContainer`), normally without a Dockerfile. Target the host's Linux architecture and a compatible chiseled image; use `ContainerFamily=noble-chiseled-extra` and `InvariantGlobalization=false` for culture/currency/time-zone features. Check native/font dependencies; runtime images have no shell/package manager.
* **Edge:** single-app hosts expose Kestrel HTTP 80 / HTTPS 443 with app-side TLS. Do not add a proxy or forwarded-header processing by default; configure trusted proxies only when the project actually uses them.
* **State/config:** keep binaries in `/app`, persistent data/uploads in bind-mounted `/app/data`, and certificates in `/app/certs`. Match production paths/endpoints to mounts. Run non-root; verify permissions against the image user (normally UID/GID 1654). Keep secrets outside tracked files/images.
* **TLS:** serve only `/app/data/.well-known/acme-challenge` at its public ACME path before HTTP redirects/authentication; never expose the data root. Cover all hostnames, including redirect sources. Runbooks own issuance/renewal and certificate activation by restart or verified reload.
* **Repository:** version scripts under `deploy/` and relevant task definitions. Use host-side Bash, LF endings, and executable permissions. Pull successfully before stopping the running container; support immutable rollback tags and bounded logs. Run maintenance host-side; use consistent SQLite backups, not live file copies.
* **Check:** verify the actual image starts, serves HTTPS, preserves writable state, and handles cultures correctly. Follow the project schema procedure; containerization does not imply startup migrations. Probe from the host or an app endpoint, not assumed in-container shell/curl.

Reference when needed: [SDK container configuration](https://learn.microsoft.com/en-us/dotnet/core/containers/publish-configuration).

## Front end: native-first, no unnecessary build step

* Prefer plain HTML, CSS, and JavaScript without frontend frameworks, Sass/LESS, or preprocessors. Use native features when they meet the requirement; small JavaScript fallbacks are appropriate for essential functionality.
* Define browser/WebView support in `PROJECT.md`. If an absent matrix affects a feature choice, ask or use a broadly supported fallback. Check version-sensitive support and use progressive enhancement; essential functionality must work.
* Use the viewport meta tag, Grid/Flex, `gap`, logical properties, and `clamp()` for responsive layouts. Prefer layout-based centering; use absolute positioning/transforms when appropriate. Use backgrounds for decoration and semantic images for content.
* Give content images intrinsic numeric HTML `width`/`height` where known, appropriate alt text, and responsive CSS such as `max-inline-size: 100%; block-size: auto`. Use `object-fit: cover` for intentional cropping.
* Require keyboard and touch operation, visible focus, appropriate semantics/labels, and reduced-motion behavior. Dialogs/popovers need working focus and dismissal behavior; hover must not be the only route to essential content.
* Prefer native nesting, cascade layers, useful `:has()`/`:is()`/`:where()` selectors, `:focus-visible`, subgrid, and container queries when supported. Gate newer syntax such as CSS functions/conditionals, typed attributes, style/scroll-state queries, anchor positioning, declarative invokers, and scroll markers/buttons individually; essential interactions need a working fallback.
* Use native `<dialog>`, popovers, scroll snapping, and form controls where suitable. Consider `field-sizing`, customizable selects, and `:user-valid`/`:user-invalid` with support checks. View Transitions can involve JavaScript; scroll-driven animations do not replace unrelated `IntersectionObserver` uses.
* Prefer OKLCH for perceptually meaningful color adjustments. Display P3 (`color(display-p3 ...)`) is wide-gamut RGB, not perceptually uniform; provide fallbacks when needed. Use `color-scheme` and `light-dark()` when supported, while preserving an explicit user theme choice and appropriate media-query fallbacks.
* Use typography, fonts, balancing/truncation, and effects deliberately. Apply `content-visibility`, intrinsic sizing, sticky positioning, and scroll containment to actual needs; test visibility, overflow, focus, and reduced motion.
* Choose useful console diagnostics: named-value objects, `table`, `trace`, `assert`, timing, groups, levels, or `dir`. Use styled output sparingly and remove temporary diagnostics.

## UX: intentional friction

* Add deliberate confirmation or an undo window when it helps prevent consequential mistakes, such as destructive deletion, publishing, or credential changes. Match friction to the action and keep it accessible; do not add confirmation to every routine operation.
* Prefer pagination over endless scrolling where it provides useful closure and respects attention.
* Surface actionable contextual problems, such as missing attachments or materially ambiguous input. Avoid accidental lag, confusing controls, or repeated confirmations masquerading as thoughtful UX.

## Verification and responses

* Establish success criteria and applicable verification commands before implementation. Check affected behavior; avoid tests that merely restate implementation and unrelated verification scope.
* Run the relevant build, tests, analyzers, and format checks available for the changed area. Fix regressions introduced by your change. For query/schema changes, verify against the actual provider on disposable data; for UI changes, check affected interactions and target platforms; for container changes, check the actual published image when available.
* Do not suppress warnings, weaken checks, remove tests, or alter unrelated dependencies to manufacture success. Distinguish existing from new failures. Report unavailable tooling/credentials/platforms and exactly what remains unverified; never claim checks ran when they did not.
* Stop when the requested behavior and applicable gates are sufficiently verified. If blocked, report the concrete blocker and useful completed work rather than making speculative changes or looping indefinitely.
* **Repository work:** make authorized edits and report changed behavior, relevant verification/results, and material limitations. Do not substitute snippets for implementation or narrate every modern feature.
* **Code examples:** give the smallest complete answer; a full program only when requested or necessary to run it. Pair `.cshtml`/`.cshtml.cs` when both are needed. Apply these conventions and include relevant environment commands.
* Before sending, check the applicable instruction priorities, scope, contracts, documentation, style, and verification evidence. State material trade-offs briefly and do not ask me to reconfirm an override or action already authorized.

