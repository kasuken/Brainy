---
description: This file describes the technical stack and coding guidelines for the Brainy project.
applyTo: "**"
---

# Brainy - Technical Stack Instructions

## Technology Stack

Brainy must be built using the following technologies:

### Frontend

* .NET 10
* Blazor Web App
* Interactive Server rendering by default
* MudBlazor for all UI components
* Responsive design for desktop, tablet, and mobile

Do not introduce alternative UI frameworks unless explicitly requested.

Avoid:

* Bootstrap components
* Radzen
* Fluent UI
* Ant Design
* Telerik
* Syncfusion

MudBlazor is the default component library.

### Backend

* ASP.NET Core (.NET 10)
* Minimal APIs when appropriate
* Service-based architecture
* Dependency Injection for all services

### Database

* SQL Server
* Entity Framework Core

Use:

* Code First approach
* Entity configurations
* Migrations
* Async database operations

Avoid:

* Dapper
* Raw SQL unless justified by performance requirements
* Stored procedures unless explicitly requested

### Authentication

Preferred options:

* Entra ID
* GitHub OAuth
* Google OAuth

Authentication implementation should be abstracted behind services.

### AI Integration

Use:

* OpenAI compatible APIs
* Azure OpenAI
* Local models through configurable providers

AI providers must be interchangeable.

Never couple business logic directly to a specific AI vendor.

### Architecture

Follow clean separation between:

* UI
* Application Services
* Domain
* Data Access

Business rules must not live inside Razor components.

Database access must not live inside Razor components.

### Entity Framework Guidelines

* Use IEntityTypeConfiguration<T>
* Use navigation properties where appropriate
* Use strongly typed identifiers when beneficial
* Use optimistic concurrency when needed
* Use AsNoTracking for read-only queries

All queries must be asynchronous.

### Blazor Guidelines

* Keep pages thin
* Move logic into services
* Create reusable MudBlazor components
* Prefer composition over inheritance
* Use proper loading states
* Handle errors gracefully

### Naming Conventions

#### Classes

* PascalCase

Examples:

* NoteService
* ProjectService
* ParaClassificationService

#### Interfaces

Prefix with I.

Examples:

* INoteService
* IProjectService

#### Database Tables

Singular entity names.

Examples:

* Note
* Project
* Area
* Resource

### Testing

Preferred:

* xUnit
* FluentAssertions

Business logic should be testable without UI dependencies.

### Performance

Prioritize:

* Fast startup
* Efficient queries
* Server-side filtering
* Pagination
* Caching where beneficial

Avoid premature optimization.

### Code Quality

Generated code should:

* Follow SOLID principles
* Be maintainable
* Be readable
* Use nullable reference types
* Avoid unnecessary abstractions
* Avoid overengineering

Favor simplicity whenever possible.
