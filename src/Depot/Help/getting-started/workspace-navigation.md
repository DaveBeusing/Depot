# Workspace Navigation

## Summary
Depot uses an activity bar, persistent workspace tabs, contextual section navigation, and keyboard-first navigation inspired by modern development tools.

## Prerequisites
- You are signed in.
- Your roles grant access to the modules you want to open.

## Steps
1. Use the activity bar on the left to open a top-level workspace such as Inventory, Warehouse, Purchasing, Approvals, Reports, or Administration.
2. Open additional activities as tabs across the top of the workspace area.
3. Use the contextual navigation below the tabs to switch between sections inside the active workspace.
4. Use **Ctrl+P** for Quick Open to find workspaces, sections, items, purchase orders, and suppliers.
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
Depot protects unsaved changes in supported editors, including Items, Purchase Orders, Suppliers, Supplier Items, and Roles. When navigation, tab closing, sign-out, or application closing would discard modified editor data, Depot asks for confirmation first.

> [!NOTE] Selecting **Discard changes** restores the last loaded or saved state before navigation continues.

## Quick Open
Quick Open groups results by type and shows badges such as **ITEM**, **PO**, **SUPPLIER**, **WORKSPACE**, and **SECTION**. With an empty search field, recently opened records are shown first for the current application session.

## Command Palette
The Command Palette includes navigation and direct workflow actions when the signed-in user has access, including:

- New Item
- New Purchase Order
- Start Inventory Count
- Transfer Stock
- Receive Goods
- Open Notifications
- Open Help
- Open User

## Common problems
- A workspace or command is hidden when your roles do not grant the required permission.
- Closing the final remaining workspace tab is prevented so the application always retains an active workspace.
- If navigation appears blocked, check whether an unsaved-changes confirmation dialog is waiting for a decision.

## Required permissions
No additional permission is required for the shell itself. Every workspace, record, and command still uses its normal application permission.

## Related topics
- [First Login](topic:getting-started.first-login)
- [Items](topic:inventory.items)
- [Purchase Orders](topic:purchasing.purchase-orders)
- [Inventory Counts](topic:warehouse.inventory-counts)
- [Stock Transfers](topic:warehouse.transfers)
