# Write Durability (SQLite `PRAGMA synchronous`)

**TL;DR for future devs:** every database write is power-loss safe (`synchronous = FULL`)
by default. You never need to do anything for important writes. When you build a
**high-volume ephemeral** write path — likes, upvotes, notifications, view counters,
presence, autosaves — wrap the write in `WriteDurabilityScope.Normal()` to skip the
per-commit fsync and get much higher write throughput.

```csharp
// Ephemeral hot path (e.g. a "like" endpoint). The scope must wrap the WHOLE write,
// including any transaction begin — not just SaveChangesAsync.
using (WriteDurabilityScope.Normal())
{
	Context.ArticleLikes.Add(NewLike);
	await Context.SaveChangesAsync(CancellationToken);
}
```

With an explicit transaction and execution strategy, the scope goes **outside**:

```csharp
using (WriteDurabilityScope.Normal())
{
	IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();
	await Strategy.ExecuteAsync(async CT =>
	{
		await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);
		// ... reads and writes ...
		await Context.SaveChangesAsync(CT);
		await Transaction.CommitAsync(CT);
	}, CancellationToken);
}
```

## What the two levels mean

The database runs in WAL mode (set persistently at startup by `SqliteInitializer`).

| Level | Pragma | Cost | Guarantee |
|---|---|---|---|
| `WriteDurability.High` (**default**) | `synchronous = FULL` | one WAL fsync per commit (~0.5–2 ms on SSD) | commit survives power loss / kernel panic |
| `WriteDurability.Normal` (opt-in) | `synchronous = NORMAL` | no fsync at commit | most recent commit(s) may be lost on power failure; **database can never be corrupted** either way |

Two useful WAL properties:

- **Ordering is safe.** A Normal commit after a High commit can never "un-durable" the
  High one — its fsync already happened.
- **High commits are durability barriers.** An fsync syncs the whole WAL file, so every
  earlier Normal commit sitting in the WAL becomes durable for free whenever any High
  commit (or a checkpoint) lands. The loss window for ephemeral writes is therefore
  "since the last High commit or checkpoint," which is short in practice.

## Why High is the default

The failure modes are asymmetric:

- Forgetting to mark an **important** write would silently risk data loss — invisible
  until a power failure eats an account creation. (This was the original design: Normal
  by default, opt *up* to High. Nearly every call site ended up opting up, and a pragma
  ordering bug meant the opt-up was silently ignored inside transactions anyway.)
- Forgetting to mark an **ephemeral** write just costs ~1 ms per commit — a performance
  issue that shows up in profiling and is trivially fixed by adding the scope.

Safe-by-default, fast-by-annotation. The ephemeral hot paths are a small, well-known
set of endpoints; annotate those.

## What qualifies for `Normal()`

Writes where the user can shrug-and-retry and nothing is truly lost:

- likes, reactions, upvotes/downvotes
- notifications, unread markers, presence indicators
- view counts, click tracking, analytics
- drafts / autosaves, cache refreshes

Everything else — identity and security changes (registration, passwords, lockout,
roles), articles, file revisions, deletes/reverts, messages, talk posts, uploads,
bookmarks — stays on the default. **When in doubt, do nothing** (the default is safe).

## How it works (mechanics)

`PRAGMA synchronous` is **per-connection** and cannot be changed while a transaction is
active. `SqliteDurabilityInterceptor` therefore applies it at the three moments where no
transaction can exist:

1. **Connection opened** — deterministic baseline on every pooled connection; also
   covers single-statement saves where EF Core skips the implicit transaction (exactly
   the shape of ephemeral hot-path writes).
2. **Transaction starting** — fires before `BEGIN` is issued, so explicit transactions
   commit at the level of the scope that was ambient *when the transaction started*.
   This is why the scope must wrap `BeginTransactionAsync`, not just `SaveChangesAsync`.
3. **SavingChanges** — only for non-transactional saves on a connection opened earlier
   and kept open (where the ambient scope may have changed since open).

Pragma failures are **not** swallowed. With this ordering the pragma can only be
rejected if a transaction was started behind EF Core's back — a bug that must surface
rather than silently downgrade durability.

The scope is keyed to the **logical async call context** (`AsyncLocal`), not to a
`DbContext` or connection instance. It flows through `await` continuations, execution
strategy lambdas, and forked parallel branches (`Parallel.ForEachAsync`, `Task.WhenAll`)
via `ExecutionContext` capture, and a scope entered inside one branch is isolated to that
branch. The pragma is still applied **per connection** by the interceptor, using whatever
level is ambient when that connection opens / its transaction starts. So for parallel work,
give each branch its own `DbContext` (never share one across concurrent operations — EF Core
forbids it regardless of durability); the scope then flows into each branch correctly, and a
branch needing a different level just wraps its own unit of work in `using (WriteDurabilityScope.Normal())`.

## Verifying

```
dotnet run scripts/verify-write-durability.cs
```

The script asserts, against a real SQLite database with the production interceptors:
default-inside-transaction = FULL, `Normal()`-inside-transaction = NORMAL, level restored
after scope disposal, and the open-connection (no transaction) path. Run it after any
change to `SqliteDurabilityInterceptor`, `WriteDurabilityScope`, or connection/pragma
setup.

## History

- **2026-06-10** — Default flipped from Normal-with-opt-up-to-High to
  **High-with-opt-down-to-Normal**, and the pragma moved from `SavingChanges` (where it
  silently failed inside transactions) to connection-open + transaction-start. See
  finding #1 in [data-layer-review-2026-06-09.md](data-layer-review-2026-06-09.md).
