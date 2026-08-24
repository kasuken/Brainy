# Changelog

## [5.10.3] - 2026-08-24

### Improved

- **Weekly selection counts** — corrected weekly project selection counting to use task-level selection membership, avoiding query translation failures and accurately reporting the number of selected tasks.
- **Weekly planning regression coverage** — added a service test covering selection counts for projects with both selected and unselected tasks.

---

## [5.10.2] - 2026-08-24

### Improved

- **Weekly planning rendering** — simplified attention-task markup into declarative Razor and added stable keys for weekly task groups and attention cards, improving list updates and component state preservation.
- **Week loading heading** — the Week page now shows a clean `Week` heading while weekly data is loading instead of displaying a placeholder week number.

---

## [5.10.1] - 2026-08-24

### Fixed

- **Weekly task capture interaction** — corrected the `Add to week` button event callback typing so the weekly planning page compiles and handles the action reliably.

---

## [5.10.0] - 2026-08-24

### Added

- **Weekly planning workspace** — a new Week view helps users choose the projects and tasks they are committing to for the current week, review next actions, and keep planning connected to project context.
- **Weekly task selections** — weekly commitments are persisted per user and week, with support for adding and removing tasks, setting the current focus, and carrying unfinished selections forward from the previous week.
- **Weekly planning coverage** — added application-service, SQL Server integration, and production-surface tests for weekly planning, persistence, carry-forward behavior, and navigation.

### Improved

- **Execution navigation** — Today and Week now share a view switch so users can move between daily execution and weekly planning without losing context.
- **Weekly project planning** — project status and task actions can be updated directly from the weekly workspace, with replanning and attention details surfaced alongside each commitment.

---

## [5.9.0] - 2026-08-22

### Added

- **Today subtask management** — Today task cards now show non-archived subtasks inline, including their status and due date, so users can review and update task progress without leaving the daily workspace.

### Improved

- **Task status actions across Today widgets** — users can complete, reopen, or move tasks and subtasks to In Progress directly from the Today dashboard, with refreshed sections after each change.
- **Today task data** — service projections now include ordered subtask details while preserving the existing project and task context.
- **Today regression coverage** — added tests for subtask data and Today task behavior introduced by the new workflow.

---

## [5.8.0] - 2026-08-22

### Improved

- **User time-zone service concurrency** — registered `IUserTimeZoneService` as transient so concurrent Blazor SSR consumers keep their captured database contexts isolated.
- **Dependency-injection regression coverage** — added a test that verifies the user time-zone service keeps its transient lifetime.

---

## [5.7.0] - 2026-08-22

### Added

- **Tasks Hub project scope filtering** — task planning views now support explicit project scopes while keeping active projects as the default, making it easier to focus on the right project context.

### Improved

- **Development cookie compatibility** — authentication and antiforgery cookies now follow the request scheme during local development while retaining secure-only cookies in production.
- **Project workspace coverage** — added regression coverage for loading project details with top-level tasks and completed subtasks.

---

## [5.6.0] - 2026-08-22

### Added

- **Quick task due dates** — the quick task dialog now lets users add an optional due date during capture, making lightweight task creation better aligned with calendar and Today planning workflows.
- **Today dashboard presets** — dashboard preferences now include preset choices for minimal and full widget layouts.

### Improved

- **User-calendar date handling** — task, project, goal, calendar, and Today surfaces now compare due and overdue work against the user's current date instead of a server-centric date.
- **Current focus detail** — the current task widget now exposes richer task context, including due-date and project information surfaced from the application services.
- **Pricing and registration defaults** — public pricing now reflects the latest currency and amounts, and registration is enabled by default for new deployments.

---

## [5.5.0] - 2026-08-20

### Added

- **Neurodivergent-friendly product messaging** — the public site now highlights Brainy's frictionless capture, calm Today screen, predictable low-stimulation interface, and non-judgmental deadline support for ADHD and AuDHD users.
- **Focus-support feature detail** — the Features page now includes a dedicated neurodivergent-friendly focus capability alongside Brainy's existing PARA and CODE workflows.

### Improved

