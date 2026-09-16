# Workspace Productivity & Home Personalization

Updated: 2026-09-16

## Purpose

Depot persists a small, user-scoped productivity profile for ERP navigation so users can return to their preferred workspaces without rebuilding navigation context after each restart.

The feature covers:

- Favorite workspaces
- Pin / unpin
- Recently used workspaces
- Personal Dashboard region
- Default landing workspace
- Quick workspace actions

## Architecture

```text
Dashboard / Shell
  -> WorkspaceProductivityViewModel
      -> WorkspaceProductivityService
          -> WorkspaceProductivityRepository
              -> DatabaseAccess
```

No view queries SQL or repositories directly. Persisted values are stable semantic `ShellRoute` identifiers only.

## Persistence

UserPreferences schema version 2 adds:

```text
UserWorkspaceFavorites
UserWorkspaceRecents
UserWorkspacePreferences
```

Favorites and recent routes are scoped by `UserId`. The default landing route is stored once per user. Existing saved-view tables remain unchanged.

Limits:

- favorites: 12
- recent workspaces: 12

Revisiting a workspace updates its existing recent entry instead of creating duplicates. Old recent entries are trimmed transactionally.

## Security and RBAC

Persisted routes are not treated as authorization.

At runtime Depot rebuilds the available workspace catalog from the current user's permission-filtered shell navigation. Stored routes that are no longer available:

- are not rendered as favorites or recent items;
- are not offered as quick actions;
- cannot be opened through productivity navigation;
- do not expose stored titles, descriptions or other business metadata.

The route identifier may remain persisted so that a favorite can become visible again if the user legitimately regains access later.

## Default landing behavior

When the shell starts:

1. the stored default route is used only if it exists in the current permitted route catalog;
2. otherwise Depot falls back to the permitted Dashboard route;
3. if Dashboard is unavailable, Depot uses the first available permitted workspace;
4. if no route is available, the existing welcome state remains usable.

Preference failures are non-fatal and never block the ERP shell.

## Dashboard region

The Dashboard contains a personal workspace region with:

- Favorites
- Recent workspaces
- Quick actions
- Current landing preference
- Reset landing action

Quick actions are bounded and deterministic. Favorites are preferred first, followed by commonly used permitted routes and recent workspaces.

## Recent navigation hot path

Workspace visits are recorded only when the semantic route changes. Repeated property notifications for the same route do not produce duplicate writes. Recording recents is best-effort and cancellable so preference persistence cannot stall or break navigation.

## Schema and versioning

```text
Depot: 0.15.180-preview
Core schema: 30
UserPreferences schema: 2
```

No Core, Sales, Finance, User Sessions or Security Events schema change is required.

## Test coverage

`WorkspaceProductivityTests` covers:

- UserPreferences 1 -> 2 migration and idempotency
- preservation of existing saved-view tables
- user isolation
- restart/reload persistence
- bounded and deduplicated recents
- favorite limits and unpin behavior
- semantic route validation

Repository CI and provider-acceptance gates remain authoritative for cross-provider verification.
