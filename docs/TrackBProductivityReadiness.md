# Track B – ERP Productivity & Usability Readiness

Updated: 2026-09-16

## Purpose

Track B completes the first integrated ERP productivity layer on top of the standardized Depot desktop workspace model. This closeout does not add another business feature. It verifies that the four delivered packages work as one product contract and that user-facing guidance matches the implemented behavior.

## Delivered packages

| Package | Capability | Status |
| --- | --- | --- |
| U1 | Saved Views & User Preferences | Implemented and merged |
| U3 | Global Search / Command Palette | Implemented and merged |
| U2 | Favorites, Recent Workspaces & Home Personalization | Implemented and merged |
| U4 | Workflow Productivity & Keyboard Shortcuts | Implemented and merged |

## Integrated productivity contract

### Saved Views

Initial persisted Saved View coverage is deliberately bounded to the semantic workspaces:

```text
inventory.overview
inventory.traceability
```

Definitions are user/workspace scoped and persist supported filters, column presentation, sorting and grid density. Unknown or unsupported state fails safe instead of blocking workspace startup.

### Global Search

`Ctrl+K` is the canonical global search surface. Search remains permission-aware, bounded and cancellable and is routed through search providers and existing service boundaries rather than UI repository access.

`Ctrl+P` Quick Open and `Ctrl+Shift+P` Command Palette remain established shell surfaces.

### Personal workspace productivity

The Dashboard `My workspace` region provides bounded Favorites and Recents, permission-aware Quick Actions and a personal default landing route. Stored semantic route IDs are preferences only; they are re-materialized against the current permission-filtered shell catalog. Permission loss therefore cannot turn a stored preference into an authorization bypass or metadata disclosure.

### Keyboard productivity

Workflow shortcuts resolve in memory and execute existing `ICommand` instances. `CanExecute`, confirmations, RBAC, validation, transactions, audit and concurrency behavior remain in the existing ViewModel/service path.

The standard workflow gestures are:

```text
Ctrl+S
Ctrl+Shift+S
F5
Ctrl+F
Alt+Insert
Alt+Delete
Ctrl+PgUp
Ctrl+PgDn
Ctrl+Enter
Ctrl+Shift+Enter
```

Established shell gestures remain reserved, including `Ctrl+K`, `Ctrl+P`, `Ctrl+Shift+P`, `Ctrl+W`, tab navigation, navigation history and `F1` Help.

## Closeout findings

The implementation packages already contained focused persistence, search, RBAC, shell and shortcut tests. The main cross-package readiness gap was user-facing Help drift:

- Workspace Navigation did not describe canonical `Ctrl+K` Global Search;
- the U4 workflow-shortcut contract was not visible in embedded Help;
- Dashboard Help did not describe Favorites, Recents, Quick Actions or default landing behavior;
- the two initial Saved View workspaces did not explain their persisted presentation-state behavior in embedded Help.

This closeout updates those existing Help topics without introducing a new Help topic ID or changing permission mappings. The Help manifest therefore remains `1.21`; its topic contract is unchanged while article content is brought in sync with the product.

## Regression contracts added

`TrackBProductivityReadinessTests` creates cross-feature checks that intentionally span package boundaries:

- Workspace Help must document `Ctrl+K` Global Search;
- every gesture currently published by `WorkflowShortcutCatalog` must appear in Workspace Help;
- workflow Help must state that `CanExecute` remains authoritative;
- Favorites, Recents and default landing behavior must be documented as permission-aware preferences;
- both initial Saved View workspaces must document persistence, Reset and authorization separation;
- the non-shell workflow shortcut layer must not claim the canonical `Ctrl+K` gesture.

These tests make documentation drift a regression rather than a manual discovery.

## Performance and hot paths

The closeout introduces no new runtime query path and no new persisted state.

Existing bounded behavior remains authoritative:

- Saved View definitions are size/count bounded;
- Global Search uses bounded result sets and cancellation;
- Favorites and Recents are bounded and deduplicated;
- shortcut matching is static in-memory resolution.

## Security and RBAC

No Track B preference, search result or shortcut is an authorization boundary.

The integrated rule remains:

```text
visible / stored / searchable / shortcut-enabled
is not equivalent to
authorized
```

Authorization continues through the existing service boundaries and command state. Stored routes and presentation state are revalidated against current application state before being exposed or used.

## Schema and version impact

This closeout changes documentation and integration tests only.

```text
Depot: 0.15.183-preview
Core database schema: 30
Sales feature schema: 11
Finance feature schema: 9
User Sessions feature schema: 3
Security Events feature schema: 2
UserPreferences feature schema: 2
Help manifest: 1.21
```

No persisted schema changes are required.

## Release readiness

The branch must pass the existing repository validation surface before Track B is considered closed:

- CI / Release build with warnings as errors;
- Shell-UX and persistence regressions;
- software-quality gates;
- security supply-chain gates;
- database-provider acceptance;
- release pipeline;
- packaged DepotManager E2E gates.

A queued or unexecuted workflow is not evidence of success.

## Track status

After this closeout is merged and the required gates are green, Track B is complete. There is no additional functional work package defined inside Track B. Subsequent work should begin from the next repository/roadmap track rather than extending Track B with unrelated functionality.
