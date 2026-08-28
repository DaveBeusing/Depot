# Finance Architecture

Updated: 2026-08-28

## Purpose

Depot Finance is a jurisdiction-neutral accounting platform layer. Business modules create controlled financial consequences through Finance services; they do not maintain an independent accounting truth. The implemented baseline covers **F0 through F6**. F7 Localization Framework is next.

## Architectural rule

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
```

Views contain presentation only. ViewModels own UI state, commands and cancellation. Finance services own authorization, accounting/reporting invariants, transactions, idempotency and state transitions. Repositories own provider-neutral persistence/query contracts and row mapping.

## Authoritative accounting flow

```text
Sales / Purchasing / Inventory / Returns / Banking
                    ↓
        AR / AP / Inventory Accounting / Banking
                    ↓
          FinanceGeneralLedgerService
                    ↓
          Immutable General Ledger (F1)
                    ↓
      FinanceFinancialReportingService (F6)
                    ↓
      Reports / exports / immutable snapshots
```

F1 remains the single General Ledger authority. F2-F5 create or reconcile financial consequences through existing boundaries. F6 is a read/reporting and snapshot layer; it never posts a parallel accounting ledger.

## Package boundaries

- **F0:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, books, journals, dimensions, tax registrations, number sequences and extension contracts.
- **F1:** immutable balanced journals, reporting-currency snapshots, posting profiles, period/account/dimension validation, number allocation, idempotency, Audit evidence and linked reversals.
- **F2:** Accounts Receivable, payments/allocation/write-off, aging/statements/dunning and Sales integration.
- **F3:** Accounts Payable, supplier-document lifecycle, three-way matching, exception authority, payments/allocation/reversal and AP integration.
- **F4:** FIFO Inventory Accounting, GRNI/COGS, inventory adjustments, PPV, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **F5:** bank accounts, immutable statements, CSV/camt.053 import, payment proposals/execution, reconciliation and cash position.
- **F6:** financial reporting, explicit report classification mappings, deterministic export and immutable report snapshots.

## F6 — Financial Reporting

`FinanceFinancialReportingService` is the F6 service boundary. It exposes:

- Trial Balance;
- General Ledger detail;
- Balance Sheet;
- Profit & Loss;
- Cash Flow;
- Accounts Receivable aging;
- Accounts Payable aging;
- Tax Summary;
- historical Inventory Valuation;
- Cost of Goods Sold.

### Ledger/reporting currency

GL-derived reports read `FinanceJournalEntries` and `FinanceJournalEntryLines` and use persisted `ReportingDebit` / `ReportingCredit`. This preserves the F1 posting-time FX snapshot and avoids recalculating historical GL values from current rates.

AR/AP aging is intentionally presented in each open item's transaction currency. F6 does not manufacture historical reporting-currency values that the subledger does not persist.

Historical Inventory Valuation reconstructs F4 valuation layers/consumptions/reversals/landed-cost effects as of the requested date and applies persisted accounting FX evidence.

### Explicit account classification

`FinanceReportingAccountMapping` binds an account in an accounting book to explicit reporting semantics:

- financial-statement section;
- cash-flow category;
- tax category;
- cash-account flag;
- Cost-of-Goods-Sold flag;
- sort order / active state.

Mappings are validated against account type. Cash accounts cannot classify themselves as Operating/Investing/Financing counterparts. COGS classification requires an expense account. Balance-sheet/P&L sections must be compatible with the account type.

F6 never infers cash-flow or tax meaning from an account number/name. Broad account-type fallback is only used for Balance Sheet/P&L section display and produces warnings when explicit mapping is absent.

### Dimension filtering

GL-derived queries may filter by a configured `DimensionId` + `DimensionValueId` pair through `FinanceJournalLineDimensions`. Supplying only one half fails validation. Aging reports remain subledger reports and do not pretend to support GL-line dimensions that are not stored on open items.

### Snapshots and exports

CSV export is deterministic and culture-invariant. `FinanceReportSnapshot` retains:

- immutable operation ID;
- report kind and parameters;
- accounting book and date/dimension scope;
- SHA-256 parameter hash;
- SHA-256 content hash;
- canonical CSV content;
- creator and UTC timestamp.

Snapshot retries with the same operation ID and identical content return the existing snapshot. Reusing the operation ID for different report content fails closed. Snapshots are `AuditEvidence`; a new assessment creates a new snapshot instead of modifying old evidence.

## Transaction, concurrency and security

Mutable F6 mapping configuration uses optimistic versions and the existing database transaction runner. Snapshot uniqueness protects retry-sensitive writes. Report generation is read-only apart from explicit snapshot creation.

Service authorization is authoritative. F6 permissions are:

- `FinanceFinancialReporting.View`
- `FinanceFinancialReporting.Manage`
- `FinanceFinancialReporting.Export`
- `FinanceReportSnapshots.Create`

The default Finance system role receives these permissions; Administrator continues to receive the complete permission catalog.

## Provider and schema model

Provider-neutral F6 DDL exists for SQLite, SQL Server and MySQL/MariaDB.

Current schema baseline:

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **8**

Finance migrations are ordered F0 schema 1 → F1 2 → F2 3 → F3 4 → F4 valuation 5 → F4 close/control 6 → F5 Banking 7 → F6 Reporting 8.

Provider neutrality is a code/design property, not a production certification claim. Live SQL Server/MySQL-MariaDB migration, concurrency, recovery and representative report-load acceptance remain deployment gates.

## F7 boundary

F6 does not provide jurisdiction-specific statutory layouts, filing schemas, legal tax opinions or country-specific accounting defaults. Those belong to F7 localization/compliance packs or later explicitly scoped packages.