- **Marketing stylesheet delivery** — public pages now load their dedicated styling reliably even when individual routes provide page titles and metadata.
- **Marketing regression coverage** — production-surface verification now checks that the landing page serves its marketing stylesheet.

---

## [5.4.0] - 2026-08-20

### Added

- **Public Brainy marketing site** — a new responsive landing page introduces Brainy's PARA organization model, CODE workflow, and Today execution surface to prospective users.
- **Features and pricing pages** — dedicated product and pricing routes provide a clearer path from product discovery to account registration.
- **Marketing navigation shell** — public pages now share a branded header and footer with direct links to features, pricing, sign-in, and registration.

### Improved

- **Authenticated entry path** — signed-in visitors who reach the public landing page are redirected to Today, keeping active users focused on their work.
- **Production-surface coverage** — added route and markup checks for the public marketing experience and its navigation.

---

## [5.3.2] - 2026-08-20

### Improved

- **Action-item project context** — action items now include the linked task's project ID and name, making their project context available to consuming workflows.
- **SQL Server query compatibility** — action-item retrieval now uses EF Core projections verified against SQL Server, with integration coverage for linked and unlinked task scenarios.

---

## [5.3.1] - 2026-08-20

### Fixed

- **Current task focus switching** — selecting a different current task now reliably clears the prior selection before assigning the new one, preventing unique-index conflicts and preserving one active focus task per user.
- **Focus-switching regression coverage** — added a service test that verifies the new current task is selected and the previous one is cleared.

---

## [5.3.0] - 2026-08-20

### Added

- **Linked project access for committed ideas** — committed ideas in the Ideas page now show a direct `View project` link to the project created from the idea, making the transition from incubation to execution easier to follow.

---

## [5.2.0] - 2026-08-20

### Added

- **Commit idea to project workflow** — ideas can now be converted into projects through a guided decision checkpoint covering the target user and problem, suitability, evidence, a validation experiment, and the commitment being replaced.
- **Decision record preservation** — committed projects retain the idea's title, description, area, and five commitment decisions, while the original idea is marked as converted and its working research notes are cleared from active idea context.

### Improved

- **Idea conversion reliability** — added ownership-aware conversion handling, concurrency conflict reporting, and focused service and dialog coverage for the new workflow.

---

## [5.1.0] - 2026-08-19

### Added

- **Midnight Blush theme** — users can now choose a second dark theme with a deep navy background and rose, purple, and teal accents from the theme menu.
- **Midnight Blush palette coverage** — added theme resolution and primary-accent tests for the new palette.

---

## [5.0.1] - 2026-08-19

### Added

- **Dracula theme** — users can now choose a dark Dracula-inspired theme from the theme menu, with a coordinated MudBlazor dark palette and Brainy design styling.

### Improved

- **Theme management** — theme selection now supports Brainy, Minimal, and Dracula themes, persists across sessions, restores before the first paint, and stays synchronized between the page styling and MudBlazor components.
- **Theme coverage** — added focused tests for theme palettes, selection behavior, and change notifications.

---

## [5.0.0] - 2026-08-19

### Added

- **Progressive Web App support** — Brainy now includes an installable web app manifest with app metadata, theme settings, responsive icons, and Apple touch icon support for a more native mobile launch experience.
- **Service worker registration** — the app registers a lightweight pass-through service worker that satisfies browser installability requirements while continuing to serve requests from the network.

---

## [4.0.16] - 2026-08-19

### Improved

- **Today snapshot navigation** — In Progress, Overdue, Due Today, Inbox, and Active Projects metrics now link directly to their relevant planning views, with descriptive labels for assistive technologies and visible keyboard focus states.
- **Clearer task prerequisites** — the task editor now displays selected prerequisite task titles instead of opaque identifiers.

### Changed

- **Today notifications consolidated** — removed the duplicate notification display from the Today page so the daily dashboard keeps its focus on active work.

---

## [4.0.15] - 2026-08-17

### Changed

- **Project summary cards simplified** — removed the unused desired-outcome display so project cards focus on progress, status, and actionable project metrics.

---

