# Implementation Plan: SQLite Resilience for EF Core

## Objective
Add production-grade SQLite resilience to the existing EF Core setup:
- **Retry logic** with exponential backoff for `SQLITE_BUSY` errors
- **Automatic PRAGMA management** (WAL mode, mmap, busy_timeout, foreign keys)
- **Write durability levels** (`Normal` vs `High`) for security-critical operations

**Key Constraint:** Zero changes to existing PageModels, Services, or Controllers. All existing `DbContext` usages continue to work unchanged.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     Existing Code (Unchanged)                    │
│  PageModels, Services, Controllers inject WikiWikiWorldDbContext │
└─────────────────────────────────┬───────────────────────────────┘
                                  │ DI resolves
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│              WikiWikiWorldDbContext (Unchanged)                  │
│  - Accepts DbContextOptions as before                            │
│  - NEW: Interceptors apply PRAGMAs and durability automatically  │
└─────────────────────────────────┬───────────────────────────────┘
                                  │ Intercepted by
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                    EF Core Interceptors (NEW)                    │
│  - SqlitePragmaInterceptor: applies PRAGMAs on connection open   │
│  - SqliteDurabilityInterceptor: sets synchronous before saves    │
│  - SqliteRetryExecutionStrategy: retries BUSY errors             │
└─────────────────────────────────────────────────────────────────┘
```

---

## What Gets Deleted

The `WikiWikiWorld.Database` project is no longer needed. It contained:
- `DatabaseConnectionFactory` — replaced by EF Core interceptors
- `ConnectionOptions` — not needed (options go in `Program.cs`)
- `WriteDurability` enum — moved to `WikiWikiWorld.Data`

**Files to delete:**
- `src/WikiWikiWorld.Database/` (entire directory)

**References to remove:**
- `src/WikiWikiWorld.Data/WikiWikiWorld.Data.csproj` → remove `<ProjectReference>` to Database
- `src/WikiWikiWorld.Web/WikiWikiWorld.Web.csproj` → remove `<ProjectReference>` to Database
- `WikiWikiWorld.slnx` → remove Database project entry

---

## Phase 1: Retries and PRAGMAs (No Breaking Changes)

### Step 1.1: Create the WriteDurability Enum
**File:** `src/WikiWikiWorld.Data/WriteDurability.cs` (NEW)

```csharp
namespace WikiWikiWorld.Data;

/// <summary>
/// Specifies the durability level for SQLite write operations.
/// </summary>
public enum WriteDurability
{
    /// <summary>
    /// Uses PRAGMA synchronous = NORMAL. Fast; latest commit may roll back on power loss.
    /// Suitable for most operations (article edits, comments, etc.)
    /// </summary>
    Normal,

    /// <summary>
    /// Uses PRAGMA synchronous = FULL. Slower; commit designed to survive power loss.
    /// Use for security-critical operations (password changes, role updates, financial data).
    /// </summary>
    High
}
```

---

### Step 1.2: Create a Custom Execution Strategy
**File:** `src/WikiWikiWorld.Data/SqliteRetryExecutionStrategy.cs` (NEW)

```csharp
namespace WikiWikiWorld.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// Custom execution strategy for SQLite that retries on BUSY/LOCKED errors
/// using exponential backoff with jitter.
/// </summary>
public sealed class SqliteRetryExecutionStrategy : ExecutionStrategy
{
    private const int DefaultMaxRetryCount = 3;
    private static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Initializes a new instance with default retry settings.
    /// </summary>
    public SqliteRetryExecutionStrategy(ExecutionStrategyDependencies Dependencies)
        : base(Dependencies, DefaultMaxRetryCount, DefaultMaxRetryDelay)
    {
    }

    /// <summary>
    /// Initializes a new instance with custom retry count.
    /// </summary>
    public SqliteRetryExecutionStrategy(ExecutionStrategyDependencies Dependencies, int MaxRetryCount)
        : base(Dependencies, MaxRetryCount, DefaultMaxRetryDelay)
    {
    }

