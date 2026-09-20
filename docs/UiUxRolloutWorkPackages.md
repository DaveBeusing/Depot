# UI/UX Rollout Work Packages

Updated: 2026-09-20

The repository-wide UI/UX migration packages defined for `ui-ux-rollout` have been implemented. The productive ERP workspaces were migrated or explicitly reviewed without introducing new business logic or database-schema changes.

All completed packages preserve the existing `Views → ViewModels → Services → Repositories → DatabaseAccess` architecture, RBAC decisions, audit behavior, transactions, concurrency/cancellation behavior, and immutable posting/history semantics.

## Package 1 — Warehouse and Purchasing Process Workspaces — Complete

Completed targets:
- `MaterialIssuesView`
- `MaterialReturnsView`
- `InventoryCountsView`
- `GoodsReceiptsView`
- `SupplierReturnsView`
- `Suppliers/SuppliersView`

Delivered outcomes:
- canonical `PageHeader → OperationPanel → content` hierarchy;
- flat collection/process workspaces;
- preserved posting and reversal/correction behavior;
- compact line/movement evidence where appropriate;
- explicit empty/loading/error treatment through shared controls.

## Package 2 — Sales Commercial Workspaces — Complete

Completed targets:
- `SalesCommercialHubView`
- `SalesView`
- `SalesCustomersView`
- `SalesOrdersView`
- `SalesQuotesView`
- `SalesPricingView`
- `SalesShippingView`
- `SalesInvoicesView`
- `SalesApprovalsView`

Delivered outcomes:
- consistent hub/page/action hierarchy without duplicate page headers;
- atomic Customer workspace covering Addresses, Contacts, Pricing and E-Invoice Identity;
- compact comparison/evidence tables and consistent numeric presentation;
- unchanged Customer → Region → Global pricing semantics;
- unchanged approval, reservation, shipment and invoice-posting behavior.

## Package 3 — Finance Operational Workspaces — Complete

Completed targets:
- `FinanceReceivablesView`
- `FinancePayablesView`
- `FinanceBankingView`
- `FinanceInventoryAccountingView`
- `FinanceLocalizationView`
- `FinanceFinancialReportingView`

Delivered outcomes:
- canonical page hierarchy and shared tabs;
- consistent numeric alignment and evidence density;
- secondary Refresh/Load/Export treatment;
- explicit destructive styling for true reversals/write-offs;
- unchanged General Ledger/subledger/posting semantics.

## Package 4 — Administration and Security Workspaces — Complete

Completed targets:
- `Users/UsersView`
- `Users/RolesView`
- `Administration/UserSessionsView`
- `Administration/AuditLogView`
- `Administration/SecurityCenterView`
- `Administration/DatabaseSettingsView`
- `Administration/PrivacyDataView`
- `Administration/AboutView`

Reviewed as already compliant/specialized:
- `Administration/CompanyProfileView`
- `ImportView`
- `Administration/AdministrationView`

Delivered outcomes:
- consistent security/audit evidence tables;
- explicit destructive/security-sensitive actions;
- shared database/provider configuration grammar;
- privacy discovery/export moved to shared Depot controls;
- system information separated from editable configuration;
- unchanged RBAC, audit, provider, credential, backup/restore and privacy behavior.

## Package 5 — Inventory Completion and Cross-Module Verification — Complete

Completed targets:
- `InventoryView`
- `MovementsView`
- `MasterData/ItemReferenceDataView`
- `Warehouses/WarehouseStructureView`
- `PurchaseOrdersView`
- `PurchaseOrderApprovalsView`
- `ReportsView`

Reviewed as already compliant/specialized:
- `ItemsView`
- `MasterData/MasterDataView`
- `DashboardView`
- `CurrentUserView`

Delivered outcomes:
- remaining collection/entity surfaces aligned with the design contract;
- shared grid variants selected deliberately;
- numeric evidence aligned centrally;
- shell wrappers avoid duplicate child page headers;
- no mock controls or unbound productivity features introduced.

## Final module state

| Module | Status |
| --- | --- |
| Inventory | Complete |
| Purchasing | Complete |
| Warehouse | Complete |
| Sales | Complete |
| Finance | Complete |
| Administration | Complete |

## Historical follow-up boundary

The original `ui-ux-rollout` intentionally excluded persisted preference/state architecture. That boundary is historical rather than a statement about the current product: the later productivity implementation is merged and now provides Saved Views, supported user-specific filter/column/sort state, grid density, Favorites, Recent workspaces, default landing and broader command/search productivity through User Preferences schema **2**.

See [User Workspace Views](UserWorkspaceViews.md), [Workspace Productivity](WorkspaceProductivity.md) and [Track B Productivity Readiness](TrackBProductivityReadiness.md) for the current implemented contract.

## Verification boundary

The `ui-ux-rollout` branch and PR #15 are historical delivery evidence. Current repository changes are validated by the current CI, quality, security, packaged-E2E and database-provider gates rather than by the old rollout branch state.
