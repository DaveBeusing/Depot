# Workflow Productivity & Keyboard Shortcuts

Updated: 2026-09-16

## Purpose

Depot provides a consistent keyboard layer for recurring ERP actions without creating a second business-logic path.

The invariant is:

```text
Shortcut
  -> ICommand
  -> existing ViewModel / Service path
```

The shortcut layer never writes business data, bypasses RBAC, or calls repositories directly. Existing command `CanExecute` state, confirmations, transaction boundaries, validation, concurrency checks, audit behavior, cancellation and authorization remain authoritative.

## Standard shortcuts

| Shortcut | Action | Behavior |
| --- | --- | --- |
| `Ctrl+S` | Save | Executes the current registered save command when enabled. |
| `Ctrl+Shift+S` | Save & New | Executes the existing save command and only invokes the existing new-record command after a successful save. |
| `F5` | Refresh | Executes the current registered refresh command. |
| `Ctrl+F` | Search | Opens the existing global Search / Command surface. `Ctrl+K` remains the canonical global-search shortcut. |
| `Ctrl+W` | Close | Uses the established shell path for closing the active workspace tab. |
| `Alt+Insert` | Add Line | Executes the current draft-line add/update command. |
| `Alt+Delete` | Delete Draft Line | Executes the current draft-line remove command only when that command is enabled. |
| `Ctrl+PgUp` | Previous Record | Moves to the previous record in supported master-data editors. |
| `Ctrl+PgDn` | Next Record | Moves to the next record in supported master-data editors. |
| `Ctrl+Enter` | Primary Action | Executes the registered primary workflow command, for example submit or post. |
| `Ctrl+Shift+Enter` | Context Action | Executes the registered context command where applicable. |

Existing shell shortcuts remain unchanged:

- `Ctrl+K` global search;
- `Ctrl+P` Quick Open;
- `Ctrl+Shift+P` Command Palette;
- `Ctrl+Tab` / `Ctrl+Shift+Tab` workspace tabs;
- `Alt+Left` / `Alt+Right` navigation history;
- `F1` context Help.

The workflow catalog deliberately does not reuse those gesture combinations.

## Initial workflow coverage

High-frequency transactional workspaces register their existing commands for keyboard use, including:

- Purchase Orders;
- Stock Transfers;
- Material Issues;
- Material Returns;
- Supplier Returns;
- Finance Payables;
- reusable item reference data;
- reason codes.

Refresh is also registered for supported Finance and Administration workspaces that already expose a `RefreshCommand`.

This is capability-based: a shortcut is active only when the focused workspace has a registered command for that action. Unsupported workspaces keep their existing behavior rather than receiving an invented action.

## Save & New

Save & New is an orchestration of two existing commands. It does not reimplement save logic.

```text
Ctrl+Shift+S
  -> existing SaveCommand
  -> await completion when asynchronous
  -> stop when the ViewModel reports an operation error
  -> existing NewCommand
```

This prevents a failed save from silently clearing the editor.

## Destructive actions

`Alt+Delete` is intentionally used instead of `Delete` or `Ctrl+Delete` so normal text-editing behavior is not intercepted.

A destructive shortcut does not weaken the command's existing protection. If a draft line cannot be removed, its existing `CanExecute` result is false and the shortcut performs no action. Primary and context actions reuse existing commands, so confirmation dialogs such as posting or reversal confirmation remain in the original workflow path.

## Record navigation

`Ctrl+PgUp` and `Ctrl+PgDn` provide explicit previous/next record movement for supported master-data list editors. The shortcut updates the same selected-record property used by mouse and keyboard selection, so normal editor population and command-state updates continue to run.

## Shortcut hints

Buttons bound to directly registered workflow commands receive a non-invasive tooltip when they do not already define one:

```text
Keyboard shortcut: Ctrl+S
```

Existing tooltips are never overwritten. Shortcuts complement the visible buttons and do not replace them.

## Performance

Shortcut resolution is in-memory only. The registry is static and bounded, and a key press resolves against the focused DataContext before falling back to the active workspace ViewModel. No database query is performed merely to resolve a shortcut.

## Security and authorization

The shortcut layer has no permission model of its own. It relies on the same command and service boundaries used by visible UI actions.

Consequently:

- hidden or unauthorized business capabilities do not become available through a shortcut;
- disabled commands remain disabled;
- RBAC checks remain in the existing service path;
- destructive workflow confirmations remain in the existing command path;
- no repository or SQL access exists in the shortcut layer.

## Schema impact

No persisted data is introduced.

```text
Core database schema: unchanged
UserPreferences feature schema: unchanged
```

## Tests

`WorkflowShortcutTests` validates:

- unique gesture assignments;
- collision avoidance with established shell navigation shortcuts;
- direct registrations point to `ICommand` properties;
- disabled commands are not executed;
- enabled commands execute once;
- Save & New executes its existing commands in order;
- relative record selection remains bounded;
- draft-line deletion uses the explicit `Alt+Delete` gesture;
- Search and Close remain shell-level actions.
