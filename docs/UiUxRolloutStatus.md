# UI/UX Rollout Status

Updated: 2026-09-02

This document records the verified state of the repository-wide workspace migration against `UiUxDesignContract.md`. A module is not marked complete while material productive workspaces still require migration or review.

| Module | Status | Current position |
| --- | --- | --- |
| Inventory | Partial | Core inventory, traceability and movement workspaces migrated; item/master-data follow-up remains. |
| Purchasing | Partial | Overview, receipt/return processes and supplier master migrated/reviewed; purchase-order/approval detail pass remains. |
| Warehouse | Partial | Transfer, issue, return and physical-count processes migrated; warehouse master-data and desktop-width review remain. |
| Sales | Partial | Commercial hub plus six execution workspaces migrated; Customer and large Sales overview remain. |
| Finance | Partial | Reporting, Banking, Inventory Accounting and Localization migrated; AR/AP need final consistency pass. |
| Administration | Partial | Sessions, Users, Roles, Audit and Security Center migrated; Database/Company/Privacy/About remain. |

## Inventory — Partial

### Migrated / reviewed
- `InventoryView`: canonical `PageHeader` / `OperationPanel` ordering, flat workspace sections for inventory and traceability collections, shared grids, numeric alignment, explicit empty states and master/detail inspection.
- `MovementsView`: canonical page hierarchy, flat movement-history and creation sections, compact movement history and existing explicit reversal workflow retained.
- `ItemsView`: reviewed as an existing reference implementation using SearchBox, status filtering, shared grids, pagination, `WorkflowListState`, master/detail editing and permission-aware item-cost actions.

### Open UX work
- Complete the item-master follow-up where oversized data groups can safely move to semantic workspace sections.
- Review remaining item/reference-data and warehouse-master surfaces.
- Differentiate search/filter empty-state wording where a view still relies on a generic shared list state.

## Purchasing — Partial

### Migrated / reviewed
- `PurchaseOverviewView`: canonical page header and operational KPI hierarchy.
- `GoodsReceiptsView`: canonical process hierarchy, flat master/detail workspaces and compact receipt/movement evidence tables.
- `SupplierReturnsView`: canonical process hierarchy, flat draft workspace, compact lines/evidence and explicit counter-booking block.
- `SuppliersView`: canonical page header, flat master/detail surfaces, compact supplier-item table and preserved activation behavior.
- `PurchaseOrdersView`: reviewed; existing search/status filters, workflow badges, pagination, document editor, immutable history and workflow action bar already use shared primitives.
- `PurchaseOrderApprovalsView`: reviewed for existing permission-aware approval behavior.

### Open UX work
- Final consistency pass on Purchase Orders and Purchase Approvals, especially collection comparison density and ordinary data containers.
- Verify supplier/purchasing process layouts at supported desktop widths.

## Warehouse — Partial

### Migrated / reviewed
- `StockTransfersView`: canonical workspace header, flat collection/process sections, validation and workflow action hierarchy retained, posted-movement and reversal blocks visually distinct.
- `MaterialIssuesView`: canonical page hierarchy, flat collection/editor areas, compact line/movement tables and explicit reversal controls.
- `MaterialReturnsView`: canonical page hierarchy, flat collection/editor areas, compact line/movement tables and explicit counter-booking correction controls.
- `InventoryCountsView`: canonical page hierarchy, flat count editor/position workspace and compact count-position table while retaining draft/counting/review/post transitions.

### Open UX work
- Review warehouse/location master-data workspaces against the same collection/entity grammar.
- Perform a final desktop-width pass for source/destination, quantity and commit-action prominence.

## Sales — Partial

### Migrated / reviewed
- `SalesCommercialHubView`: shared `PageHeader` and `AppTabControlStyle`; permission-aware tabs retained.
- `SalesOrdersView`: canonical page hierarchy, flat order master/editor surfaces, compact commercial line comparison and unchanged reservation/release commands.
- `SalesQuotesView`: canonical collection/editor hierarchy, compact quote lines and preserved quote lifecycle/conversion actions.
- `SalesInvoicesView`: canonical page/operation hierarchy, flat invoice workspace, compact invoice/credit-note evidence and clear posted-correction separation.
- `SalesShippingView`: canonical page/operation hierarchy, flat shipment workspace, compact pick/return tables and preserved posting/reversal/customer-return behavior.
- `SalesApprovalsView`: canonical page hierarchy, operational KPI cards retained, flat approval queue/detail and compact order lines.
- `SalesPricingView`: canonical page hierarchy, flat price-list/item/bulk/region sections, compact price/cost evidence tables and unchanged Customer → Region → Global pricing semantics.

