<div align="center">

# Brainy

**A practical second brain for turning scattered knowledge into useful work.**

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Blazor](https://img.shields.io/badge/Blazor-Interactive%20Server-512BD4?style=flat-square&logo=blazor&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL%20Server-EF%20Core-CC2927?style=flat-square&logo=microsoftsqlserver&logoColor=white)

[Overview](#overview) | [Features](#features) | [Getting started](#getting-started) | [Development](#development)

</div>

## Overview

Brainy is a SaaS second-brain application built with .NET 10, Blazor, MudBlazor, Entity Framework Core, and SQL Server. It is inspired by Tiago Forte's PARA method and CODE workflow, with an emphasis on actionability rather than passive storage.

Capture notes and ideas, organize them into projects, areas, resources, and archives, then retrieve, summarize, and reuse that knowledge in real work.

> [!NOTE]
> AI assistant integration is implemented behind an application service boundary, but the default configuration disables AI features. Set an AI provider explicitly before using AI-assisted workflows.

## Features

- **Today dashboard** for current work, deadlines, overdue tasks, notifications, and project progress.
- **PARA organization** across projects, areas, resources, and archives.
- **Inbox processing** for captured notes and ideas that still need organization.
- **Notes and knowledge distillation** with highlights, summaries, sources, images, tags, and related-note relationships.
- **Tasks and planning** with priorities, due dates, subtasks, dependencies, recurrence, and project context.
- **Goals and milestones** for connecting longer-term outcomes to active projects.
- **Outputs** for turning stored knowledge into reusable deliverables.
- **Search and retrieval** across the user's own notes and content.
- **Per-user data isolation** enforced through ASP.NET Core Identity and application services.
- **Responsive Blazor UI** built with MudBlazor and interactive server rendering.

## Architecture

| Project | Responsibility |
| --- | --- |
| `Brainy.Domain` | Domain entities, enums, and shared domain contracts. |
| `Brainy.Application` | DTOs, service interfaces, business workflows, and AI abstractions. |
| `Brainy.Data` | Entity Framework Core context, SQL Server persistence, Identity, configurations, and migrations. |
| `Brainy.Web` | Blazor Web App, Interactive Server components, authentication endpoints, and the MudBlazor interface. |
| `Brainy.Application.Tests` | xUnit and FluentAssertions tests for application services. |

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

Pending EF Core migrations are applied automatically when the application starts. Register at `/Account/Register`; email confirmation is not required in the current configuration.

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

Development applies migrations at startup. CI only restores, builds, and tests; it does not modify a live database. Production deployments should apply the database migration before starting the new application version.

### Authentication and data ownership

Brainy uses ASP.NET Core Identity with cookie authentication. Principal entities such as `Note`, `Project`, `Area`, `Resource`, `Source`, `Output`, `Tag`, and `TaskItem` carry a required user relationship. Application services resolve the current user through `ICurrentUserService` and scope reads and writes to that user. Child records inherit ownership through their parent.

## Deployment

The release workflow publishes `Brainy.Web` and deploys it to Azure App Service when a GitHub release is published, or when started manually. It uses GitHub Actions with Azure OIDC authentication and performs a `/health` smoke check after deployment.

The health endpoint is available at `/health` and intentionally checks application liveness without querying the database.

## Resources

- [PARA method](https://fortelabs.com/blog/para/)
- [Progressive Summarization](https://fortelabs.com/blog/progressive-summarization-a-practical-technique-for-designing-better-summaries/)
- [.NET documentation](https://learn.microsoft.com/dotnet/)
- [Blazor documentation](https://learn.microsoft.com/aspnet/core/blazor/)
- [MudBlazor documentation](https://mudblazor.com/)
- [Entity Framework Core documentation](https://learn.microsoft.com/ef/core/)
