# Finance Architecture

Updated: 2026-09-16

## Purpose

Depot Finance is a jurisdiction-neutral accounting platform. Business modules create controlled financial consequences through Finance services; they do not maintain an independent accounting truth.

## Architectural rule

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
```

Views contain presentation only. ViewModels own UI state, commands and cancellation. Finance services own authorization, accounting/reporting/localization invariants, transactions, idempotency and state transitions. Repositories own provider-neutral persistence/query contracts and row mapping.

## Authoritative accounting flow

```text
Sales / Purchasing / Inventory / Returns / Banking / Fixed Assets
                    ↓
        AR / AP / Inventory Accounting / Banking / Fixed Assets
                    ↓
          FinanceGeneralLedgerService
                    ↓
          Immutable General Ledger
                    ↓
      FinanceFinancialReportingService
                    ↓
      Reports / exports / immutable snapshots

Legal Entity
        ↓ explicit effective assignment
FinanceLocalizationService
        ↓
Pack hierarchy + capability/configuration/procedure registry
```

The General Ledger remains the single accounting authority. Receivables, Payables, Inventory Accounting, Banking and Fixed Assets create or reconcile financial consequences through existing boundaries. Financial Reporting is read/reporting plus immutable snapshot persistence. Localization is metadata/control infrastructure and never posts accounting entries.

## Finance capability boundaries

- **Foundation:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations and number sequences.
- **General Ledger:** immutable balanced journals, reporting-currency snapshots, posting profiles, validation, idempotency, Audit evidence and linked reversals.
- **Accounts Receivable:** customer open items, payments/allocation/write-off, aging/statements/dunning and Sales integration.
- **Accounts Payable:** supplier-document lifecycle, three-way matching, exception authority, payments/allocation/reversal and Purchasing integration.
- **Inventory Accounting:** FIFO valuation, GRNI/COGS, inventory adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **Banking and Payments:** bank accounts, immutable statements, CSV/camt.053 import, payment proposals/execution, reconciliation and cash position.
- **Fixed Assets:** asset classes and masters, GL-authoritative capitalization, deterministic straight-line/no-depreciation schedules, periodic depreciation, impairments, non-destructive transfers, disposal, retained transaction evidence and subledger-to-GL reconciliation.
- **Financial Reporting:** configurable reports, explicit account mappings, deterministic export and immutable snapshots.
- **Localization:** explicit effective-dated assignments, hierarchical localization packs, effective capability/configuration/procedure registry and jurisdiction-extension infrastructure.

## Financial Reporting

`FinanceFinancialReportingService` exposes Trial Balance, General Ledger detail, Balance Sheet, Profit & Loss, Cash Flow, Accounts Receivable aging, Accounts Payable aging, Tax Summary, historical Inventory Valuation and Cost of Goods Sold.

GL-derived reports read persisted `ReportingDebit` / `ReportingCredit`, preserving posting-time FX evidence. AR/AP aging remains in each open item's transaction currency. Historical Inventory Valuation reconstructs valuation evidence. Cash-flow, tax, cash-account and COGS meaning uses explicit `FinanceReportingAccountMapping` configuration rather than account-name/number heuristics.

CSV export is deterministic and culture-invariant. `FinanceReportSnapshot` retains report parameters, parameter/content SHA-256 hashes, canonical CSV, creator and timestamp and is retained as `AuditEvidence`.

## Localization Framework

`FinanceLocalizationService` owns explicit localization activation and hierarchy resolution. `LegalEntity.CountryCode` never selects localization automatically. A legal entity has no effective localization profile until an authorized user creates a `FinanceLocalizationAssignment`; active root assignments for one entity cannot overlap.

The built-in hierarchy is `GENERIC → EU → DE`. Built-in pack identities and built-in registry rows are immutable. Custom packs use the same persistence model and can add regional/country definitions without another schema change when no new executable behavior is required.

`FinanceLocalizationRegistryEntry` support levels are `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly`. These values are responsibility/capability labels, not legal or compliance pass/fail flags.

`FinanceLocalizationAssignment` and `FinanceLocalizationRegistryEntry` are retained `AuditEvidence`. Mutations use service authorization, structured Audit records and optimistic concurrency.

## Permissions

Financial Reporting:

- `FinanceFinancialReporting.View`
- `FinanceFinancialReporting.Manage`
- `FinanceFinancialReporting.Export`
- `FinanceReportSnapshots.Create`

Localization:

- `FinanceLocalization.View`
- `FinanceLocalization.Manage`

Administrator receives the complete permission catalog. UI visibility is not an authorization boundary; services enforce permissions.

## Provider and schema model

Current schema baseline:

- Core database schema: **30**
- Sales feature schema: **15**
- Finance feature schema: **12**

Finance schema evolution is sequential from foundation through General Ledger, subledgers, inventory accounting, banking, reporting and localization. Sales is listed because Finance/provider acceptance crosses order-to-cash and related feature migrations; its current persistence baseline is maintained independently from Finance.

The Finance persistence/runtime path is technically accepted on the database baselines in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md): bundled SQLite, SQL Server 2022 engine 16.x, MariaDB 11.8.9 LTS and MySQL 8.4.11 LTS.

Live acceptance traverses real repositories/services for GL, AR, AP and FIFO/inventory accounting. It additionally persists/imports/reconciles/reverses Banking evidence and generates/exports/snapshots Financial Reporting evidence. The same jobs validate migrations, concurrency/deadlock/retry, restart/re-entry, native remote backup/restore and representative provider load.

MySQL/MariaDB Finance UTC timestamps are normalized as provider-native `DATETIME(6)` values at the data-access boundary; Services and Repositories retain the common UTC contract.

SQLite's dynamic `NUMERIC` affinity does not guarantee the full server-style fixed `DECIMAL(28,9)` magnitude/precision range. The support matrix documents this provider-specific boundary.

## Jurisdiction/compliance boundary

Database-provider technical acceptance is not accounting/legal certification. Localization supplies extension infrastructure and reference semantics; it does not provide a legal opinion, tax determination, statutory filing certification, automatic chart of accounts, VAT rate table, HGB/IFRS policy selection or organization-specific compliance procedure.

Deployments still require accounting-book/chart/calendar/posting-profile/valuation/reporting policy approval, reconciliation/period-end procedures, retention/backup operating procedures and qualified jurisdiction-specific review.

See [Finance Localization](FinanceLocalization.md), [Finance Compliance](FinanceCompliance.md), [Finance Banking](FinanceBanking.md) and [Finance Reporting](FinanceReporting.md).

## Budgeting and variance analysis

Finance schema **11** extends the sequential Finance persistence path with controlled budgeting. `FinanceBudgetingService` owns lifecycle, authorization and approval; `FinanceBudgetingRepository` owns provider-neutral persistence, paging and aggregation. Budget planning data remains separate from General Ledger truth.

Actual-vs-Budget projections compose budget aggregates with `FinanceFinancialReportingService`; they do not duplicate journal or reporting calculations. Approved and Locked versions are immutable; amendments create new Draft versions.

See [Finance Budgeting and Variance Analysis](FinanceBudgeting.md).


## SEPA SCT payment initiation artifact

Finance schema **12** extends Banking with a dedicated payment-export boundary. `FinanceSepaPaymentExportService` consumes approved payment-run evidence, validates explicit debtor/creditor payment profiles, generates deterministic `pain.001.001.09` XML and atomically retains the exact bytes plus SHA-256 and source evidence. Bank submission remains an external system boundary rather than a distributed transaction.


## Project Accounting integration

Project Accounting is a consumer of Finance evidence, not a Finance posting authority. Project actuals are read-only projections over posted General Ledger lines reached through explicit project attribution. Project budget links reference existing `FinanceBudgetLines`; Finance Budgeting continues to own amounts, account/period granularity, version lifecycle and approval/locking semantics.

Open Purchase Order commitments are intentionally not posted into Finance merely to support project analysis. They remain a separate operational projection until authoritative downstream accounting evidence exists. Project Accounting feature schema **1** is versioned independently from Finance schema **12**.