### Open UX work
- `SalesCustomersView`: migrate the complete Customer master including Addresses, Contacts, Pricing and E-Invoice Identity as one atomic change.
- `SalesView`: final pass on the large commercial overview/workspace.
- Final review of commercial empty states and comparison columns after Customer/overview migration.

## Finance — Partial

### Migrated / reviewed
- `FinanceFinancialReportingView`: secondary Refresh/Export actions, primary Generate, shared toolbar, flat mapping section and numeric alignment.
- `FinanceBankingView`: secondary Refresh, shared tabs, compact cash/statement/payment-run tables and flat account/import/reconciliation/payment sections.
- `FinanceInventoryAccountingView`: secondary Refresh, shared tabs, compact valuation/reconciliation tables, flat configuration/variance/landed-cost workspaces and explicit destructive reversal treatment.
- `FinanceLocalizationView`: secondary Refresh, shared tabs, toolbar-based effective-profile resolution, compact registries and flat assignment/catalog/registry editors.
- `FinanceReceivablesView` and `FinancePayablesView`: reviewed; both already use PageHeader, FilterBar, OperationPanel, KPI metrics and shared numeric grid alignment.

### Open UX work
- Final AR/AP consistency pass: shared tab style, secondary Refresh treatment, ordinary form-card reduction and dense evidence-table review where safe.
- Continue auditing posted/immutable evidence presentation so booked data is never visually confused with editable draft state.
- Retain General Ledger authority and existing Finance posting/subledger invariants.

## Administration — Partial

### Migrated / reviewed
- `UserSessionsView`: canonical page header, KPI cards retained, flat policy and session/history collection surfaces, RBAC-gated termination/policy commands retained.
- `UsersView`: flat user collection/editor sections while retaining local password-validation styles and audited role assignment behavior.
- `RolesView`: flat role collection/editor sections and preserved protected-role/permission behavior.
- `AuditLogView`: canonical page header, compact audit/change evidence grids, flat master/detail surfaces and unchanged sanitized evidence/export behavior.
- `SecurityCenterView`: canonical page header, compact event grid, flat event/policy surfaces and preserved RBAC-gated response actions.

### Open UX work
- `DatabaseSettingsView`: full connection/maintenance/backup surface requires an atomic follow-up pass because provider and recovery controls are tightly coupled.
- `CompanyProfileView`, `PrivacyDataView` and `AboutView`: final form/read-only information standardization.
- Review remaining Administration settings/system-information surfaces and keep security-sensitive confirmations unchanged.

## Shared ERP grid productivity

The central DataGrid resources now:

- retain row and column virtualization plus recycling;
- use semantic control-height tokens instead of per-grid row/header magic numbers;
- retain shared text and numeric alignment styles;
- retain keyboard-focus and selection treatment;
- expose `AppDataGridCompactStyle` as a reusable high-density variant using `ControlHeight.M` rows;
- keep `AppDataGridStyle` as the default surface.

Compact density is now deliberately used on high-density operational/evidence tables in Warehouse, Purchasing, Sales, Finance, Inventory and Administration. It does not introduce persisted user preferences.

## Deliberately deferred

The following remain a separate architecture package rather than presentation-only rollout work:

- persistent Saved Views;
- user-specific filter state;
- persisted column selection/order/width/sort;
- user-selectable grid density;
- saved workspaces and favorites;
- user-specific default views;
- optional global command/search productivity patterns.

These belong to `ERP Productivity – Saved Views, User Preferences & Advanced Grid Workspaces`.

## Architecture and data boundaries

This rollout does not introduce a database-schema change. No business process, provider-specific data path, RBAC decision, audit operation, transaction guarantee or concurrency/cancellation contract is intentionally changed by the UI migration.
