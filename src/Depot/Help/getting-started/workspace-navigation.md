# Workspace Navigation

## Summary
Depot uses a compact activity bar, closeable workspace tabs, contextual section navigation, global search, personal workspace shortcuts, and keyboard-first workflow actions.

After sign-in, Depot starts on the permission-aware landing workspace selected for the signed-in user when that route is still available. If the stored preference is unavailable, Depot falls back safely to Dashboard or another permitted workspace. The Welcome page remains the tabless shell fallback when all workspace tabs are closed.

## Prerequisites
- You are signed in.
- Your roles grant access to the modules you want to open.

## Top-level workspaces
Depending on permissions, the activity bar can expose:

- **Dashboard** — permission-aware operational overview plus the personal **My workspace** region.
- **Inventory** — inventory overview, items, and stock movements.
- **Warehouse** — transfers, inventory counts, material issues/returns, and Shipping.
- **Purchasing** — purchase orders, goods receipts, and supplier returns.
- **Sales** — Overview, Quotes, Pricing, Customers, Sales Orders, and Invoices.
- **Approvals** — Purchase Approvals and Sales Approvals in one focused workspace.
- **Reports** — inventory value and stock-distribution reporting with export.
- **Administration** — users, roles, database management, audit log, and application information.

Notifications, Help, and the signed-in user are available from the utility area at the bottom of the activity bar.

## Steps
1. Select an icon in the activity bar to open a top-level workspace as a tab.
2. Open additional activities or supported records as tabs across the top of the workspace area.
3. Use the contextual navigation directly below the tabs to switch between sections inside the active workspace.
4. Use **Ctrl+K** for Global Search across permitted workspaces, commands, Help and supported business records.
5. Use **Ctrl+P** for Quick Open and **Ctrl+Shift+P** for the Command Palette.
6. Use **Ctrl+Tab** and **Ctrl+Shift+Tab** to move between open workspace tabs.
7. Use **Alt+Left** and **Alt+Right** to move backward and forward through navigation history.
8. Use **Ctrl+W**, middle-click, the tab close button, or tab context actions to close tabs.
9. Use **Ctrl+Shift+T** or **Reopen Closed Tab** to reopen the last closed non-document workspace tab in this session.
10. Use the workflow shortcuts below where the focused workspace exposes the corresponding existing command.
11. Press **F1** to open context-sensitive Help in its own workspace tab.

## Global Search
**Ctrl+K** is the canonical global-search shortcut. Results are grouped by type and remain permission-aware. Supported providers include workspaces and commands plus supported Items, Customers, Suppliers, Sales Orders, Purchase Orders, Invoices and journal/document identifiers. Search is bounded and cancellable; a new query supersedes older work.

Quick Open remains available on **Ctrl+P** for compatibility with the established shell navigation workflow. The Command Palette remains available on **Ctrl+Shift+P**.

## My workspace
The Dashboard **My workspace** region provides user-specific productivity shortcuts:

- **Favorites** for pinned workspaces;
- **Recently used** workspaces, kept as a bounded deduplicated list;
- permission-aware **Quick Actions**;
- a personal **Default landing workspace**.

These preferences store stable semantic route identifiers, not visible labels or WPF control names. A stored route is never treated as authorization. If access is later removed, the route is not displayed or opened and Depot uses a safe landing fallback.

## Keyboard shortcuts
### Shell
| Shortcut | Action |
| --- | --- |
| `Ctrl+K` | Open Global Search |
| `Ctrl+P` | Open Quick Open |
| `Ctrl+Shift+P` | Open Command Palette |
| `Ctrl+W` | Close the active workspace tab |
| `Ctrl+Shift+T` | Reopen the last closed workspace tab in this session |
| `Ctrl+Tab` | Select the next workspace tab |
| `Ctrl+Shift+Tab` | Select the previous workspace tab |
| `Alt+Left` | Navigate backward |
| `Alt+Right` | Navigate forward |
| `F1` | Open context Help |

### Workflow productivity
| Shortcut | Action |
| --- | --- |
| `Ctrl+S` | Save the current supported draft/editor |
| `Ctrl+Shift+S` | Save and start a new record where supported |
| `F5` | Refresh the current supported workspace |
| `Ctrl+F` | Open search through the existing shell search path |
| `Alt+Insert` | Add or update a draft line where supported |
| `Alt+Delete` | Delete the selected draft line where supported |
| `Ctrl+PgUp` | Select the previous record in supported editors |
| `Ctrl+PgDn` | Select the next record in supported editors |
| `Ctrl+Enter` | Run the registered primary workflow action |
| `Ctrl+Shift+Enter` | Run the registered context action |

Workflow shortcuts execute the same existing `ICommand` objects as visible controls. Their `CanExecute` state remains authoritative, so shortcuts do not bypass RBAC, validation, confirmation dialogs, transaction boundaries, audit behavior or concurrency protection. `Alt+Delete` is intentionally separate from normal text-editing Delete behavior.

## Result
You can keep multiple workflows and supported records open at once, find permitted work globally, return quickly to personal workspaces, and use the same business actions from keyboard or visible controls. Closing every tab returns the shell to the Welcome page.

## Status bar
The status bar keeps the operational context visible: current connection state, active workspace, a safe database/provider label, and the Depot application version.

The shell deliberately does not display connection strings, server hosts, user names, passwords, or full local database paths. Company/legal-entity and accounting-period context is shown here only when a future shared shell context can provide it safely and consistently; Depot does not infer those values from administration screens.

Select the version to open the existing **About** page in a workspace tab.

## Unsaved changes
Depot protects unsaved changes in supported editors. When navigation, tab closing, sign-out, or application closing would discard modified editor data, Depot asks for confirmation first.

> [!NOTE] Selecting **Discard changes** restores the last loaded or saved state before navigation continues.

## Common problems
- A workspace, search result, favorite, recent item or command is hidden when your roles do not grant the required permission.
- A shortcut does nothing when the current workspace does not register that action or its existing command is disabled.
- If a former default landing workspace is no longer permitted, Depot intentionally falls back to an allowed workspace.
- If all tabs disappear, Depot returns to the Welcome page.
- If navigation appears blocked, check whether an unsaved-changes or workflow confirmation dialog is waiting for a decision.

## Required permissions
No additional permission is granted by the shell, search, personal workspace region or keyboard shortcuts. Every workspace, record and action continues to use its normal application permission and service boundary.

## Related topics
- [First Login](topic:getting-started.first-login)
- [Dashboard](topic:getting-started.dashboard)
- [Items](topic:inventory.items)
- [Inventory Overview](topic:inventory.overview)
- [Purchase Orders](topic:purchasing.purchase-orders)
- [Sales Overview](topic:sales.overview)
- [Inventory Counts](topic:warehouse.inventory-counts)
- [Stock Transfers](topic:warehouse.transfers)
- [Shipping](topic:sales.shipping)
- [Approvals](topic:approvals.queue)
