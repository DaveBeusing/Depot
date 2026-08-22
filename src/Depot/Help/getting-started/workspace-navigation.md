# Workspace Navigation

## Summary
Depot uses a compact activity bar, closeable workspace tabs, contextual section navigation, and keyboard-first navigation inspired by modern development tools.

After sign-in, Depot starts on a tabless **Welcome** page. The Welcome page greets the signed-in user according to the time of day and shows shortcuts for Quick Open, the Command Palette, tab switching, tab closing, and Help. Select a module from the activity bar when you are ready to begin work.

## Prerequisites
- You are signed in.
- Your roles grant access to the modules you want to open.

## Top-level workspaces
Depending on permissions, the activity bar can expose:

- **Dashboard** — permission-aware operational overview. Administrators see available overview metrics across Inventory, Purchasing, Warehouse, Sales, Approvals, Administration, and Reports.
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
4. Use **Ctrl+P** for Quick Open to find workspaces, sections, operational records, purchase records, and sales records.
5. Use **Ctrl+Shift+P** to open the Command Palette and navigate or start common workflows.
6. Use **Ctrl+Tab** and **Ctrl+Shift+Tab** to move between open workspace tabs.
7. Use **Alt+Left** and **Alt+Right** to move backward and forward through navigation history.
8. Use **Ctrl+W**, middle-click, the tab close button, or tab context actions to close tabs. Every workspace tab can be closed.
9. When the final tab is closed, Depot returns to the tabless Welcome page.
10. Press **F1** to open context-sensitive Help in its own workspace tab.

## Result
You can keep multiple workflows and supported records open at once. Closing every tab returns the shell to the Welcome page without opening a Welcome tab.

## Status bar
The status bar shows the current database connection state. Hover the database status indicator to see the detail for the currently configured connection, such as the SQLite database path or server/database endpoint.

The current Depot application version is shown on the right side of the status bar. Select the version to open the existing **About** page in a workspace tab.

## Keyboard shortcuts
| Shortcut | Action |
| --- | --- |
| `Ctrl+P` | Open Quick Open |
| `Ctrl+Shift+P` | Open Command Palette |
| `Ctrl+W` | Close the active workspace tab |
| `Ctrl+Tab` | Select the next workspace tab |
| `Ctrl+Shift+Tab` | Select the previous workspace tab |
| `Alt+Left` | Navigate backward |
| `Alt+Right` | Navigate forward |
| `F1` | Open context Help |

## Unsaved changes
Depot protects unsaved changes in supported editors. When navigation, tab closing, sign-out, or application closing would discard modified editor data, Depot asks for confirmation first.

> [!NOTE] Selecting **Discard changes** restores the last loaded or saved state before navigation continues.

## Quick Open
Quick Open groups results by type and shows badges for supported workspaces, sections, and records. With an empty search field, recently opened records are shown first for the current application session.

Supported Sales records open as keyed document tabs so reopening the same supported record activates the existing tab instead of creating a duplicate. Shipment and Customer Return results navigate to **Warehouse > Shipping**. Commercial records remain in the **Sales** workspace.

## Command Palette
The Command Palette centralizes available navigation and workflow commands. Commands remain permission-aware and use stable shell routes rather than visible labels as navigation contracts.

## Common problems
- A workspace, section, result, or command is hidden when your roles do not grant the required permission.
- If all tabs disappear, this is expected: Depot returns to the Welcome page.
- If navigation appears blocked, check whether an unsaved-changes confirmation dialog is waiting for a decision.
- If database details are needed, hover the database indicator in the status bar.

## Required permissions
No additional permission is required for the shell itself. Every workspace, section, record, and command still uses its normal application permission.

## Related topics
- [First Login](topic:getting-started.first-login)
- [Items](topic:inventory.items)
- [Purchase Orders](topic:purchasing.purchase-orders)
- [Sales Overview](topic:sales.overview)
- [Inventory Counts](topic:warehouse.inventory-counts)
- [Stock Transfers](topic:warehouse.transfers)
- [Shipping](topic:sales.shipping)
- [Approvals](topic:approvals.queue)
