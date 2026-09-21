# Cross-App Consistency

Updated: 2026-09-19

## Purpose

Depot uses one cross-app presentation contract for statuses, workspace tabs, shell context, productive tables, notifications, My Work, search and first-run guidance. This is a presentation/interaction contract; it does not introduce new business states or business workflows.

## Unified status language

`DocumentStatusBadge` preserves the domain-specific status text and resolves only the shared visual semantic:

| Status family | Shared semantic |
| --- | --- |
| Draft | neutral |
| Waiting / Pending | warning |
| Ready / Active / Open / In progress | primary |
| Due today | warning |
| Completed / Approved / Posted / Closed / Paid | success |
| Overdue / Blocked / Error / Failed | error |
| Rejected / Cancelled | error |
| Reversed | error |
| Archived / Disabled / Inactive | muted |

Every badge combines glyph plus text with the semantic variant. Domain states are not renamed or collapsed into a new persisted status model.

A real change of document status may receive one brief visual confirmation through the shared status-change motion. Initial rendering remains static, reduced-motion preferences are respected, and no repeating pulse/blink behavior is used.

`OperationStatus` may surface one command-bound recovery or follow-up action. The action appears only when both action text and an existing command are supplied; the command remains responsible for `CanExecute`. Existing diagnostic/help actions stay separate from recovery so troubleshooting evidence remains available.

## Workspace tabs

Tabs reuse the existing icon and unsaved-change marker. Hover and selection use the shared reduced-motion-aware Fast/Standard grammar, with selection expressed through opacity plus a centered scale transform on the tab accent rather than layout animation. A newly materialized workspace tab may briefly enter through the shared `State` transition; closing remains immediate so disposal and keyboard behavior are never delayed. Keyboard focus remains visible through a static border independent of animation.

**Reopen Closed Tab** and **Ctrl+Shift+T** restore the last closed normal workspace tab for the current application session. Document tabs remain disposable and are intentionally not retained for reopen. There is no new persisted session-restore model and no pin persistence in this pass.

## Shell navigation motion

Activity Bar selection retains the established accent and adds restrained icon/halo feedback using opacity and minimal render scale. Utility buttons for navigation, notifications, help, current user and shell chrome use the common button-motion foundation. Reduced Motion collapses these non-essential durations, and no shell animation changes navigation routes, focus ownership, tab persistence or workspace content transition semantics.

## Shell context

The status bar shows:

- connection state;
- active workspace;
- provider plus safe database name;
- Depot version.

The shell does not show connection strings, credentials, server hosts, user names, passwords or full local database paths.

Company/legal entity and current accounting period are intentionally not inferred from administration screens. They should be added only when a shared, authoritative and permission-safe shell context exists.

## Productive tables

The existing table infrastructure remains authoritative:

- `AppDataGridStyle` for normal productive collections;
- `AppDataGridCompactStyle` for deliberately dense evidence/operations;
- numeric/currency/date formatting through the existing shared styles and view models;
- existing `WorkspaceViewBar`, saved views, filters, pagination and detail-pane patterns where already supported.

This pass does not add a second generic table abstraction or new persisted preference model.

## Notifications and My Work

The UX contract is:

> Notification = something happened.  
> My Work = something currently requires work.

Notifications remain event-oriented and use the existing notification navigation handler to deep-link to the relevant permitted workspace/record. My Work remains the actionable projection produced by the existing services. No second task persistence is introduced.

## Search and command palette

Command Palette, Quick Open and Global Search share one group order:

1. Suggested
2. Commands
3. Workspaces
4. Records
5. Recent

A workflow command is **Suggested** only when its registered context route matches the active workspace/page. Permission filtering and the existing service/action boundary remain authoritative.

## Welcome / empty shell

When no workspace tab is open, Welcome leads with continuing work: default workspace, My Work and Search / Quick Open. It remains a tabless shell fallback and does not invent a parallel home/dashboard workflow.

## Persistence and versioning

No persisted schema changes are required. Help topic IDs, permission mappings and Help routing remain unchanged.