## [4.0.14] - 2026-08-17

### Changed

- **Project status vocabulary clarified** — the former `Waiting` status is now `Blocked`, and a new `Parked` status distinguishes work intentionally paused from work awaiting an unblocker.
- **Existing status data migrated** — projects stored with `Waiting` are converted to `Blocked` by an EF Core migration, with the updated statuses reflected throughout project editors, cards, detail pages, Areas, Goals, Today, and Tasks Hub views.

---

## [4.0.13] - 2026-08-16

### Added

- **LLM focus export** — a new LLM page and navigation entry let users download a versioned, user-scoped JSON snapshot for external focus-planning workflows. The export includes active projects, open tasks, dependencies, goals, inbox metrics, the user's calendar date and time zone, and a versioned planning prompt.
- **Privacy-aware LLM handoff** — the exported snapshot records that it is not sent automatically and reminds users to review project, task, and goal text before sharing it with an external LLM.

### Improved

- **Focus export prioritization and coverage** — projects and tasks are ordered by priority and due date, and inbox counting follows the note's actual Inbox status. Regression and production-surface tests cover the new LLM workflow and its navigation entry.

---

## [4.0.12] - 2026-08-15

### Added

- **Archive reasons across the PARA model** — archived Projects, Areas, Resources, Notes, Tasks, Ideas, Goals, and Outputs now retain optional context explaining why they were archived, with a database migration and archive dialogs that capture the reason.
- **Data import support** — account management now supports importing Brainy JSON exports with entity counts and a detailed import result for reviewing what was added or skipped.
- **Today dashboard preferences** — users can choose which Today widgets are visible, including the daily snapshot, current focus, task sections, goals, and priority projects.
- **Current focus picker** — the Today quick actions now open a focus-selection dialog for choosing the task currently being worked on.

### Improved

- **Search and content workflows refined** — search results now preserve and display snippet source context, while task, note, image, calendar, and output workflows receive broader dialog, validation, and interaction improvements.
- **Account and responsive UI updates** — management, archive, project, task board, task list, calendar, and Today surfaces were updated to support the new dialogs, archive context, and streamlined interactions.

---

## [4.0.11] - 2026-08-15

### Added

- **Inbox PARA suggestions** — inbox cards now show the suggested PARA category for each note, with the suggestion reasoning available on hover. Suggestions refresh after capture processing, edits, categorization, and archiving.
- **Home notifications restored** — the Today page now displays available notification alerts again.

### Improved

- **Global search accessibility** — the search field now exposes combobox semantics and tracks the active result for keyboard and assistive-technology users.
- **Theme menu accessibility** — the theme switcher now has an accessible label.

---

## [4.0.10] - 2026-08-15

### Added

- **Project table view** — the Projects page now includes a card/table view toggle. The table layout exposes project status, priority, due date, progress, open-task count, and quick actions for opening, editing, completing, or archiving a project.

---

## [4.0.9] - 2026-08-14

### Fixed

- **Area Detail now renders reliably with ad blockers enabled** — Area Detail CSS class names no longer use ad-blocker-sensitive `ad-*` prefixes, preventing cosmetic filter lists from hiding page content or styling.
- **Ad-blocker-safe naming regression tests** — markup tests now protect the Area Detail page from reintroducing CSS class names commonly targeted by content blockers.

---

## [4.0.8] - 2026-08-14

### Fixed

- **Area summaries now match active content** — Area project, open-task, and recent-note counts now exclude archived records, archived-by-status projects, subtasks, and records owned by another user.
- **Archived notes removed from active Area views** — archived notes no longer appear in an Area's linked-note list or as candidates to link to an active Area.

---

## [4.0.7] - 2026-08-14

### Fixed

- **Inactive project tasks excluded from active task views** — Today, Tasks Hub, and current-task recommendations now require the parent project to be active, so tasks belonging to waiting projects no longer appear in active execution workflows.
- **Regression coverage for waiting projects** — focused Today and Tasks Hub service tests now verify that tasks from waiting projects are excluded.

---

## [4.0.6] - 2026-08-14

### Added

