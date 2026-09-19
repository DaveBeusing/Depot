# Shell navigation and information architecture

Updated: 2026-09-19

## Primary navigation

Depot keeps two related navigation views:

- the complete permission-filtered shell route catalog in `MainViewModel.NavigationItems`;
- the primary Activity Bar projection in `PrimaryNavigationItems`.

The primary Activity Bar exposes Home, Inventory, Sales, Purchasing, Warehouse, Finance, Reports and Administration when the signed-in user's permissions make those areas available. Role Centers and the dedicated Approvals workspace remain routable but are not separate primary modules.

This separation is presentation-only. Existing route IDs, deep links, permission checks, Favorites, Recents, default landing, Quick Open and command-palette discovery continue to use the complete shell catalog.

## Role Centers

Role Centers are landing workspaces rather than a horizontal mega-module in the main information architecture. Their existing routes stay unchanged and remain available through Home/My Work surfaces, Favorites, Recents, default landing, Quick Open and the command palette. Opening a Role Center suppresses the module-level secondary-navigation strip so the landing workspace itself remains the primary context.

The Role Center route family uses a neutral work/briefcase icon instead of the Finance icon.

## Expandable Activity Bar

The Activity Bar supports two session states:

- collapsed: 52 px, icon only, with tooltips;
- expanded: 220 px, icon plus label.

The toggle is keyboard reachable and exposes an automation name. Toggling only changes shell presentation state and does not navigate or replace the selected workspace.

The expanded/collapsed state is intentionally session-local. User Preferences schema 2 does not currently contain a shell-navigation-width preference; persisting this state would require an explicit persisted preference contract and schema evolution. That persistence is deferred rather than expanding the schema for this presentation-only package.

## Compatibility boundaries

No business workflow, status model, permission, database schema or Help route is introduced by this information-architecture change. Service-side authorization remains authoritative, and users with multiple roles continue to receive the union of routes allowed by their effective permissions.
