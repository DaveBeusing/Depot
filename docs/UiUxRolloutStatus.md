# UI/UX Rollout Status

Updated: 2026-09-02

The repository-wide presentation rollout against `UiUxDesignContract.md` is implementation-complete for the productive Inventory, Purchasing, Warehouse, Sales, Finance and Administration workspaces. Every material production surface in these modules was either migrated during this branch or explicitly reviewed as already compliant with the shared workspace grammar.

| Module | Status | Verified position |
| --- | --- | --- |
| Inventory | Complete | Inventory, items, movements, reports and shared item-reference workspaces migrated/reviewed. |
| Purchasing | Complete | Overview, purchase orders, approvals, receipts, returns and supplier master migrated/reviewed. |
| Warehouse | Complete | Transfers, issues, returns, counts and warehouse/location master migrated/reviewed. |
| Sales | Complete | Commercial hub, overview, customers and all execution workspaces migrated/reviewed. |
| Finance | Complete | Reporting, AR, AP, Banking, Inventory Accounting and Localization migrated/reviewed. |
| Administration | Complete | Users, roles, sessions, security, audit, database, company, privacy, about, import and master-data surfaces migrated/reviewed. |

## Inventory — Complete

### Migrated
- `InventoryView`: canonical `PageHeader → OperationPanel → content` hierarchy, flat inventory/traceability workspaces and explicit collection states.
- `MovementsView`: flat movement history/creation surfaces, compact movement evidence and explicit reversal workflow.
- `MasterData/ItemReferenceDataView`: canonical page/status hierarchy and flat reference-data master/detail workspace.
- `ReportsView`: secondary export action, flat report result section and compact numeric report grids.

### Reviewed — already compliant
- `ItemsView`: shared search/filter controls, grid/pagination/list-state handling, master/detail editing and permission-aware costing actions.
- `MasterData/MasterDataView`: shell-level secondary navigation wrapper; intentionally does not duplicate child `PageHeader` controls.

## Purchasing — Complete

### Migrated
- `PurchaseOverviewView`: canonical header and operational KPI hierarchy.
- `PurchaseOrdersView`: canonical page/status hierarchy, flat collection/editor surfaces, compact order/history tables and preserved workflow actions.
- `PurchaseOrderApprovalsView`: PageHeader actions separated from operational status, flat approval queue/detail and compact numeric order comparison.
- `GoodsReceiptsView`: canonical process hierarchy, flat master/detail workspaces and compact receipt/movement evidence.
- `SupplierReturnsView`: flat draft/process workspace, compact lines/evidence and explicit correction treatment.
- `Suppliers/SuppliersView`: canonical supplier master/detail hierarchy and compact supplier-item evidence.

All purchasing posting, approval, receipt, return and audit semantics remain unchanged.

## Warehouse — Complete

### Migrated
- `StockTransfersView`
- `MaterialIssuesView`
- `MaterialReturnsView`
- `InventoryCountsView`
- `Warehouses/WarehouseStructureView`

The warehouse set now consistently uses shared page/status hierarchy, flat productive sections, explicit primary vs. correction actions and compact movement/count evidence where density benefits operators. Warehouse/location dependency and activation behavior is unchanged.

## Sales — Complete

### Migrated
- `SalesCommercialHubView`
- `SalesView`
- `SalesCustomersView`
- `SalesOrdersView`
- `SalesQuotesView`
- `SalesInvoicesView`
- `SalesShippingView`
- `SalesApprovalsView`
- `SalesPricingView`

`SalesView` remains an embedded hub workspace and therefore intentionally does not introduce a second `PageHeader`. Its operational status and selected workspace now occupy distinct grid rows instead of overlapping.

Customer Addresses, Contacts, Pricing and E-Invoice Identity are retained in one atomic customer workspace. Customer → Region → Global pricing fallback, reservation/release behavior, shipment posting, invoice/credit-note immutability and approval permissions are unchanged.

## Finance — Complete

### Migrated
- `FinanceFinancialReportingView`
- `FinanceReceivablesView`
- `FinancePayablesView`
- `FinanceBankingView`
- `FinanceInventoryAccountingView`
- `FinanceLocalizationView`

Finance uses secondary Refresh/Load/Export actions, shared tabs, right-aligned numeric columns and compact evidence-oriented tables. Reversal/write-off actions are visually destructive while normal posting/approval/settlement actions retain their existing command semantics.

General Ledger authority, subledger ownership, posting rules, fiscal-period logic, valuation, GRNI/COGS, tax and localization behavior are unchanged.

## Administration — Complete

### Migrated
- `Administration/UserSessionsView`
- `Users/UsersView`
- `Users/RolesView`
- `Administration/AuditLogView`
- `Administration/SecurityCenterView`
- `Administration/DatabaseSettingsView`
- `Administration/PrivacyDataView`
- `Administration/AboutView`

### Reviewed — already compliant or intentionally specialized
- `Administration/CompanyProfileView`: already uses `PageHeader`, `OperationPanel`, shared form styles and `WorkflowActionBar`; Legal, Tax, Trade, Regulatory, Payment and Document Default cards are retained as genuinely self-contained configuration groups.
- `ImportView`: canonical process page with validated preview, explicit result state, numeric grid alignment and final workflow action.
- `Administration/AdministrationView`: shell content wrapper only; child workspaces own their page grammar.

Database provider behavior for SQLite, SQL Server and MySQL/MariaDB, credential protection, backup/restore behavior, RBAC, privacy evidence, session/security response and audit semantics are unchanged.

## Shared ERP grid productivity

The central DataGrid resources:

- retain row and column virtualization plus recycling;
- use semantic control-height tokens;
- expose `AppDataGridStyle` as the normal collection surface;
- expose `AppDataGridCompactStyle` for deliberately dense operational/evidence data;
- use `AppDataGridNumericTextStyle` for quantities, prices, costs, percentages, tax, debit/credit, balances and other numeric evidence;
- preserve keyboard focus, selection and horizontal scrolling behavior.

Compact density is deliberately used for operational lines, movements, financial evidence, pricing previews, audit/security evidence, backups and reports. It does not add persisted user preferences.

## Cross-module review

- `DashboardView` was reviewed as already compliant: canonical page/status hierarchy and intentional action/KPI cards.
- `CurrentUserView` was reviewed as a specialized account-information surface; its cards are self-contained account/authorization/permission groups rather than general ERP collection containers.
- shell/navigation wrapper views intentionally do not duplicate child page headers.

## Deliberately deferred

The following require persisted preference/state architecture and are not part of this presentation-only rollout:

- persistent Saved Views;
- user-specific filter state;
- persisted column selection/order/width/sort;
- user-selectable grid density;
- saved workspaces and favorites;
- user-specific default views;
- optional global command/search productivity patterns.

These belong to `ERP Productivity – Saved Views, User Preferences & Advanced Grid Workspaces`.

## Architecture and data boundaries

- Database schema: unchanged (`DatabaseVersion.CurrentVersion = 30`).
- Business logic: unchanged.
- `Views → ViewModels → Services → Repositories → DatabaseAccess`: preserved.
- RBAC, audit, transaction, immutable-history, concurrency and cancellation contracts: preserved.

Build, regression, accessibility, release-integrity and security-supply-chain verification for the final branch head is tracked by PR #15 and its GitHub Actions checks.
