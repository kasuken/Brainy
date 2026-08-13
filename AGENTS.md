# Brainy agent guidance

This file is the canonical entry point for automated contributors. More specific
instructions under `.github/instructions/` and `.github/skills/` apply when they
do not conflict with this file or with the existing implementation.

## Product and architecture

- Brainy is a .NET 10 Blazor Web App using Interactive Server rendering,
  MudBlazor, EF Core, SQL Server, ASP.NET Core Identity, PARA, and CODE.
- Preserve the Domain / Application / Data / Web separation. Razor components
  orchestrate UI only; data access and business invariants belong in services.
- Every read, mutation, and related-entity assignment must be scoped to the
  authenticated user. Validate both the primary entity and all supplied foreign IDs.
- Active workflows exclude archived records by default. Archive and restore must
  be reversible and must preserve why an item was archived.
- AI-generated data must retain provider/model, prompt version, source IDs, and
  the original generated value separately from later edits.
- Dates such as due dates are user-calendar dates; audit timestamps remain UTC.

## Current product decisions

- Today is the default execution surface.
- Tasks Hub and Tasks Calendar are intentional planning views, but every Task
  remains linked to and understandable in its Project context.
- MudBlazor is the only UI component framework.
- `Microsoft.Extensions.AI` is the current provider abstraction. Do not add an
  orchestration framework unless a concrete workflow requires it.
- Tests use xUnit and AwesomeAssertions. Follow existing naming and assertion conventions.

## Required validation

Run before handing off changes:

```powershell
dotnet restore Brainy.slnx
dotnet build Brainy.slnx --configuration Release --no-restore
dotnet test Brainy.slnx --configuration Release --no-build
dotnet list Brainy.slnx package --vulnerable --include-transitive
dotnet ef migrations has-pending-model-changes --project Brainy.Data --startup-project Brainy.Web --no-build --configuration Release
git diff --check
```

For user-visible or security-sensitive flows, add an integration or browser test
at the boundary where the defect was observable. EF InMemory tests are not proof
of SQL Server constraints, transactions, migrations, or rowversion behavior.
