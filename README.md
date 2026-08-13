<div align="center">

# Brainy

**A practical second brain for turning scattered knowledge into useful work.**

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Blazor](https://img.shields.io/badge/Blazor-Interactive%20Server-512BD4?style=flat-square&logo=blazor&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL%20Server-EF%20Core-CC2927?style=flat-square&logo=microsoftsqlserver&logoColor=white)

[Overview](#overview) | [Features](#features) | [Getting started](#getting-started) | [Development](#development)

</div>

<img width="1889" height="954" alt="image" src="https://github.com/user-attachments/assets/1d503607-6ed0-4070-8d55-826c8d08c62d" />

## Overview

Brainy is a SaaS second-brain application built with .NET 10, Blazor, MudBlazor, Entity Framework Core, and SQL Server. It is inspired by Tiago Forte's PARA method and CODE workflow, with an emphasis on actionability rather than passive storage.

Capture notes and ideas, organize them into projects, areas, resources, and archives, then retrieve, summarize, and reuse that knowledge in real work.

> [!NOTE]
> AI assistant integration is implemented behind an application service boundary, but the default configuration disables AI features. Set an AI provider explicitly before using AI-assisted workflows.

## Features

- **Today dashboard** for current work, deadlines, overdue tasks, and project progress.
- **PARA organization** across projects, areas, resources, and archives.
- **Inbox processing** for captured notes and ideas that still need organization.
- **Notes and knowledge distillation** with highlights, summaries, sources, images, related-note relationships, and action-item promotion into project tasks.
- **Tasks and planning** with priorities, due dates, subtasks, recurring occurrences, archive/restore, project context, and prerequisite management with cycle protection.
- **Goals and milestones** for connecting longer-term outcomes to active projects.
- **Outputs** for turning stored knowledge into reusable Markdown deliverables, with preview, copy, download, and AI provenance.
- **Search and retrieval** across notes, outputs, projects, tasks, areas, resources, ideas, and goals.
- **Per-user data isolation** enforced through ASP.NET Core Identity and application services.
- **Data portability** through a versioned JSON export of the signed-in user's content and relationships.
- **Responsive Blazor UI** built with MudBlazor and interactive server rendering.

## Architecture

| Project | Responsibility |
| --- | --- |
| `Brainy.Domain` | Domain entities, enums, and shared domain contracts. |
| `Brainy.Application` | DTOs, service interfaces, business workflows, and AI abstractions. |
| `Brainy.Data` | Entity Framework Core context, SQL Server persistence, Identity, configurations, and migrations. |
| `Brainy.Web` | Blazor Web App, Interactive Server components, authentication endpoints, and the MudBlazor interface. |
| `Brainy.Application.Tests` | xUnit assertion tests for application services. |
| `Brainy.Web.Tests` | Security and public web-surface integration tests. |
| `Brainy.Data.IntegrationTests` | SQL Server migration, constraint, and rowversion tests. |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server, SQL Server Express, or LocalDB
- [Entity Framework Core CLI](https://learn.microsoft.com/ef/core/cli/dotnet) for manually managing migrations

### Configure the database

The development configuration uses LocalDB by default:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=Brainy;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true"
  }
}
```

Place overrides in `Brainy.Web/appsettings.Development.json`, User Secrets, or environment variables. Do not commit production credentials.

### Run locally

From the repository root:

```bash
dotnet run --project Brainy.Web
```

The development launch profiles use `http://localhost:5255` and `https://localhost:7107`.

Pending EF Core migrations are applied automatically when the application starts unless `Database:ApplyMigrationsOnStartup` is disabled. Local development enables `/Account/Register`; production registration is closed by default and must be deliberately enabled with `Identity:AllowRegistration`. Account confirmation is separately configurable through `Identity:RequireConfirmedAccount`.

## Development

### Build and test

```bash
dotnet restore
dotnet build
dotnet test
```

For release-style validation matching CI:

```bash
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release --verbosity normal
```

### Database migrations

Create a migration from the repository root:

```bash
dotnet ef migrations add <MigrationName> --project Brainy.Data --startup-project Brainy.Web
```

Apply migrations manually:

```bash
dotnet ef database update --project Brainy.Data --startup-project Brainy.Web
```

Generate an idempotent SQL script for a controlled deployment:

```bash
dotnet ef migrations script --idempotent --project Brainy.Data --startup-project Brainy.Web --output migrations.sql
```

Startup migration is enabled by default in every environment. CI migrates a disposable SQL Server database to validate the schema, but it never modifies production. The production SQL endpoint is private, so migration currently occurs during application startup. Review every migration before release and use a dedicated private-network migration job when that infrastructure is available.

### Authentication and data ownership

Brainy uses ASP.NET Core Identity with cookie authentication. Principal entities such as `Note`, `Project`, `Area`, `Resource`, `Source`, `Output`, `Tag`, and `TaskItem` carry a required user relationship. Application services resolve the current user through `ICurrentUserService` and scope reads and writes to that user. Child records inherit ownership through their parent.

## Deployment

The release workflow builds, audits, tests, verifies migrations, publishes `Brainy.Web`, and deploys it to Azure App Service when a GitHub release is published or the workflow is started manually. It uses GitHub Actions with Azure OIDC authentication and requires approval through the protected `production` environment.

`/health/live` and the backwards-compatible `/health` endpoint check the process only. `/health/ready` checks SQL connectivity and is used by deployment validation. See [`docs/production-runbook.md`](docs/production-runbook.md) for release, rollback, and recovery procedures.

## Resources

- [PARA method](https://fortelabs.com/blog/para/)
- [Progressive Summarization](https://fortelabs.com/blog/progressive-summarization-a-practical-technique-for-designing-better-summaries/)
- [.NET documentation](https://learn.microsoft.com/dotnet/)
- [Blazor documentation](https://learn.microsoft.com/aspnet/core/blazor/)
- [MudBlazor documentation](https://mudblazor.com/)
- [Entity Framework Core documentation](https://learn.microsoft.com/ef/core/)
