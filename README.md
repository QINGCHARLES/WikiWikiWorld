# WikiWikiWorld

WikiWikiWorld is an ASP.NET Core Razor Pages wiki platform. Magazedia is the current configured implementation, in the same way Wikipedia is one implementation of MediaWiki. Implementation-specific content and settings should live in configuration or runtime data rather than in the reusable platform code.

The application uses SQLite for persistence and stores uploaded runtime media under a local `data/` directory.

## Projects

- `src/WikiWikiWorld.Data` - EF Core models, SQLite setup, query specifications, and database scripts.
- `src/WikiWikiWorld.Web` - Razor Pages web application, API controllers, Markdown extensions, and static assets.

## Prerequisites

- .NET 10 SDK or later.
- SQLite support through the bundled `Microsoft.Data.Sqlite` package.

## Build

PowerShell:

```powershell
dotnet build WikiWikiWorld.slnx
```

Bash:

```bash
dotnet build WikiWikiWorld.slnx
```

The solution is configured to treat warnings as errors.

## Run Locally

PowerShell:

```powershell
dotnet watch run --project src/WikiWikiWorld.Web/WikiWikiWorld.Web.csproj
```

Bash:

```bash
dotnet watch run --project src/WikiWikiWorld.Web/WikiWikiWorld.Web.csproj
```

Local runtime data is written under `data/`, which is intentionally ignored by Git.

## Tests

There are no dedicated test projects in the solution yet. Use `dotnet build WikiWikiWorld.slnx` as the current verification command until tests are added.

## Project Notes

- Read `PROJECT.md` for local-only implementation context before working on the application.
- Uploaded site files and SQLite databases are local runtime data and should not be committed.
- The solution uses the `.slnx` format.

## License

License information has not been added yet.
