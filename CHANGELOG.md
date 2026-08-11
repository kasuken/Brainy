# Changelog

## [1.7.0] - 2026-08-11

### Fixed

- **Horizontal scroll eliminated** — `overflow-x: hidden` added to `body` in both `app.css` and `brainy-design.css` so wide content no longer causes a horizontal scrollbar on any page.
- **Task metadata wraps correctly on narrow screens** — `.td-task-meta` on the Today page now has `flex-wrap: wrap` and `min-width: 0` so project badges and due-date chips stack instead of overflowing.
- **Project label text truncated with ellipsis** — the project chip inside task rows now has `white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 100%` so long project names are clipped cleanly rather than breaking layout.

---

## [1.6.0] - 2026-08-11

### Fixed

- **SignalR message size limit increased to 512 KB** — the default SignalR hub limit (32 KB) was silently dropping large note pastes and crashing the circuit, causing notes to be saved without their content. `MaximumReceiveMessageSize` is now set to 512 KB on the Blazor Server hub so large notes are reliably transmitted.

---

## [1.5.0] - 2026-08-11

### Added

- **Create note directly from a project** — the Project Detail page now has two separate note actions: **New note** (opens the full note editor pre-linked to the project) and **Link existing** (links an already-existing note). Previously there was a single modal with two tabs.
- `ProjectId` parameter on `NoteEditorDialog`: when set, a newly created note is automatically linked to that project on save.
- Note status can now be selected when **creating** a note (the status dropdown was previously only shown in edit mode).
- Contextual help tip in `NoteEditorDialog`: shows "This note will be linked to the current project" when opened from a project, and the standard inbox tip otherwise.

### Changed

- **Note editing from Project Detail page** now opens inline in `NoteEditorDialog` instead of navigating to a separate page; the page reloads after closing so changes are immediately visible.
- **Note editing from Search page** now opens `NoteEditorDialog` as a dialog instead of navigating away, keeping the user in context.
- `ProjectNoteDialog` simplified to a focused "Link Existing Note" dialog; the "New Note" tab was removed in favour of the dedicated `NoteEditorDialog` flow.
- `/notes/{id:guid}` route now redirects to `/notes?open={id}` (previously redirected to `/notes` without preserving the note id), enabling deep-link support for direct note access from external links, search results, and notifications.
- `Notes` page auto-opens the editor dialog when the `open` query parameter is present, completing the deep-link flow.

---

## [1.4.0] - 2026-08-11

### Added

- **In Progress widget on Today page** — tasks whose status is *In Progress* now appear at the top of the execution flow, above Overdue and Due Today, so active work is always visible at a glance.
- `InProgress` list added to `TodayAggregateDto` and populated by `TodayService`.
- New `InProgressWidget` Blazor component with collapsible section, task count, and quick-action buttons (complete, edit).
- On wide screens (≥ 1 200 px) the In Progress section spans all grid columns for maximum visibility.
- Today empty-state check updated to account for the new In Progress section.
- Help tip updated to mention the In Progress section in the onboarding overlay.

---

## [1.3.0] - 2026-08-10

### Fixed

- Archiving a task now clears its `IsCurrentTask` flag (`TaskService`, `TasksHubService`).
- Completing or archiving a project now clears `IsCurrentTask` on all affected tasks (`ProjectService`).
- Auto-completing a parent task when all subtasks finish now clears its `IsCurrentTask` flag (`TaskService`).
- Completing a task via the Tasks Hub bulk-update path now clears `IsCurrentTask` (`TasksHubService`).

---

## [1.2.0] - 2026-08-10

### Fixed

- Completing a task now automatically clears its `IsCurrentTask` flag so it no longer appears as the user's active focus on the Today screen.

---

## [1.1.0] - 2026-08-10

### Added

- Idea status lifecycle workflow (`Captured → Reviewing → Validated → Developing → Completed / Archived / Cancelled`) with an expanded `IdeaStatus` enum.
- Redesigned Idea detail page with inline status transitions, metrics, and review controls.
- Updated Ideas list page and dashboard to reflect new lifecycle statuses and filters.
- `UpdateIdeaWorkflow` database migration for the new idea columns.

### Removed

- `ConvertIdeaToTaskDialog` component replaced by the new inline workflow in `IdeaDetailPage`.

---

## [1.0.0] - 2026-08-10

### Added

**Infrastructure & architecture**