- **`RefreshVersion` parameter on `TaskListComponent`** — the component now reloads its task list whenever `RefreshVersion` changes, enabling parent pages to trigger a targeted refresh without a full page reload.

### Changed

- **Navigate to new project after creation** — `ProjectsPage` now navigates directly to the newly created project's detail page instead of refreshing the list in place, reducing friction in the project-creation flow.
- **`ProjectEditorDialog` returns the project DTO** — the dialog now closes with the created or updated `ProjectDto` so callers can act on the result (e.g., redirect to the new project).
- **Task list refreshes after add/board change** — `ProjectDetailPage` increments `_taskListRefreshVersion` when a task is added via the dialog or changed on the board, ensuring the task list stays in sync without triggering a full detail reload.

---

## [4.0.5] - 2026-08-13

### Changed

- **Tasks Hub header redesigned** — the top-dashboard panel is now a styled card with a clay accent left bar, radial gradient background, rounded corners, and a soft drop shadow for a stronger visual hierarchy.
- **At-a-glance stat pills in the header** — a new `.td-header-summary` row shows compact metric pills (total, overdue, due today, in-progress counts) inline with the hero text so key numbers are visible without scrolling.
- **Header layout switches to horizontal on wider viewports** — `.td-header-row` now uses `align-items: flex-end; justify-content: space-between` so the hero text and stat pills sit side-by-side, reducing vertical space consumption.
- **Hero description line added** — a short subtitle below the page greeting improves orientation for new users and clarifies page intent.
- **TaskHealthWidget layout tightened** — internal gaps and font sizes adjusted to remain readable within the narrower card bounds.

---

## [4.0.4] - 2026-08-13

### Improved

- Add a complete Dashboard to the Pulse page, including a full PARA summary, task and project metrics, and a new "Pulse" section with a timeline of recent activity across all content types.

---

## [4.0.1] - 2026-08-13

### Fixed

- Corrected the release metadata to keep the patch version aligned with the published package and assembly versioning.
- Updated the changelog entry to reflect the latest patch release after the 4.0.0 major release.

---

## [4.0.0] - 2026-08-13

### Added

- Production health/readiness probes, hardened release/CI workflows, SQL Server model integration coverage, CodeQL, Dependabot, and an operator runbook.
- Versioned, tenant-scoped JSON data export with image checksums and browser download from Account management.
- Immutable lifecycle activity ledger for accurate Pulse history, including migration backfill for provable legacy timestamps.
- Task recurrence execution, archive/restore provenance, prerequisite management and enforcement, highlight source offsets, and action-item distillation into project tasks.
- Search coverage for every first-class content type and AI-output provenance/source preservation.

### Security

- Enforced HTTPS/HSTS assumptions, secure cookies, forwarded headers, security response headers, identity lockout, authentication rate limits, closed-by-default production registration, and stronger passwords.
- Added consistent tenant ownership validation, safe Markdown rendering, note-image signature validation, per-user image quotas, and abandoned-upload cleanup.
- Added optimistic concurrency handling for Projects, Tasks, Resources, Goals, Ideas, and Outputs.

### Changed

- Quick Capture now rejects unsupported binary attachments instead of silently discarding their contents.
- Today, Pulse, and stale-work calculations use a persisted user time zone and injected clocks.
- Archive and Area lifecycle operations preserve child state and reject unsafe restores or archives with actionable messages.
- Primary pages expose recoverable loading failures, semantic headings, keyboard controls, accessible names, and responsive focus states.

---

## [3.4.0] - 2026-08-13

### Added

- **Open Tasks section on Area detail page** — a new `ad-section` lists all open tasks across the area's linked projects, grouped with status (To Do / In Progress / …) and priority badges. When no projects are linked, or all tasks are complete, a contextual empty state is shown.
- **Overdue task count stat tile** — the summary strip at the top of `AreaDetailPage` now includes an *Overdue* tile whose value turns red (`ad-stat-tile__value--red`) when overdue tasks exist.
- **Ideas count stat tile** — an *Ideas* tile (clay accent when non-zero) is added to the summary strip alongside the existing Open Tasks and Notes tiles.

