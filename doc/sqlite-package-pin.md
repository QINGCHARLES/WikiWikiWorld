# SQLite native package pin (temporary)

**TL;DR for future devs:** `src/WikiWikiWorld.Data/WikiWikiWorld.Data.csproj` pins
`SQLitePCLRaw.bundle_e_sqlite3` to `3.0.3` to dodge a SQLite CVE in the version EF Core
pulls transitively. **Remove this pin when we upgrade to EF Core 11+**, which adopts the
3.x line itself. See "When to remove it" below.

## Why the pin exists

`Microsoft.EntityFrameworkCore.Sqlite` 10.0.x (we are on 10.0.0) depends transitively on
`SQLitePCLRaw.bundle_e_sqlite3` **2.1.11**, whose native binary package
`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 bundles a SQLite build older than 3.50.2 and is flagged by:

- **GHSA-2m69-gcr7-jv3q** / **CVE-2025-6965** — a SQLite defect (aggregate-term overflow)
  fixed in **SQLite 3.50.2**.

The repo builds with `TreatWarningsAsErrors` (`Directory.Build.props`), so the NuGet audit
warning **NU1903** becomes a build-breaking error on the whole solution.

Bumping EF Core does **not** help: even the latest 10.0.x patch (10.0.9) still references
`SQLitePCLRaw.bundle_e_sqlite3` 2.1.11. Microsoft only moved to the 3.x line in **EF Core
11** (see below).

## What the pin does

```xml
<!-- in WikiWikiWorld.Data.csproj -->
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.3" />
```

SQLitePCLRaw 3.x **renamed/restructured the native binary package**: the old
`SQLitePCLRaw.lib.e_sqlite3` is replaced by `SourceGear.sqlite3` (3.50.4.5 = SQLite
**3.50.4**, packaging build 5). So pinning the 3.x bundle doesn't *upgrade* the flagged
package — it **removes it from the dependency graph entirely** and brings in the patched
native binary plus the aligned managed packages (`core` / `provider` / `config` 3.0.3).

A direct (top-level) `PackageReference` overrides the transitive one EF Core would
otherwise win with. **Not** `PrivateAssets="all"` (unlike the `System.Security.Cryptography.Xml`
pin in the same file): the SQLite native binary is a runtime dependency that must flow
through to the referencing app (`WikiWikiWorld.Web`), or no `e_sqlite3` native lib gets
deployed.

## Is the 3.x line safe? (provenance + compatibility)

Yes. `SourceGear.sqlite3`, the old `SQLitePCLRaw.lib.e_sqlite3`, and `SQLitePCLRaw.core`
are **all authored by Eric Sink** — SourceGear is his company, and he has maintained
SQLitePCLRaw (`github.com/ericsink/SQLitePCL.raw`) for ~15 years. The 3.x rename is the
same maintainer restructuring packaging, not a new third party.

In his own words on the EF Core update issue: *"SQLitePCLRaw 3.x is entirely about changes
in packaging — it includes no functional code changes."* The managed `core` API surface
that `Microsoft.Data.Sqlite` binds to is unchanged 2.x→3.x. The only real forward step is
the bundled **SQLite engine** version, which sits inside SQLite's strong backward-
compatibility guarantees (on-disk file format stable since 2004). Verified at runtime here
by `scripts/verify-write-durability.cs` passing against the 3.x native.

## When to remove it

Microsoft adopted SQLitePCLRaw 3.x in **EF Core 11.0.0**:

- dotnet/efcore **#36535** "Update to SQLitePCLRaw 3.x" — merged via #36551,
  **closed 2025-09-30**, milestone **11.0.0** (preview 1). EF Core 11 GA ships with
  .NET 11, expected **~November 2026**.

So **as part of upgrading this project to EF Core 11+**:

1. Delete the `SQLitePCLRaw.bundle_e_sqlite3` pin from `WikiWikiWorld.Data.csproj` (and
   this note's reference to it). EF Core 11 references the 3.x bundle itself.
2. Rebuild and verify (below).

**Do not just leave the pin in after upgrading.** A direct `PackageReference` is also a
*ceiling*: if EF Core 11 wants a 3.x bundle newer than our pinned `3.0.3`, NuGet raises a
downgrade error (**NU1605**), which `TreatWarningsAsErrors` turns into a build break. At
best the stale pin is a redundant no-op; at worst it holds back EF Core's chosen version.
Removing it is the correct action.

## Verifying (after any change to the pin or EF Core version)

```powershell
dotnet build WikiWikiWorld.slnx                              # 0 warnings, 0 errors
dotnet list WikiWikiWorld.slnx package --vulnerable --include-transitive   # "no vulnerable packages"
dotnet list src/WikiWikiWorld.Web/WikiWikiWorld.Web.csproj package --include-transitive   # SQLitePCLRaw 3.x, SourceGear.sqlite3; no lib.e_sqlite3 2.1.11
dotnet run scripts/verify-write-durability.cs               # opens a real SQLite DB on the resolved native
```

The deployed native binary's version can be read directly, e.g.
`(Get-Item src/WikiWikiWorld.Web/bin/Debug/net10.0/runtimes/win-x64/native/e_sqlite3.dll).VersionInfo.FileVersion`
should be `3.50.4.5` (≥ 3.50.2, the CVE-2025-6965 fix).

## History

- **2026-06-20** — Pinned `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 to clear NU1903
  (GHSA-2m69-gcr7-jv3q / CVE-2025-6965) that blocked the solution build under
  `TreatWarningsAsErrors`. EF Core 10.0.x still references the vulnerable 2.1.11 bundle;
  the fix lands upstream in EF Core 11 (dotnet/efcore#36535).
