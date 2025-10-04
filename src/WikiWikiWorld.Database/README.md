# WikiWikiWorld.Database

A reusable SQLite connection factory library with advanced features for .NET applications.

## Features

- **Connection Pooling**: Configurable read and write connection pools with concurrency limits
- **Automatic Retry Logic**: Handles transient SQLite BUSY errors with exponential backoff
- **WAL Mode**: Optimized for Write-Ahead Logging mode
- **Transaction Support**: Built-in transaction management with automatic rollback
- **Resource Management**: Proper async disposal patterns and connection lifecycle management
- **Thread-Safe**: Safe for concurrent access from multiple threads

## Installation

Add a project reference to `WikiWikiWorld.Database.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\WikiWikiWorld.Database\WikiWikiWorld.Database.csproj" />
</ItemGroup>
```

## Usage

### Basic Setup

```csharp
using WikiWikiWorld.Database;

// Create connection options (all parameters are optional with sensible defaults)
var options = new ConnectionOptions(
    BusyTimeoutMs: 5_000,           // Time to wait for locks
    CacheSizePages: -20_000,         // Negative = KB, positive = pages
    MmapSizeBytes: 2_147_483_648,    // Memory-mapped I/O size (2GB)
    PageSizeBytes: 8_192,            // Database page size
    ReadMaxConcurrency: 0,           // 0 = use processor count
    WriteMaxConcurrency: 1,          // Writes are serialized by default
    MaxRetryAttempts: 3,             // Retry attempts for BUSY errors
    RetryDelayMs: 50,                // Base delay for retry backoff
    ShutdownTimeoutMs: 10_000,       // Time to wait for connections during shutdown
    ProviderPooling: true            // Use ADO.NET connection pooling
);

// Create and initialize the factory
var factory = new DatabaseConnectionFactory(options);
await factory.InitializeAsync("path/to/database.db");

// Register in DI container (recommended)
services.AddSingleton<IDatabaseConnectionFactory>(factory);
```

### Using Default Options

```csharp
// Use default options
var factory = new DatabaseConnectionFactory(new ConnectionOptions());
await factory.InitializeAsync("path/to/database.db");
```

### Read Operations

```csharp
// Direct connection scope
await using var scope = await factory.GetConnectionAsync(ConnectionMode.Read);
var results = await scope.Connection.QueryAsync<MyModel>("SELECT * FROM MyTable");

// Or use the convenience method with automatic retry
var results = await factory.ExecuteWithRetryAsync(
    ConnectionMode.Read,
    async connection => await connection.QueryAsync<MyModel>("SELECT * FROM MyTable")
);
```

### Write Operations

```csharp
// Write with automatic retry
await factory.ExecuteWithRetryAsync(
    ConnectionMode.Write,
    async connection => await connection.ExecuteAsync(
        "INSERT INTO MyTable (Name) VALUES (@Name)",
        new { Name = "John" }
    )
);
```

### Transaction Support

```csharp
// Automatic transaction management with retry
var result = await factory.ExecuteWithRetryInTransactionAsync(
    async (connection, transaction) =>
    {
        // Multiple operations in a single transaction
        await connection.ExecuteAsync(
            "INSERT INTO Table1 (Value) VALUES (@Value)",
            new { Value = 123 },
            transaction
        );
        
        await connection.ExecuteAsync(
            "UPDATE Table2 SET Status = @Status WHERE Id = @Id",
            new { Status = "Active", Id = 1 },
            transaction
        );
        
        // Return a value if needed
        return await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM Table1",
            transaction: transaction
        );
    }
);
```

### Monitoring

```csharp
// Get current connection statistics
var (activeReads, activeWrites) = factory.GetStats();
Console.WriteLine($"Active reads: {activeReads}, Active writes: {activeWrites}");
```

### Shutdown

```csharp
// Graceful shutdown (waits for active connections)
await factory.ShutdownAsync();

// Or use DisposeAsync (doesn't throw on timeout)
await factory.DisposeAsync();
```

## Connection Modes

- **`ConnectionMode.Read`**: Opens the database in read-only mode. Multiple concurrent read connections are allowed.
- **`ConnectionMode.Write`**: Opens the database in read-write mode. Serialized by default (only one write at a time).

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `BusyTimeoutMs` | 5,000 | Maximum time to wait for database locks (milliseconds) |
| `CacheSizePages` | -20,000 | Cache size. Negative = KB, positive = pages. Default = 20MB |
| `MmapSizeBytes` | 2,147,483,648 | Memory-mapped I/O size in bytes (2GB) |
| `PageSizeBytes` | 8,192 | Database page size (must match existing DB or be set on creation) |
| `ReadMaxConcurrency` | CPU count | Maximum concurrent read connections (0 = use processor count) |
| `WriteMaxConcurrency` | 1 | Maximum concurrent write connections (recommended: 1) |
| `MaxRetryAttempts` | 3 | Number of retry attempts for BUSY errors |
| `RetryDelayMs` | 50 | Base delay for exponential backoff (milliseconds) |
| `ShutdownTimeoutMs` | 10,000 | Maximum time to wait during shutdown (milliseconds) |
| `ProviderPooling` | true | Use ADO.NET connection pooling |

## SQLite Pragmas

The factory automatically configures the following SQLite pragmas:

### Database-level (persistent):
- `journal_mode = WAL` - Write-Ahead Logging for better concurrency
- `synchronous = NORMAL` - Balanced durability/performance
- `page_size` - Configurable page size

### Connection-level:
- `busy_timeout` - Configurable lock timeout
- `cache_size` - Configurable cache size
- `mmap_size` - Memory-mapped I/O size
- `temp_store = MEMORY` - Store temp tables in memory
- `foreign_keys = ON` - Enable foreign key constraints

## Thread Safety

The `DatabaseConnectionFactory` is fully thread-safe and designed for concurrent access. Connection pools use semaphores to control concurrency limits.

## Error Handling

The factory automatically retries on these SQLite BUSY error codes:
- 5 (SQLITE_BUSY)
- 261 (SQLITE_BUSY_RECOVERY)
- 517 (SQLITE_BUSY_SNAPSHOT)  
- 773 (SQLITE_BUSY_TIMEOUT)

Retries use exponential backoff with full jitter to avoid thundering herd problems.

## Best Practices

1. **Use DI**: Register the factory as a singleton in your DI container
2. **Single Instance**: Create only one factory instance per database file
3. **Initialization**: Always call `InitializeAsync()` before use
4. **Shutdown**: Call `ShutdownAsync()` or `DisposeAsync()` on application shutdown
5. **Write Concurrency**: Keep `WriteMaxConcurrency = 1` unless you have specific needs
6. **Connection Scopes**: Always use `await using` with connection scopes
7. **Transactions**: Use `ExecuteWithRetryInTransactionAsync` for multi-step write operations

## Dependencies

- .NET 9.0
- Microsoft.Data.Sqlite 9.0.0

## License

Part of the WikiWikiWorld project.