### Changed

- **Ideas section promoted to top-level** — the Related Ideas block was previously nested inside the Projects section; it is now a standalone `ad-section` rendered after Open Tasks, making it easier to scan at a glance.
- `ITaskService` injected into `AreaDetailPage` to load per-area task data.

---

## [3.3.0] - 2026-08-13

### Changed

- **Project names are now clickable links in all Today widgets** — `<span class="td-task-project">` replaced with `<a href="/projects/{id}">` in `CurrentTaskWidget`, `DueTodayWidget`, `OverdueWidget`, `InProgressWidget`, `NextTasksWidget`, and `HighPriorityProjectWorkWidget`, letting users navigate directly to a project from any task row.
- **DueThisWeekWidget groups by project ID + name** — the `GroupBy` key changed from `ProjectName` (string) to `{ ProjectId, ProjectName }` so tasks from different projects with the same name are no longer merged; group headers are rendered as `<a>` links to the project page.
- **Project link hover state** — `.td-task-project:hover` adds a subtle background tint and underline for clear affordance without disrupting the ambient typography.

---

## [3.2.0] - 2026-08-12

### Added

- **Theme persistence** — the active theme (Brainy or Minimal) is now saved to `localStorage` and restored on next load via a small inline JS helper in `MainLayout`; users no longer lose their theme choice on page refresh.

### Changed

- **Font tokens adopted site-wide** — hard-coded `'Fraunces'` font-family references across `ArchivesPage`, `AreasPage`, `GoalListPage`, `IdeasPage`, `InboxPage`, `Notes`, `ParaDashboard`, `ResourcesPage`, `TasksHubPage`, and related components replaced with `var(--font-display)`, so both themes apply their correct display typeface automatically.
- **MinimalTheme refined** — `AppThemes.MinimalTheme` updated with a proper Inter-based font stack and a tightened neutral colour palette.
- **Hardcoded Google Fonts import removed from OutputDetailPage** — the page-level `<link>` for Playfair Display / Lato is removed; fonts are now loaded once from `App.razor` via the theme-aware stylesheet.
- **Minimal theme CSS consolidated** — the `:root[data-theme="minimal"]` block in `brainy-design.css` is reorganised to cover typography tokens, spacing, and palette overrides in a single coherent section.

---

## [3.1.0] - 2026-08-12

### Added

- **Runtime theme switching** — users can now switch between two themes from a palette icon in the app bar:
  - **Brainy Theme** — the existing warm editorial design (clay accent `#c0561d`, Fraunces serifs for headings, Hanken Grotesk for body text, `12px` border radius).
  - **Minimal Theme** — a clean, neutral design (black/white palette, Inter font throughout, `4px` border radius) inspired by distraction-free writing environments.
- **`ThemeService`** — new scoped service (`Brainy.Web.Themes.ThemeService`) that holds the active theme state and fires an `OnThemeChanged` event consumed by `MainLayout`.
- **`AppThemes`** — new static class exposing `BrainyTheme` and `MinimalTheme` as `MudTheme` instances; the inline theme definition previously embedded in `MainLayout` is now centralised here.
- **`data-theme="minimal"` CSS attribute** — a small inline JS helper (`window.brainyTheme.setTheme`) toggles the attribute on `<html>`, driving a `:root[data-theme="minimal"]` block in `brainy-design.css` that overrides all CSS custom properties for the Minimal palette.
- **Inter font** — the Google Fonts stylesheet in `App.razor` now includes the `Inter` family required by the Minimal theme.

### Changed

- `MainLayout` refactored to use `ThemeService` and `AppThemes` instead of the inline `_theme` field; implements `IDisposable` to unsubscribe from theme-change events.
- `app.css` body font-family and background/colour now use `var(--font-body)`, `var(--paper)`, and `var(--ink)` tokens so both themes apply correctly without hard-coded values.

---

## [3.0.0] - 2026-08-12

### Changed

- **Daily Focus Summary widget items wider** — `.td-snap-item` minimum width increased from `80px` to `140px` in `DailyFocusSummaryWidget` so the stat tiles no longer compress too tightly on mid-size screens, improving readability of the daily snapshot strip.

