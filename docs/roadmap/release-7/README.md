# Release 7 candidate backlog

Proposed epic and sub-issues for Brainy 7.0, derived from an audit of the repository at `6.0.0`.

**Nothing here is a commitment.** It is a triage list — create the issues, then decide.

## How to create these on GitHub

```bash
gh auth status                 # needs repo write access
./create-issues.sh --dry-run   # preview
./create-issues.sh             # create epic + 18 sub-issues, linked
```

The script creates `00-epic.md` first, then every other file as a sub-issue attached to it via the GitHub sub-issue API. Labels come from the `<!-- labels: ... -->` marker on line 2 of each file and use only labels that already exist in the repository.

Once the issues exist, this folder can be deleted — GitHub is the source of truth.

## Contents

| File | Proposed issue | Tier | Priority |
| --- | --- | --- | --- |
| `00-epic.md` | Epic: Brainy 7.0 | — | P0 |
| `01-transactional-email.md` | Add transactional email delivery | 1 | P0 |
| `02-real-billing-provider.md` | Implement a real billing provider so Pro is purchasable | 1 | P0 |
| `03-byok-ai-key.md` | Add per-user bring-your-own AI provider key | 1 | P0 |
| `04-ai-metering-decision.md` | Decide and enforce hosted AI metering | 1 | P0 |
| `05-wire-existing-ai.md` | Wire the existing IAiAssistant surface into Inbox and capture | 2 | P1 |
| `06-importers.md` | Add importers for Obsidian, Notion, Markdown and Evernote | 2 | P1 |
| `07-note-version-history.md` | Add note version history | 2 | P1 |
| `08-calendar-feed.md` | Publish an ICS calendar feed for deadlines | 2 | P1 |
| `09-web-push.md` | Add web push notifications | 2 | P1 |
| `10-templates.md` | Add templates for projects, notes and outputs | 2 | P1 |
| `11-tag-management.md` | Add a tag management page | 3 | P2 |
| `12-command-palette.md` | Extend global search into a command palette | 3 | P2 |
| `13-output-share-links.md` | Add read-only share links for Outputs | 3 | P2 |
| `14-markdown-export.md` | Export to a Markdown / Obsidian-compatible vault | 3 | P2 |
| `15-e2e-tests.md` | Add a Playwright end-to-end test suite | 3 | P2 |
| `16-opentelemetry.md` | Add OpenTelemetry traces, metrics and logs | 3 | P2 |
| `17-localization.md` | Add localization infrastructure | 3 | P2 |
| `18-activation-metrics.md` | Compute activation funnel metrics on the analytics dashboard | 3 | P1 |

## Not duplicated here

Two audit findings already have open issues and stay parented under #292:

- **#300** — hybrid search with source-backed semantic retrieval. The Tier 1 headline for release 7.
- **#303** — searchable captured documents with source-preserving extraction and OCR. Depends on #300.

Attach them to the new epic manually if you want them tracked there too.
