# UI/UX Rollout Work Packages

Updated: 2026-09-02

This document breaks the remaining repository-wide UI/UX migration into implementation-sized packages. The packages intentionally prioritize productive ERP workspaces that can be completed without introducing new business logic or database-schema changes.

All packages must preserve the existing `Views → ViewModels → Services → Repositories → DatabaseAccess` architecture, RBAC decisions, audit behavior, transactions, concurrency/cancellation behavior, and immutable posting/history semantics.

## Package 1 — Warehouse and Purchasing Process Workspaces

### Goal
Bring the remaining operational stock and receipt/return processes onto the canonical workspace hierarchy while preserving posting and reversal/correction behavior.

### Primary targets
- `MaterialIssuesView`
- `MaterialReturnsView`
- `InventoryCountsView`
- `GoodsReceiptsView`
- `SupplierReturnsView`
- supplier master-data workspace where low-risk structural alignment is possible

### Required UX outcomes
- canonical `PageHeader → OperationPanel → content` hierarchy;
- collection/process surfaces use shared workspace primitives;
- primary posting/start/review actions remain visually dominant;
- destructive reversal/correction actions remain clearly separated;
- dense line/movement tables use shared grid alignment/density patterns where appropriate;
- empty/loading/error behavior remains explicit.

## Package 2 — Sales Commercial Workspaces

### Goal
Move the core sales execution workspaces below `SalesCommercialHubView` onto a consistent commercial collection/entity/process grammar.

### Primary targets
- `SalesCustomersView`
- `SalesOrdersView`
- `SalesQuotesView`
- `SalesPricingView`
- `SalesShippingView`
- `SalesInvoicesView`
- `SalesApprovalsView`
- `SalesView`

### Required UX outcomes
- consistent page/action hierarchy;
- clearer comparison surfaces for customers, orders, quotes, invoices and shipments;
- ordinary data containers flattened where safe;
- consistent numeric/date/status presentation;
- scoped Customer → Region → Global pricing behavior unchanged;
- approval and posting commands remain permission-aware.

## Package 3 — Finance Operational Workspaces

### Goal
Apply the data-first reporting grammar to the operational Finance workspaces without weakening accounting evidence boundaries.

### Primary targets
- `FinanceReceivablesView`
- `FinancePayablesView`
- `FinanceBankingView`
- `FinanceInventoryAccountingView`
- `FinanceLocalizationView`

### Required UX outcomes
- canonical page hierarchy and shared tab presentation;
- high-density financial tables use consistent numeric alignment;
- draft/editable controls are visually distinct from posted/immutable evidence;
- Refresh/Export/navigation actions remain secondary to posting/matching/settlement actions;
- General Ledger and subledger authority remain unchanged.

## Package 4 — Administration and Security Workspaces

### Goal
Standardize the administration surfaces while preserving all security-sensitive confirmation, RBAC and audit behavior.

### Primary targets
- `Users/UsersView`
- `Users/RolesView`
- `Administration/AuditLogView`
- `Administration/SecurityCenterView`
- `Administration/DatabaseSettingsView`
- `Administration/CompanyProfileView`
- `Administration/PrivacyDataView`
- `Administration/AboutView`

### Required UX outcomes
- compact collection/entity hierarchy;
- consistent security/audit tables;
- destructive and security-sensitive actions remain explicit;
- settings forms use shared field/section grammar;
- system-information pages remain read-only and clearly separated from configuration.

## Package 5 — Inventory Completion and Cross-Module Verification

### Goal
Close the remaining Inventory gaps and perform a repository-wide consistency pass before the rollout is declared complete.

### Primary targets
- `MovementsView`
- `ItemsView` follow-up cleanup where required
- remaining item/master-data and warehouse master-data workspaces
- rollout documentation and changed-workspace verification

### Required UX outcomes
- remaining collection/entity surfaces align with the design contract;
- search/filter empty states are context-specific where practical;
- dense tables use the shared grid variants deliberately;
- no mock controls are introduced;
- module status is changed from `Partial` only after the productive views in that module have been migrated/reviewed.

## Deliberately separate follow-up package

The following remain outside this rollout because they require new persisted preference/state architecture rather than presentation-only migration:

- persistent Saved Views;
- user-specific filter state;
- persisted column selection/order/width/sort;
- user-selectable grid density;
- saved workspaces/favorites;
- user-specific default views;
- broader command/search productivity features.

These belong to `ERP Productivity – Saved Views, User Preferences & Advanced Grid Workspaces`.