- Solution skeleton with `Brainy.Domain`, `Brainy.Application`, `Brainy.Data`, and `Brainy.Web` projects.
- ASP.NET Core Identity with cookie authentication and per-user data ownership. All principal entities carry a `UserId` foreign key; application services scope every read and write to the current user.
- Entity Framework Core 10 with SQL Server, Code First migrations, and `IEntityTypeConfiguration<T>` configurations.
- Automatic migration on startup via `DatabaseInitializer.MigrateAsync()`.
- EF Core retry-on-failure resilience strategy for serverless SQL.
- `RowVersion` concurrency tokens on multiple tables for optimistic concurrency.
- `/health` liveness endpoint for Azure App Service health checks.
- Development data seeder for local testing.
- GitHub Actions CI workflow (restore → build → test on every push and pull request to `main`).
- GitHub Actions release workflow: publishes `Brainy.Web`, deploys to Azure App Service via OIDC authentication, and performs a `/health` smoke test.

**Notes**

- Note CRUD service with unit tests, Note list page, and Note editor as a MudBlazor modal dialog.
- `AiSummary` field on the `Note` entity.
- Note image upload and serving via `NoteImage` entity and dedicated streaming endpoints.
- Note relationships: link notes together manually and related-notes engine with auto-refresh.
- Note archiving functionality.
- Bulk PARA category move for notes.

**PARA & Inbox**

- PARA domain service layer (`ParaSummaryService`) and PARA Dashboard page.
- Inbox capture-first workflow and bulk inbox processing with destination selection.
- Search page for full-text note lookup.

**Projects**

- Project CRUD service, Project list page (search, filter, sort), and Project detail page with full workspace view.
- Project entity enhanced for full PARA compliance with status lifecycle model (`Active → On Hold → Completed → Archived`).
- Critical, High, Medium, and Low priority levels.
- Automatic project progress calculation based on task completion.
- Project task board with Kanban view and task list view with sorting and filtering.
- Project completion workflow, archive workflow, and due date monitoring.
- Project dashboard widget.
- Notes linked to projects.
- Required Area validation enforced on Project creation and editing.

**Tasks**

- `TaskItem` entity with status model, priority model, `CompletedDate`, and `TaskComplexity` (Simple / Moderate / Complex / Epic) enum with a database column.
- Task CRUD service.
- Subtask support with nested display, cascading completion, and overdue/due-today counts per parent.
- Inline task rescheduling across multiple widgets and a focus-picker dialog.
- Task list view with sorting, filtering, and bulk operations.

**Today dashboard (Epic 9, issues #41–#80)**

- Today page showing current task, high-priority project work, tasks due today, overdue tasks, tasks due this week, and upcoming tasks.
- Archived tasks excluded from all active workflows and views.

**Areas (Epic 11, issues #81–#88)**

- Area CRUD service, list page, and detail page.
- Emoji support for Areas.

**Resources**

- Resource editor and management page.
- Emoji support for Resources.

**Outputs (Epic, issues #219–#239)**

- Output entity with status lifecycle (`Draft → In Progress → Review → Published → Archived`) and output type classification.
- Output CRUD service, list page, and detail page.
- AI-assisted output generation dialog (`OutputAiGenerationDialog`) using stored notes as source material.

**Goals**

- Goal and `GoalMilestone` entities with activity tracking.
- Goal CRUD service, list page, and detail page with milestone management and activity log.
- Note and goal management enhancements (link goals to projects, progress metrics).

**Tasks Hub**

- Tasks Hub page aggregating tasks across all projects.
- Tasks Hub widgets with inline editing and status updates.
- Today widgets with inline editing.

**Calendar**

- Calendar service and calendar view for tasks and deadlines.

**Quick Capture**

- Quick Capture global dialog with file attachment support.
- `brainyCapture.js` for browser-level capture integration.

**Onboarding**

- Contextual help system with onboarding dialogs for key pages (Notes, Projects, Areas, Resources, Inbox, Outputs).

### Fixed

- EF Core "second operation started on this context" concurrency error in the search page and outputs widget.
- PARA summary queries run sequentially to avoid `DbContext` concurrency errors.
- Greeting text on the Home page corrected to a static "Today" heading.

### Changed

- AI assistant replaced by a no-op `NullAiAssistant` (`AddDisabledAiAssistant()`); AI features are preserved but disabled by default until a provider is configured.
- Note editor migrated from an inline page section to a MudBlazor modal dialog.

### Removed

- Unused `TodayTaskOrderingHelper`, `Counter`, and `Weather` stub components.
