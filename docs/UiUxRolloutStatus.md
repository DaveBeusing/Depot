# UI/UX Rollout Status

Updated: 2026-09-20

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

Compact density is deliberately used for operational lines, movements, financial evidence, pricing previews, audit/security evidence, backups and reports. The original presentation rollout did not add persistence; subsequent User Preferences work now persists supported per-user grid density together with Saved View presentation state.

## Cross-module review

- `DashboardView` was reviewed as already compliant: canonical page/status hierarchy and intentional action/KPI cards.
- `CurrentUserView` was reviewed as a specialized account-information surface; its cards are self-contained account/authorization/permission groups rather than general ERP collection containers.
- shell/navigation wrapper views intentionally do not duplicate child page headers.

## Subsequent productivity integration — Complete

The presentation-only rollout deliberately left persistence and cross-workspace productivity to a separate architecture. That follow-up is now integrated on `master`:

- persistent user-scoped Saved Views for supported workspace filters and presentation state;
- persisted supported column visibility/order/width/sort and user-selectable grid density;
- per-user default Saved Views;
- Favorites, Recent workspaces and a default landing route;
- My Work and role-centered landing surfaces that reuse existing domain/service authority;
- Quick Open, Command Palette and Global Search with permission-filtered discovery.

These capabilities use the existing User Preferences schema **2** and remain presentation/navigation preferences rather than authorization inputs. See [User Workspace Views](UserWorkspaceViews.md), [Workspace Productivity](WorkspaceProductivity.md) and [Track B Productivity Readiness](TrackBProductivityReadiness.md).

## Current visual-designer integration

The current `master` also contains productive visual designer surfaces for document templates, Finance posting flows, Sales pricing strategy, financial-report mappings, bank reconciliation, localization hierarchy, role permissions and import mapping. Designer-specific domain models, validators, services and persistence remain feature-owned; shared UI infrastructure is extracted only where current implementations have identical interaction semantics.

## Architecture and data boundaries

- Core database schema remains `DatabaseVersion.CurrentVersion = 30`; subsequent personalization uses User Preferences schema **2**.
- Business logic: unchanged.
- `Views → ViewModels → Services → Repositories → DatabaseAccess`: preserved.
- RBAC, audit, transaction, immutable-history, concurrency and cancellation contracts: preserved.

The original rollout branch/PR is historical. Current acceptance is governed by the repository's active CI, quality, security, packaged-E2E and database-provider workflows on each current change.


## Cross-app consistency — Complete

The cross-app consistency pass reuses the existing productive workspace grammar rather than introducing a parallel UI model:

- `DocumentStatusBadge` resolves shared status semantics and combines glyph plus text with the existing semantic badge variants;
- workspace tabs retain the existing icon and unsaved-change marker, add visible keyboard focus, and can reopen the last closed non-document workspace tab for the current session;
- the shell status bar keeps connection, active workspace, safe provider/database context and version visible without exposing connection strings, credentials, hosts or full local paths;
- Command Palette, Quick Open and Global Search use the canonical group order **Suggested → Commands → Workspaces → Records → Recent**;
- Welcome/first-run language starts from continuing work, My Work and Search/Quick Open instead of a module-first instruction;
- notification navigation continues to deep-link through the existing service/navigation boundary, while My Work remains the actionable-work projection;
- productive tables continue to use the already-shared `AppDataGridStyle`, compact/numeric styles, saved views, filters and detail-pane patterns; no duplicate generic grid system was added.

The cross-app consistency pass itself did not change persisted schemas, Help topic IDs, permission mappings or routes. Later User Preferences and productivity work is represented separately by its current schema and documentation.
