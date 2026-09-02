# UI/UX Rollout Status

Updated: 2026-09-02

This document records the verified state of the repository-wide workspace migration against `UiUxDesignContract.md`. Status values describe the current rollout honestly; a module is not marked complete while material production workspaces still require migration or review.

| Module | Status |
| --- | --- |
| Inventory | Partial |
| Purchasing | Partial |
| Warehouse | Partial |
| Sales | Partial |
| Finance | Partial |
| Administration | Partial |

## Inventory — Partial

### Migrated / reviewed
- `InventoryView`: canonical `PageHeader` / `OperationPanel` ordering, flat workspace sections for inventory and traceability collections, shared `AppDataGridStyle`, numeric alignment, explicit empty states and master/detail inspection.
- `ItemsView`: reviewed as an existing reference implementation using SearchBox, status filtering, `AppDataGridStyle`, pagination, `WorkflowListState`, master/detail editing and permission-aware item-cost actions.

### Open UX work
- Review remaining Inventory movement and item-related master-data workspaces against the same flat entity/collection grammar.
- Further reduce oversized entity surfaces where field groups can be expressed through semantic workspace sections without changing item-master behavior.
- Differentiate search/filter empty-state wording where a view currently exposes only the generic shared list state.

## Purchasing — Partial

### Migrated / reviewed
- `PurchaseOverviewView`: canonical page header and operational KPI hierarchy.
- `PurchaseOrdersView`: reviewed; existing search/status filters, workflow status badges, pagination, document editor, immutable status history and workflow action bar already use the shared interaction primitives.
- Purchase approvals were reviewed for existing search/filter and permission-aware approval behavior.

### Open UX work
- Review Suppliers, Goods Receipts, Supplier Returns and remaining purchasing workspaces for the same collection/entity/process hierarchy.
- Evaluate whether purchase-order collection comparison benefits from a dense DataGrid instead of the existing master list without weakening the current editor workflow.

## Warehouse — Partial

### Migrated / reviewed
- `StockTransfersView`: canonical workspace header, flat collection/process sections, existing validation and workflow action hierarchy retained, posted-movement and reversal blocks kept visually distinct.

### Open UX work
- Migrate Material Issue, Material Return, Receiving, Shipping and Inventory Count workspaces consistently.
- Review warehouse process screens at supported desktop sizes for source/destination, quantity and commit-action prominence.

## Sales — Partial

### Migrated / reviewed
- `SalesCommercialHubView`: shared `PageHeader` and `AppTabControlStyle`; quote and pricing tabs remain permission-aware.
- `SalesView`, pricing and shipping surfaces were reviewed for existing shared DataGrid and workflow usage.

### Open UX work
- Continue flattening Customer and Sales Order master/detail surfaces where current Cards are ordinary data containers.
- Review Sales Order, Customer, Price List, Shipment and approval collections for consistent commercial comparison columns, empty states and action hierarchy.
- Preserve the scoped Customer → Region → Global price-list model while changing presentation only.

## Finance — Partial

### Migrated / reviewed
- `FinanceFinancialReportingView`: Refresh and Export use secondary action treatment; Generate remains primary; report parameters use the shared workspace toolbar; mapping forms use flat workspace sections; numeric report columns use shared numeric alignment.
- Existing Finance DataGrid usage across Banking, Payables and Localization was reviewed as part of the central grid standardization.

### Open UX work
- Apply the same dense, data-first hierarchy to Receivables, Payables, Banking, Inventory Accounting and Localization.
- Continue auditing posted/immutable evidence presentation so booked data is never visually confused with editable draft state.
- Retain General Ledger authority and existing Finance posting/subledger invariants.

## Administration — Partial

### Migrated / reviewed
- `UserSessionsView`: canonical page header, KPI cards retained for summary metrics, session policy moved to a flat workspace section, active/history grids moved to flat collection surfaces, existing RBAC-gated termination and policy commands retained.
- Administration routing continues to use the existing `ShellModuleContentControl` and current view-model boundaries.

### Open UX work
- Review Users, Roles, Permissions, Settings, Database configuration, Audit Log, Security Center and system-information workspaces for the same compact collection/entity grammar.
- Preserve all existing confirmation, RBAC and audit behavior for security-sensitive actions.

## Shared ERP grid productivity

The central DataGrid resources now:

- retain row and column virtualization plus recycling;
- use semantic control-height tokens instead of per-grid row/header magic numbers;
- retain shared text and numeric alignment styles;
- retain keyboard-focus and selection treatment;
- expose `AppDataGridCompactStyle` as a reusable high-density variant using `ControlHeight.M` rows;
- keep `AppDataGridStyle` as the default surface.

Compact density is optional and must be selected deliberately for genuinely high-density tables. It does not introduce column selection, saved views or user preference persistence.

## Deliberately deferred

The following remain follow-up work because they require a broader persistence/state architecture or additional module-specific migration:

- persistent Saved Views;
- user-specific filter state;
- persisted column configuration and sort order;
- user-selectable grid density;
- saved workspaces and favorites;
- user-specific default views;
- optional global command/search productivity patterns.

These belong to the planned follow-up package `ERP Productivity – Saved Views, User Preferences & Advanced Grid Workspaces` once the workspace rollout reaches full module coverage.

## Architecture and data boundaries

This rollout does not introduce a database-schema change. No business process, provider-specific data path, RBAC decision, audit operation, transaction guarantee or concurrency/cancellation contract is intentionally changed by the UI migration.
