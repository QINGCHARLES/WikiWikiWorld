# WikiWikiWorld.Data Code Review — 2026-06-09

Review of `src/WikiWikiWorld.Data` (EF Core 10 / SQLite data layer), including how it is
wired up in `src/WikiWikiWorld.Web/Program.cs`.

**Overall verdict:** Well above average. Modern .NET 10 / EF Core 10 usage (named query
filters), records with `init`, fail-fast multi-tenant filtering in the context constructor,
and a SQLite tuning stack (WAL + busy_timeout + retry-with-jitter + IMMEDIATE transactions)
that shows real understanding of SQLite's concurrency model.

**Recurring theme to watch:** several pieces of code promise something and silently don't
deliver it — errors are swallowed or configuration is dead, so failures are invisible.

---

## Major issues

### 1. `WriteDurabilityScope.High()` is a silent no-op in transactional paths — ✅ FIXED 2026-06-10

> **Resolution:** durability default flipped to High (FULL) with explicit opt-down via
> `WriteDurabilityScope.Normal()` for ephemeral hot paths; pragma now applied at
> connection-open and transaction-start (before `BEGIN`) instead of `SavingChanges`;
> silent catch removed. Verified by `scripts/verify-write-durability.cs`.
> See [write-durability.md](write-durability.md).

`SqliteDurabilityInterceptor` applies `PRAGMA synchronous = FULL` in `SavingChanges`, but
SQLite rejects that pragma while a transaction is active, and the interceptor's catch block
swallows the `SqliteException`. Every call site that pairs `BeginImmediateTransactionAsync`
with `WriteDurabilityScope.High()` (article save, file upload, new message, etc.) has the
transaction already open when the interceptor fires, so the commit runs at whatever level
the connection happened to be at. High durability only actually works in the
non-transactional paths (Register, Login, bookmarks, talk posts).

Compounding wrinkle: `PRAGMA synchronous` is **per-connection, not persistent**. The
`synchronous = NORMAL` set in `SqliteInitializer` only affects that one startup connection.
Pooled connections start at SQLite's default (FULL) and only get NORMAL after their first
non-High `SaveChanges`. So the effective durability of a transactional "High" write is
indeterminate — it depends on the history of the pooled connection it landed on.

**Fix direction:** apply the pragma before `BEGIN` via a transaction interceptor
(`IDbTransactionInterceptor.TransactionStarting`), move the `WriteDurabilityScope` at call
sites to wrap the transaction (not just `SaveChangesAsync`), set a deterministic baseline
`synchronous = NORMAL` in `SqlitePragmaInterceptor` at connection open, and remove the
silent catch so misordering fails loudly. See discussion in session notes / commit history.

### 2. Model configuration and real schema have drifted; nothing reconciles them

No migrations and no `EnsureCreated()` anywhere — the schema comes from the hand-written
`SqliteScripts/Create.sql`. Every `HasIndex` / `HasFilter` in `OnModelCreating` is therefore
dead configuration. Concrete drift already exists: the model declares a partial index on
ArticleRevisions with filter `IsCurrent = 1 AND DateDeleted IS NULL`; `Create.sql` creates a
plain unfiltered index. Pick one source of truth: adopt migrations, or strip index config
from the model and treat `Create.sql` as canonical.

### 3. Nothing in the database enforces "one current revision per article"

The `IsCurrent` invariant rests entirely on the IMMEDIATE-transaction code path being the
only writer. Add a unique partial index:

```sql
CREATE UNIQUE INDEX UX_ArticleRevisions_Current
ON ArticleRevisions (SiteId, Culture, UrlSlug)
WHERE IsCurrent = 1 AND DateDeleted IS NULL;
```

This turns any future bug into a loud constraint violation instead of silent data
corruption.

### 4. `BeginImmediateTransactionAsync` doesn't do what its name says — ✅ FIXED 2026-06-10

> **Resolution:** deleted `DbContextExtensions.cs`; all seven call sites now use plain
> `BeginTransactionAsync` (Microsoft.Data.Sqlite ≥ 6 issues `BEGIN IMMEDIATE` by default).

`DbContextExtensions.cs` (class `DatabaseFacadeExtensions` — names disagree) is just
`BeginTransactionAsync` plus a comment trail that contradicts itself and ends with "we just
return it." In practice probably fine — Microsoft.Data.Sqlite 6+ issues `BEGIN IMMEDIATE`
by default — but the method should be deleted rather than kept as a misleading wrapper.

### 5. `PRAGMA page_size = 8192` in `SqliteInitializer` is a no-op on existing databases

Page size cannot change once a DB is in WAL mode (requires `journal_mode=DELETE` +
`VACUUM`), and it is set *after* switching to WAL in the same batch. It only takes effect
on a brand-new empty file and silently does nothing otherwise.

### 6. Chronological ordering works only because every write happens to be UTC

`DateTimeOffsetToStringConverter` stores the offset in the string, and string comparison
across different offsets is not chronological. Today everything uses
`DateTimeOffset.UtcNow`, so it works — but one non-UTC write silently breaks every
`OrderByDescending(DateCreated)` and date-range query. Use a custom converter that
normalizes to UTC on write, or document the invariant prominently.

### 7. `LatestArticlesWithPublicationIssueInfoboxSpec` is a full-table scan

`Text.Contains("{{PublicationIssueInfobox")` becomes `LIKE '%…%'` over article bodies with
no possible index. Fine at current scale; will hurt. Fix cheaply with a flag column set at
save time (the markdown is already parsed) or FTS5.

---

## Minor issues

- **FileRevision has no `SiteId`; `ArticleTalkSubjectPost` has no site filter.** Both rely
  on parent-entity scoping or are global by design. Probably intentional, but worth a code
  comment since the context constructor loudly promises site isolation.
- **`ISiteContextService.AllowCrossSiteAndCultureQueries` is a default interface member**
  returning `false` — easy for an implementer to shadow accidentally. A plain abstract
  property is safer.
- **`sealed record` entities with mutable `set` properties** — records give value-based
  equality, a footgun next to EF change tracking (`Contains`/`Distinct` on entity lists
  behave by value, not identity). Works as used today; know why it's there.
- **`private new const int DefaultMaxRetryCount`** in `SqliteRetryExecutionStrategy` —
  member hiding via `new` is a smell; rename.
- **Style drift:** root files use tabs, `Models/` uses 4 spaces.
- **`GlobalUsings.cs` duplicates `<ImplicitUsings>enable</ImplicitUsings>`** — only the
  `WikiWikiWorld.Data.Models` global using earns its keep.
- **Stale `<Folder Include="Extensions\" />`** in the csproj — the folder has a real file.
- **`SitePassword` / `ArchivePassword` on `PublicationUploadQueue` are plaintext.** They
  must be recoverable, so hashing is out, but consider Data Protection encryption at rest.
- **Duplicated sync/async pairs** (initializer, interceptors) — the sync halves are likely
  never called; consider dropping them.

---

## Suggested priority

1. Fix #1 (durability no-op) — a correctness promise the code visibly makes and breaks.
2. Fix #2 + #3 together — one source of truth for schema, plus the unique partial index,
   so the `IsCurrent` invariant holds *period*, not just "while the code is careful."
3. Everything else opportunistically.