    /// <inheritdoc/>
    protected override bool ShouldRetryOn(Exception Exception)
    {
        // Unwrap aggregate exceptions
        if (Exception is AggregateException Aggregate)
        {
            foreach (Exception Inner in Aggregate.InnerExceptions)
            {
                if (ShouldRetryOn(Inner))
                {
                    return true;
                }
            }
            return false;
        }

        // Check for retryable SQLite errors
        if (Exception is SqliteException SqliteEx)
        {
            // Primary BUSY (5)
            if (SqliteEx.SqliteErrorCode is 5)
            {
                return true;
            }

            // Extended BUSY variants: RECOVERY (261), SNAPSHOT (517), TIMEOUT (773)
            int Extended = SqliteEx.SqliteExtendedErrorCode;
            return Extended is 261 or 517 or 773;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override TimeSpan? GetNextDelay(Exception LastException)
    {
        TimeSpan? BaseDelay = base.GetNextDelay(LastException);
        if (BaseDelay is null)
        {
            return null;
        }

        // Add jitter (0% to 100% of base delay) to prevent thundering herd
        int JitterMs = Random.Shared.Next(0, (int)BaseDelay.Value.TotalMilliseconds);
        return BaseDelay.Value + TimeSpan.FromMilliseconds(JitterMs);
    }
}
```

---

### Step 1.3: Create a Connection Interceptor for PRAGMAs
**File:** `src/WikiWikiWorld.Data/SqlitePragmaInterceptor.cs` (NEW)

```csharp
namespace WikiWikiWorld.Data;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

/// <summary>
/// Interceptor that applies SQLite PRAGMAs when a connection is opened.
/// </summary>
public sealed class SqlitePragmaInterceptor(
    int BusyTimeoutMs = 5_000,
    int CacheSizePages = -20_000,
    long MmapSizeBytes = 2_147_483_648) : DbConnectionInterceptor
{
    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection Connection, ConnectionEndEventData EventData)
    {
        ApplyPragmas((SqliteConnection)Connection);
        base.ConnectionOpened(Connection, EventData);
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection Connection,
        ConnectionEndEventData EventData,
        CancellationToken CancellationToken = default)
    {
        await ApplyPragmasAsync((SqliteConnection)Connection, CancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(Connection, EventData, CancellationToken).ConfigureAwait(false);
    }

    private void ApplyPragmas(SqliteConnection Connection)
    {
        using SqliteCommand Command = Connection.CreateCommand();
        Command.CommandText = BuildPragmasSql();
        Command.ExecuteNonQuery();
    }

    private async Task ApplyPragmasAsync(SqliteConnection Connection, CancellationToken CancellationToken)
    {
        await using SqliteCommand Command = Connection.CreateCommand();
        Command.CommandText = BuildPragmasSql();
        await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
    }

    private string BuildPragmasSql() =>
        $"""
        PRAGMA busy_timeout = {BusyTimeoutMs};
        PRAGMA cache_size = {CacheSizePages};
        PRAGMA mmap_size = {MmapSizeBytes};
        PRAGMA temp_store = MEMORY;
        PRAGMA foreign_keys = ON;
        """;
}
```

---

### Step 1.4: Create a Startup Initializer for WAL Mode
**File:** `src/WikiWikiWorld.Data/SqliteInitializer.cs` (NEW)

WAL mode and page size must be set once at startup (they're database-level, not connection-level):

```csharp
namespace WikiWikiWorld.Data;

using Microsoft.Data.Sqlite;

/// <summary>
/// Initializes SQLite database-level settings (WAL mode, page size).
/// Call once at application startup before any DbContext usage.
/// </summary>
public static class SqliteInitializer
{
    /// <summary>
    /// Applies database-level PRAGMAs that persist across connections.
    /// </summary>
    /// <param name="ConnectionString">The SQLite connection string.</param>
    public static void Initialize(string ConnectionString)
    {
        using SqliteConnection Connection = new(ConnectionString);
        Connection.Open();

        using SqliteCommand Command = Connection.CreateCommand();
        Command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA page_size = 8192;
            """;
        Command.ExecuteNonQuery();
    }

    /// <summary>
    /// Applies database-level PRAGMAs that persist across connections.
    /// </summary>
    /// <param name="ConnectionString">The SQLite connection string.</param>
    /// <param name="CancellationToken">Cancellation token.</param>
    public static async Task InitializeAsync(string ConnectionString, CancellationToken CancellationToken = default)
    {
        await using SqliteConnection Connection = new(ConnectionString);
        await Connection.OpenAsync(CancellationToken).ConfigureAwait(false);

        await using SqliteCommand Command = Connection.CreateCommand();
        Command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA page_size = 8192;
            """;
        await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
    }
}
```

---

### Step 1.5: Wire Up EF Core in Program.cs
**File:** `src/WikiWikiWorld.Web/Program.cs`

Replace the existing `AddDbContext` block:

```csharp
// BEFORE:
// Builder.Services.AddDbContext<WikiWikiWorldDbContext>(Options =>
//     Options.UseSqlite(ConnectionString));

// AFTER:

// Initialize SQLite database-level settings (WAL mode, page size)
SqliteInitializer.Initialize(ConnectionString);

// Create interceptors
SqlitePragmaInterceptor PragmaInterceptor = new(
    BusyTimeoutMs: 5_000,
    CacheSizePages: -20_000,
    MmapSizeBytes: 2_147_483_648
);

Builder.Services.AddDbContext<WikiWikiWorldDbContext>(Options =>
    Options.UseSqlite(ConnectionString, SqliteOptions =>
    {
        SqliteOptions.ExecutionStrategy(Dependencies =>
            new SqliteRetryExecutionStrategy(Dependencies, MaxRetryCount: 3));
    })
    .AddInterceptors(PragmaInterceptor));
```

**Add the using directive at the top of Program.cs:**
```csharp
using WikiWikiWorld.Data;
```

---

## Phase 2: Write Durability Levels (Opt-In Enhancement)

### Step 2.1: Create an Ambient Durability Scope
**File:** `src/WikiWikiWorld.Data/WriteDurabilityScope.cs` (NEW)

```csharp
namespace WikiWikiWorld.Data;

/// <summary>
/// Provides an ambient scope to indicate that database writes within this scope
/// require high durability (PRAGMA synchronous = FULL).
/// </summary>
public sealed class WriteDurabilityScope : IDisposable
{
    private static readonly AsyncLocal<WriteDurability> CurrentDurability = new();

    private readonly WriteDurability PreviousDurability;
    private bool Disposed;

    /// <summary>
    /// Gets the current ambient write durability level. Defaults to Normal.
    /// </summary>
    public static WriteDurability Current => CurrentDurability.Value;

    /// <summary>
    /// Initializes a new durability scope with the specified level.
    /// </summary>
    /// <param name="Durability">The durability level for this scope.</param>
    public WriteDurabilityScope(WriteDurability Durability)
    {
        PreviousDurability = CurrentDurability.Value;
        CurrentDurability.Value = Durability;
    }

    /// <summary>
    /// Creates a high-durability scope (PRAGMA synchronous = FULL).
    /// </summary>
    public static WriteDurabilityScope High() => new(WriteDurability.High);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!Disposed)
        {
            CurrentDurability.Value = PreviousDurability;
            Disposed = true;
        }
    }
}
```

---

### Step 2.2: Create a SaveChanges Interceptor for Durability
**File:** `src/WikiWikiWorld.Data/SqliteDurabilityInterceptor.cs` (NEW)

```csharp
namespace WikiWikiWorld.Data;