---

## [2.1.0] - 2026-08-12

### Added

- **Pulse metrics grouped by domain area** — the flat metric tile grid on the Pulse page is replaced with labelled groups (Notes, Tasks, Projects, Outputs, Ideas, Goals). Each group shows a header with an icon, title, and a pill badge with the group total, followed by its own metric tile grid. New `MetricGroup` and `MetricTile` record types drive the data model.

### Changed

- **Task widget action button order standardised** — in `OverdueWidget`, `DueTodayWidget`, `DueThisWeekWidget`, and `HighPriorityProjectWorkWidget` the "Set In Progress" button now appears after "Edit task" and before "Mark complete", giving a consistent Edit → Set In Progress → Complete order across all Today widgets.

---

## [2.0.0] - 2026-08-12

### Added

- **New Task shortcut in the header nav** — a primary-styled "New Task" button is now available in the top dashboard and opens the add-task dialog directly, making task creation faster and more visible from the home screen.

### Changed

- **Home dashboard flow redesigned for faster action** — the top dashboard was restructured to present the main daily view above the task list, with a cleaner navigation row and updated responsive layout.
- **Priority projects moved below the task list** — the projects panel now appears after the execution sections, using a broader responsive card grid for easier scanning.
- **Quick Actions toolbar removed** — task creation is now handled from the New Task button in the header rather than the previous standalone action area.

---

## [1.10.0] - 2026-08-12

### Added

- **New Task shortcut in the header nav** — a primary-styled "New Task" button (clay accent colour, `.td-nav-chip-primary`) is now the first chip in the navigation row and opens the add-task dialog directly, replacing the separate Quick Actions toolbar.

### Changed

- **Priority projects moved below the task list** — the projects panel is removed from the top-dashboard area and rendered at the bottom of the main execution column after all task sections. All priority projects are now shown (no two-card cap), laid out in a responsive `auto-fill minmax(320px, 1fr)` grid with full `ProjectSummaryCard` (non-compact).
- **Inbox reminder widget removed from dashboard** — `InboxReminderWidget` and its `.td-priority-projects` wrapper are no longer shown in the top section.
- **Wide-screen widgets grid narrowed** — at ≥ 1 200 px `.td-sidebar-widgets` now uses `repeat(2, 1fr)` instead of `repeat(4, 1fr)`, and the first child no longer spans extra columns, giving each widget equal width.
- **Quick Actions toolbar removed** — `TodayQuickActions` and its `.td-quick-actions-wrap` wrapper are removed; task creation is now initiated from the New Task nav chip.

---

## [1.9.0] - 2026-08-12

### Changed

- **Home page layout refactored to full-width top-dashboard** — the sticky sidebar (`<aside>`) is replaced with a `<section class="td-top-dashboard">` that spans the full width above the main content area. The outer `.td-layout` now uses `flex-direction: column` instead of a two-column CSS grid, removing the 1 024 px / 1 440 px grid breakpoints.
- **Responsive header row** — hero text and navigation chips are placed in a new `.td-header-row` container that stacks vertically on mobile and switches to a row with space-between alignment at ≥ 768 px.
- **Widgets area uses responsive CSS grid** — `.td-sidebar-widgets` changed from `flex-direction: column` to a 1 → 2 → 4 column CSS grid (breakpoints at 768 px and 1 200 px); the first widget spans two columns on the widest layout.
- **Priority projects capped at two cards** — the priority-projects list on the Today dashboard now renders at most two `ProjectSummaryCard` items to avoid crowding the top area.
- **Decorative separators removed** — `border-bottom` on `.td-nav` and `border-top` on `.td-priority-projects` are removed; spacing is handled by gap values alone.

---

## [1.8.0] - 2026-08-12

### Fixed

- **Setting a task as current now forces In Progress status** — calling `SetCurrentTaskAsync` on a task that is not already *In Progress* now automatically transitions its status to `InProgress` and clears any `CompletedDate`, preventing a completed task from appearing as the active focus item on the Today screen.

---

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
