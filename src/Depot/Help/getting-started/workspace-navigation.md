# Workspace Navigation

## Summary
Depot uses a compact activity bar, persistent workspace tabs, contextual section navigation, and keyboard-first navigation inspired by modern development tools.

The activity bar starts with the Depot logo and shows permission-aware workspace icons. Hover an activity-bar icon to see its workspace name.

## Prerequisites
- You are signed in.
- Your roles grant access to the modules you want to open.

## Top-level workspaces
Depending on permissions, the activity bar can expose:

- **Dashboard** — cross-role operational overview.
- **Inventory** — inventory overview, items, and stock movements.
- **Warehouse** — transfers, inventory counts, material issues/returns, and Shipping.
- **Purchasing** — purchase orders, goods receipts, and supplier returns.
- **Sales** — overview, Quotes, Pricing, Customers, Sales Orders, and Invoices.
- **Approvals** — Purchase Approvals and Sales Approvals in one focused workspace.
- **Reports** — inventory and operational reporting.
- **Administration** — master data, users, roles, database management, audit log, and application information.

Notifications, Help, and the signed-in user are available from the utility area at the bottom of the activity bar.

## Steps
1. Select an icon in the activity bar to open a top-level workspace.
2. Open additional activities as tabs across the top of the workspace area.
3. Use the contextual navigation directly below the tabs to switch between sections inside the active workspace.
4. Use **Ctrl+P** for Quick Open to find workspaces, sections, operational records, purchase records, and sales records.
5. Use **Ctrl+Shift+P** to open the Command Palette and navigate or start common workflows.
6. Use **Ctrl+Tab** and **Ctrl+Shift+Tab** to move between open workspace tabs.
7. Use **Ctrl+W**, middle-click, or the tab close button to close the current workspace tab.
8. Press **F1** to open context-sensitive Help in its own workspace tab.

## Result
You can move between daily workflows without returning to a fixed navigation page or losing the context of other open workspaces.

## Keyboard shortcuts
| Shortcut | Action |
| --- | --- |
| `Ctrl+P` | Open Quick Open |
| `Ctrl+Shift+P` | Open Command Palette |
| `Ctrl+W` | Close the active workspace tab |
| `Ctrl+Tab` | Select the next workspace tab |
| `Ctrl+Shift+Tab` | Select the previous workspace tab |
| `F1` | Open context Help |

## Unsaved changes
Depot protects unsaved changes in supported editors, including Items, Purchase Orders, Suppliers, Supplier Items, Sales Orders, Customers, and Roles. When navigation, tab closing, sign-out, or application closing would discard modified editor data, Depot asks for confirmation first.

> [!NOTE] Selecting **Discard changes** restores the last loaded or saved state before navigation continues.

## Quick Open
Quick Open groups results by type and shows badges such as **ITEM**, **PO**, **SUPPLIER**, **SO**, **SHIPMENT**, **INVOICE**, **WORKSPACE**, and **SECTION**. With an empty search field, recently opened records are shown first for the current application session.

Shipment and Customer Return results navigate to **Warehouse > Shipping**. Sales Orders, Customers, Invoices, and Credit Notes remain in the **Sales** workspace.

## Command Palette
The Command Palette includes navigation and direct workflow actions when the signed-in user has access, including:

- New Item
- New Purchase Order
- Start Inventory Count
- Transfer Stock
- Receive Goods
- New Customer
- New Sales Order
- Open Approval Queue
- Ship Order
- Create Customer Return
- Create Invoice
- Open Notifications
- Open Help
- Open User

## Common problems
- A workspace, section, or command is hidden when your roles do not grant the required permission.
- Closing the final remaining workspace tab is prevented so the application always retains an active workspace.
- If navigation appears blocked, check whether an unsaved-changes confirmation dialog is waiting for a decision.

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
