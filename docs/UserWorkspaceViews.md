# User Workspace Views

Updated: 2026-09-16

## Purpose

Depot persists user-specific ERP workspace presentation state so recurring work does not require rebuilding filters and grid layouts after every sign-in or restart.

The feature is presentation-state persistence only. It does not create an alternate business-data access path and it does not weaken workspace authorization.

## Architecture

```text
Workspace View
  -> WorkspaceViewsViewModel
  -> WorkspaceViewService
  -> WorkspaceViewRepository
  -> DatabaseAccess / transaction runner
  -> SQLite / SQL Server / MariaDB / MySQL
```

Application composition configures the workspace-view service once. Workspace controls resolve that configured service and create their dedicated view model. Business workspaces continue to load data through their existing view-model/service/repository paths.

## Stable identifiers

Persisted definitions use semantic identifiers rather than WPF control names or volatile object references:

```text
WorkspaceId
ColumnId
FilterId
```

Identifiers allow letters, digits, `.`, `-`, `_` and `:`. Removed or renamed columns and filters are ignored when a saved definition is applied. Unknown state therefore cannot prevent a workspace from opening.

Initial workspace coverage:

- `inventory.overview`
- `inventory.traceability`

The mechanism is reusable by further workspaces by assigning semantic identifiers to their grid, columns and supported filters.

## Persisted state

A saved view contains:

- filter state;
- column order;
- column widths;
- column visibility;
- sort columns and direction;
- grid density (`Compact`, `Standard`, `Comfortable`).

Each user can save multiple views per workspace. A single default view can be selected per user/workspace. `Reset` restores the canonical layout captured from the workspace definition; it does not alter another user's state.

## Persistence model

Feature schema:

```text
UserPreferences = 2
```

Saved-view tables remain:

```text
UserWorkspaceViews
UserWorkspaceDefaultViews
```

Schema version 2 additionally contains user productivity tables for favorites, recent workspaces and default landing behavior. Those structures are documented in `WorkspaceProductivity.md` and do not change the saved-view definition format.

`UserWorkspaceViews` is unique by `(UserId, WorkspaceId, ViewId)` and by `(UserId, WorkspaceId, Name)`. `UserWorkspaceDefaultViews` has a single row per `(UserId, WorkspaceId)`, which makes the default invariant explicit under concurrent use.

Updates and deletes use the persisted `Version` for optimistic concurrency. Default changes are transactional. The feature schema is provisioned through the same provider-neutral database provisioning path as the existing feature schemas.

## Security and failure behavior

The service never accepts an arbitrary user id from a workspace caller. It derives the owner from the current authenticated `AuthorizationService.CurrentUser` and refuses persistence when no active user is signed in.

Saved views contain presentation state only. Applying a view cannot navigate around RBAC or query business repositories directly. Existing workspace services remain responsible for authorization and data filtering.

Definitions use format version `1`, are size- and count-bounded, and are validated before persistence. Invalid JSON, unsupported future formats, or otherwise invalid stored definitions are ignored on load so a stale preference cannot make the workspace unavailable.

## Limits

- maximum 20 saved views per user/workspace;
- maximum serialized definition size: 64 KiB;
- bounded column, sort and filter entry counts;
- semantic IDs capped at 160 characters;
- saved-view names capped at 120 characters.

These limits protect startup and navigation paths from unbounded preference payloads.

## Testing

`WorkspaceViewPersistenceTests` covers:

- feature-schema creation and idempotent migration;
- per-user and per-workspace isolation;
- default-view persistence;
- optimistic-concurrency rejection;
- deletion;
- fail-safe handling of unsupported definitions.

Provider certification remains covered by the normal provisioning/provider gates because `DatabaseProvisioningService` provisions `UserPreferences` for every supported provider.