using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor that applies the appropriate synchronous PRAGMA before SaveChanges
/// based on the ambient WriteDurabilityScope.
/// </summary>
public sealed class SqliteDurabilityInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData EventData,
        InterceptionResult<int> Result)
    {
        ApplyDurabilityPragma(EventData.Context);
        return base.SavingChanges(EventData, Result);
    }

    /// <inheritdoc/>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData EventData,
        InterceptionResult<int> Result,
        CancellationToken CancellationToken = default)
    {
        await ApplyDurabilityPragmaAsync(EventData.Context, CancellationToken).ConfigureAwait(false);
        return await base.SavingChangesAsync(EventData, Result, CancellationToken).ConfigureAwait(false);
    }

    private static void ApplyDurabilityPragma(DbContext? Context)
    {
        if (Context?.Database.GetDbConnection() is SqliteConnection Connection &&
            Connection.State == ConnectionState.Open)
        {
            string Synchronous = WriteDurabilityScope.Current is WriteDurability.High ? "FULL" : "NORMAL";
            using SqliteCommand Command = Connection.CreateCommand();
            Command.CommandText = $"PRAGMA synchronous = {Synchronous};";
            Command.ExecuteNonQuery();
        }
    }

    private static async Task ApplyDurabilityPragmaAsync(DbContext? Context, CancellationToken CancellationToken)
    {
        if (Context?.Database.GetDbConnection() is SqliteConnection Connection &&
            Connection.State == ConnectionState.Open)
        {
            string Synchronous = WriteDurabilityScope.Current is WriteDurability.High ? "FULL" : "NORMAL";
            await using SqliteCommand Command = Connection.CreateCommand();
            Command.CommandText = $"PRAGMA synchronous = {Synchronous};";
            await Command.ExecuteNonQueryAsync(CancellationToken).ConfigureAwait(false);
        }
    }
}
```

---

### Step 2.3: Add Durability Interceptor to Program.cs
**File:** `src/WikiWikiWorld.Web/Program.cs`

Update the `AddDbContext` block to include both interceptors:

```csharp
// Create interceptors
SqlitePragmaInterceptor PragmaInterceptor = new(
    BusyTimeoutMs: 5_000,
    CacheSizePages: -20_000,
    MmapSizeBytes: 2_147_483_648
);
SqliteDurabilityInterceptor DurabilityInterceptor = new();

