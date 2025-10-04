# WikiWikiWorld

A wiki platform built with ASP.NET Core and SQLite.

## Project Structure

- **WikiWikiWorld.Data** - Data models and repositories
- **WikiWikiWorld.Database** - Database connection factory and utilities
- **WikiWikiWorld.Web** - ASP.NET Core web application

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- SQLite

### Building

```bash
dotnet build WikiWikiWorld.slnx
```

### Running

```bash
dotnet watch run --project src/WikiWikiWorld.Web/WikiWikiWorld.Web.csproj
```

### Testing

```bash
dotnet test WikiWikiWorld.slnx
```

## License

TBD
