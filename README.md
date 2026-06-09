# Brainy

A SaaS second-brain app built on .NET 10, Blazor, MudBlazor, and SQL Server.  
Inspired by Tiago Forte's PARA method and CODE workflow.

---

## Solution Structure

| Project | Role |
|---|---|
| `Brainy.Domain` | Entities, enums — no external dependencies |
| `Brainy.Application` | Interfaces, DTOs, application services |
| `Brainy.Data` | EF Core `BrainyDbContext`, migrations, SQL Server |
| `Brainy.Web` | Blazor Web App (Interactive Server), MudBlazor UI |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server (LocalDB, Express, or full) — LocalDB ships with Visual Studio

---

## Getting Started

### 1. Configure the connection string

Edit `Brainy.Web/appsettings.Development.json` (or use User Secrets):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=Brainy;Trusted_Connection=True;TrustServerCertificate=true"
  }
}
```

### 2. Run the application

```bash
dotnet run --project Brainy.Web
```

Migrations are applied automatically on startup. No manual `dotnet ef database update` step is required in development.

---

## Database Migrations

### Strategy

| Scenario | Approach |
|---|---|
| Development | Migrations are applied automatically at startup via `DatabaseInitializer.MigrateAsync()` |
| Production | Run `dotnet ef database update` as part of the deployment pipeline **before** the new app version starts |
| CI | The build pipeline compiles and tests; it does **not** apply migrations to a live database |

### Creating a new migration

From the repository root:

```bash
dotnet ef migrations add <MigrationName> \
  --project Brainy.Data \
  --startup-project Brainy.Web
```

### Applying migrations manually

```bash
dotnet ef database update \
  --project Brainy.Data \
  --startup-project Brainy.Web
```

### Reverting a migration

```bash
# Roll back to a specific migration
dotnet ef database update <PreviousMigrationName> \
  --project Brainy.Data \
  --startup-project Brainy.Web

# Remove the last unapplied migration file
dotnet ef migrations remove \
  --project Brainy.Data \
  --startup-project Brainy.Web
```

### Generating a SQL script (for production deployments)

```bash
dotnet ef migrations script \
  --idempotent \
  --project Brainy.Data \
  --startup-project Brainy.Web \
  --output migrations.sql
```

The `--idempotent` flag makes the script safe to run multiple times — it skips migrations already recorded in the `__EFMigrationsHistory` table.

---

## Building & Testing

```bash
dotnet build
dotnet test
```
