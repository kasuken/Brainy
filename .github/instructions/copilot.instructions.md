---
description: "Use when working on Brainy, a .NET Blazor 10 second-brain app using Tiago Forte/PARA workflows, MatBlazor UI components, Entity Framework, and SQL Server."
name: "Brainy Second Brain Guidelines"
applyTo: "**"
---
# Brainy - Copilot Instructions

## Product Context

Brainy is a SaaS for managing a digital second brain.

The product helps users capture, organize, retrieve, and transform knowledge using proven second brain practices. The main methodology is inspired by Tiago Forte’s approach:

- PARA: Projects, Areas, Resources, Archives
- CODE: Capture, Organize, Distill, Express
- Progressive Summarization
- Actionability over storage
- Knowledge reuse over passive note taking

Brainy is not just a note-taking app. It helps users turn information into useful outputs.

## Product Principles

Always optimize Brainy for:

1. Fast capture
2. Clear organization
3. Easy retrieval
4. Knowledge distillation
5. Reuse in real work
6. Low cognitive load
7. Trustworthy AI assistance

Avoid building features that only create more storage without improving actionability.

## Core Mental Model

Every item in Brainy should answer at least one of these questions:

- Is this useful for a current project?
- Is this related to an ongoing responsibility?
- Is this reference material for the future?
- Is this no longer active but worth keeping?

Use PARA as the default structure:

| Category | Meaning |
|---|---|
| Project | Short-term outcome with a deadline |
| Area | Ongoing responsibility without a fixed end |
| Resource | Topic of interest or reference material |
| Archive | Inactive material kept for later |

## Feature Design Rules

When designing features, prefer workflows that follow CODE:

### Capture

Make it easy to save information from:

- Text notes
- URLs
- PDFs
- Emails
- Meeting notes
- Documents
- Voice notes
- Images

Capture must be frictionless.

Do not force users to classify everything upfront.

### Organize

Organization should happen close to action.

Prefer smart suggestions over mandatory folders.

The system should suggest:

- Project
- Area
- Resource
- Archive
- Tags
- Related notes
- Possible next actions

Never overcomplicate organization with deep nested folders.

### Distill

Brainy should help users extract the essence of information.

Support:

- Highlights
- Summaries
- Key takeaways
- Action items
- Decisions
- Open questions
- Progressive summarization

A good distilled note should be useful even months later.

### Express

Brainy should help users create outputs from stored knowledge.

Support outputs such as:

- Blog posts
- LinkedIn posts
- Reports
- Product specs
- Meeting briefs
- Roadmaps
- Decision records
- Learning plans
- Research summaries

Knowledge is valuable only when reused.

## AI Behavior

AI features must act as a second brain assistant, not as a generic chatbot.

The AI should:

- Ask clarifying questions only when required
- Suggest PARA placement
- Extract action items
- Detect duplicate or related notes
- Summarize without losing nuance
- Preserve original source references
- Explain why it made a suggestion
- Help users turn knowledge into output

The AI must not invent sources, facts, or references.

If information is missing, say so clearly.

## UX Rules

Keep the interface simple.

Main navigation should reflect the user’s mental model:

- Today
- Inbox
- Projects
- Areas
- Resources
- Archives
- Search
- Outputs

The Today screen is the main screen of the application.

Today must help the user focus on active execution, not knowledge storage. It should show:

- Current task: the task the user is actively working on now
- High-priority project work: important tasks from priority projects
- Due today: tasks that expire today
- Overdue: tasks that have already expired
- Due this week: tasks that expire during the current week
- Next tasks: tasks that expire in the coming days or weeks

Do not show archived tasks on Today. Archived tasks belong only in Archives or inside archived project context.

Do not create a standalone Tasks screen by default.

Show tasks in the context of their project. Archived tasks must not appear in active project, area, inbox, search-result, or task-oriented views unless the user is explicitly viewing Archives.

When a project is archived, treat its tasks as archived context too. They should be visible from the archived project or Archives, not from active work screens.

The Inbox is for unprocessed captured items.

The product should encourage users to process items, not hoard them.

## Data Model Guidance

Core entities should include:

- Note
- Source
- Project
- Area
- Resource
- Archive
- Tag
- Highlight
- Summary
- ActionItem
- Output
- Relationship
- Task
- Subtask

Every note should support:

- Title
- Content
- Source
- PARA category
- Tags
- Created date
- Updated date
- Status
- Linked notes
- AI summary
- User highlights

Tasks should support:

- Parent project
- Optional parent task for subtasks
- Title
- Description
- Status
- Priority
- Due date
- Archived state
- Created date
- Updated date

Projects should support an archived state. Archived projects and archived tasks must be excluded from active work queries by default.

## Quality Bar

A feature is good only if it helps the user:

- Save time
- Reduce mental clutter
- Make better decisions
- Produce something useful
- Find knowledge faster
- Connect ideas

Reject features that mainly add complexity.

## Naming Rules

Use clear product language.

Prefer:

- Inbox
- Projects
- Areas
- Resources
- Archives
- Highlights
- Summaries
- Outputs
- Actions

Avoid vague terms like:

- Vault
- Knowledge graph
- Neural space
- AI brain
- Memory layer

Unless the feature clearly needs them.

## Development Guidance

When generating code:

- Keep domain logic separate from UI
- Use clear service boundaries
- Make PARA classification explicit
- Keep AI prompts versioned
- Store original user content separately from AI-generated content
- Track provenance for summaries and extracted insights
- Design for auditability
- Design all the components, pages and layout by using the skill `frontend-design`
- For all the subpages like create, edit use Modals of MudBlazor
- When possible, groups all the components and pages related to the same feature in the same folder, for example, all the components and pages related to Notes should be in a folder called Notes

AI-generated data should always be marked as AI-generated.

## Default Product Positioning

Brainy helps professionals build a practical second brain by turning scattered information into organized, reusable knowledge.

It is for people who want to think better, work faster, and create more from what they already know.