Builder.Services.AddDbContext<WikiWikiWorldDbContext>(Options =>
    Options.UseSqlite(ConnectionString, SqliteOptions =>
    {
        SqliteOptions.ExecutionStrategy(Dependencies =>
            new SqliteRetryExecutionStrategy(Dependencies, MaxRetryCount: 3));
    })
    .AddInterceptors(PragmaInterceptor, DurabilityInterceptor));
```

---

### Step 2.4: Usage Examples

**Normal writes (no change needed):**
```csharp
// Existing code works unchanged — uses synchronous = NORMAL
Article.Content = NewContent;
await DbContext.SaveChangesAsync();
```

**High-durability writes (wrap in scope):**
```csharp
// Security-critical operation — uses synchronous = FULL
using (WriteDurabilityScope.High())
{
    User.PasswordHash = NewHash;
    await DbContext.SaveChangesAsync();
}
```

**Candidates for high-durability writes:**
- Password changes
- Role/permission updates
- Account deletion
- Email confirmation
- Two-factor authentication setup

---

## Summary: What Changes Where

| File | Change Type | Description |
|------|-------------|-------------|
| `WikiWikiWorld.Database/` | **Delete** | Entire project removed |
| `WikiWikiWorld.Data.csproj` | Modify | Remove Database project reference |
| `WikiWikiWorld.Web.csproj` | Modify | Remove Database project reference |
| `WikiWikiWorld.slnx` | Modify | Remove Database project entry |
| `WriteDurability.cs` | New | Durability enum |
| `SqliteRetryExecutionStrategy.cs` | New | Handles BUSY retries |
| `SqlitePragmaInterceptor.cs` | New | Applies per-connection PRAGMAs |
| `SqliteInitializer.cs` | New | One-time WAL mode setup |
| `SqliteDurabilityInterceptor.cs` | New | Applies durability before SaveChanges |
| `WriteDurabilityScope.cs` | New | Ambient opt-in for high-durability |
| `Program.cs` | Modify | Wire up interceptors and initializer |
| **All PageModels/Services** | **None** | Continue to work unchanged |

---

## Implementation Order

1. Create new files in `WikiWikiWorld.Data`:
   - `WriteDurability.cs`
   - `SqliteRetryExecutionStrategy.cs`
   - `SqlitePragmaInterceptor.cs`
   - `SqliteInitializer.cs`
   - `WriteDurabilityScope.cs`
   - `SqliteDurabilityInterceptor.cs`

2. Modify `Program.cs` to use the new components

3. Remove `WikiWikiWorld.Database` project:
   - Remove from `WikiWikiWorld.slnx`
   - Remove `<ProjectReference>` from `WikiWikiWorld.Data.csproj`
   - Remove `<ProjectReference>` from `WikiWikiWorld.Web.csproj`
   - Delete `src/WikiWikiWorld.Database/` directory

4. Build and test

---

## Testing Checklist

- [ ] Build succeeds with no warnings
- [ ] Application starts and initializes database
- [ ] Read operations work (view article)
- [ ] Write operations work (create/edit article)
- [ ] Verify WAL mode is active: run `PRAGMA journal_mode;` → should return `wal`
- [ ] Test high-durability write with `WriteDurabilityScope.High()`
- [ ] (Optional) Simulate concurrent writes to verify retry behavior
