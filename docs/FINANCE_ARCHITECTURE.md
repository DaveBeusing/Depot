# Finance Architecture

Updated: 2026-08-28

## Purpose

Depot Finance is a jurisdiction-neutral accounting platform layer. Business modules create controlled financial consequences through Finance services; they do not maintain an independent accounting truth. The implemented baseline covers **F0 through F7**.

## Architectural rule

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
```

Views contain presentation only. ViewModels own UI state, commands and cancellation. Finance services own authorization, accounting/reporting/localization invariants, transactions, idempotency and state transitions. Repositories own provider-neutral persistence/query contracts and row mapping.

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

Legal Entity (F0)
        ↓ explicit effective assignment
FinanceLocalizationService (F7)
        ↓
Pack hierarchy + capability/configuration/procedure registry
```

F1 remains the single General Ledger authority. F2-F5 create or reconcile financial consequences through existing boundaries. F6 is a read/reporting and snapshot layer; it never posts a parallel accounting ledger. F7 is a localization metadata/control boundary and does not post accounting entries.

## Package boundaries

- **F0:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, books, journals, dimensions, tax registrations, number sequences and extension contracts.
- **F1:** immutable balanced journals, reporting-currency snapshots, posting profiles, period/account/dimension validation, number allocation, idempotency, Audit evidence and linked reversals.
- **F2:** Accounts Receivable, payments/allocation/write-off, aging/statements/dunning and Sales integration.
- **F3:** Accounts Payable, supplier-document lifecycle, three-way matching, exception authority, payments/allocation/reversal and AP integration.
- **F4:** FIFO Inventory Accounting, GRNI/COGS, inventory adjustments, PPV, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **F5:** bank accounts, immutable statements, CSV/camt.053 import, payment proposals/execution, reconciliation and cash position.
- **F6:** financial reporting, explicit report classification mappings, deterministic export and immutable report snapshots.
- **F7:** explicit effective-dated localization assignments, hierarchical localization packs, effective capability/compliance registry and jurisdiction-extension infrastructure.

## F6 — Financial Reporting

`FinanceFinancialReportingService` is the F6 service boundary. It exposes Trial Balance, General Ledger detail, Balance Sheet, Profit & Loss, Cash Flow, Accounts Receivable aging, Accounts Payable aging, Tax Summary, historical Inventory Valuation and Cost of Goods Sold.

GL-derived reports read persisted `ReportingDebit` / `ReportingCredit`, preserving the F1 posting-time FX snapshot. AR/AP aging remains in each open item's transaction currency. Historical Inventory Valuation reconstructs F4 evidence. Cash-flow, tax, cash-account and COGS meaning uses explicit `FinanceReportingAccountMapping` configuration rather than account-name/number heuristics.

CSV export is deterministic and culture-invariant. `FinanceReportSnapshot` retains report parameters, parameter/content SHA-256 hashes, canonical CSV, creator and timestamp and is retained as `AuditEvidence`.

## F7 — Localization Framework

`FinanceLocalizationService` is the F7 service boundary.

### Explicit activation

`LegalEntity.CountryCode` never selects localization automatically. A legal entity has no effective localization profile until an authorized user explicitly creates a `FinanceLocalizationAssignment`. Country is used to validate a country pack. Active root assignments for one legal entity cannot overlap.

This avoids accidental jurisdiction behavior when legal-entity master data changes or when a deployment has not completed Finance/legal/tax acceptance.

### Pack hierarchy

`FinanceLocalizationPack` defines `Generic`, `Regional` and `Country` layers. Parent packs must be broader than children. Generic packs have no parent/country; regional packs have a broader parent and no country; country packs have a broader parent and an ISO 3166-1 alpha-2 country.

The built-in hierarchy is:

```text
GENERIC
  └─ EU
      └─ DE
```

Built-in pack identities are immutable. Custom packs use the same table/model and can add further regional/country definitions without another schema change. Dependency cycles and excessive hierarchy depth fail closed.

### Effective localization profile

`GetEffectiveProfileAsync` resolves the root assignment for the requested date, walks the parent hierarchy, validates the selected country pack against the F0 Legal Entity, then resolves effective registry entries for every pack in the chain.

No inferred compliance state is returned. The profile contains pack/reference evidence plus warnings when deployment configuration or external procedures remain.

### Capability/compliance registry

`FinanceLocalizationRegistryEntry` is an effective-dated record with a requirement code, category, support level, description and reference. Support levels are:

- `SoftwareCapability`
- `ConfigurationRequired`
- `ExternalProcedureRequired`
- `ReferenceOnly`

These values are responsibility/capability labels, not pass/fail compliance flags. Built-in rows are immutable. Custom entries can extend the registry and are protected by optimistic concurrency.

### Retention and audit

`FinanceLocalizationAssignment` and `FinanceLocalizationRegistryEntry` are classified as `AuditEvidence`. Pack/assignment/registry mutations create structured Audit entries. Legal entity, pack and effective-from assignment identity is immutable after creation; changes over time are represented with effective ranges/later records.

## Transaction, concurrency and security

Mutable Finance configuration uses the existing database transaction runner and optimistic versions. Retry-sensitive accounting operations continue to use their existing operation/source idempotency boundaries.

F6 permissions:

- `FinanceFinancialReporting.View`
- `FinanceFinancialReporting.Manage`
- `FinanceFinancialReporting.Export`
- `FinanceReportSnapshots.Create`

F7 permissions:

- `FinanceLocalization.View`
- `FinanceLocalization.Manage`

The default Finance role receives these permissions; Administrator continues to receive the complete permission catalog. UI visibility is not an authorization boundary; services enforce permission checks.

## Provider and schema model

Provider-neutral Finance DDL exists for SQLite, SQL Server and MySQL/MariaDB.

Current schema baseline:

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **9**

Finance migrations are ordered F0 schema 1 → F1 2 → F2 3 → F3 4 → F4 valuation 5 → F4 close/control 6 → F5 Banking 7 → F6 Reporting 8 → F7 Localization 9.

Provider neutrality is a code/design property, not a production certification claim. Live SQL Server/MySQL-MariaDB migration, concurrency, recovery and representative Finance/localization acceptance remain deployment gates.

## Jurisdiction/compliance boundary

F7 supplies extension infrastructure and reference semantics. It does not itself provide a legal opinion, tax determination, statutory filing certification, automatic chart of accounts, VAT rate table, HGB/IFRS policy selection or organization-specific compliance procedure. A jurisdiction that needs new executable software behavior may still require a separately scoped code package built on the F7 framework.

See `FINANCE_LOCALIZATION.md` and `FINANCE_COMPLIANCE.md`.
