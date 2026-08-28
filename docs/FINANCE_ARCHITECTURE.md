# Finance architecture

Updated: 2026-08-27

## Purpose

Finance F0 establishes a provider-neutral, jurisdiction-neutral accounting foundation without introducing posting behavior prematurely. It is the base for General Ledger, Accounts Receivable, Accounts Payable, inventory accounting, banking, statutory/localization extensions, and reporting.

## Architectural boundary

The Finance foundation follows Depot's normal architecture and does not create a parallel subsystem:

```text
Views -> ViewModels -> Services -> Repositories -> DatabaseAccess
                                      |
                        SQLite / SQL Server / MySQL-MariaDB
```

F0 intentionally stops before a Finance UI and before ledger posting. Domain contracts live under `Models`, provider/localization contracts under `Services`, and the additive feature schema under `Data`.

## Jurisdiction neutrality

The core Finance foundation must not encode assumptions such as:

- Germany as the legal jurisdiction;
- EUR as a default or functional currency;
- 19% or any other tax rate;
- SKR03/SKR04 or another national chart of accounts;
- IFRS, HGB, US GAAP, or another accounting standard as a fixed enum/default;
- XRechnung as the generic finance-document format.

Country and currency codes validate ISO-style syntax only. Whether a code is currently assigned by the relevant standards body belongs to reference/localization data rather than hard-coded business logic.

## F0 domain model

F0 introduces:

- `CurrencyCode` and `FinanceCurrency`;
- `LegalEntity`;
- `TaxRegistration`;
- `ExchangeRate`;
- `FiscalCalendar` and `AccountingPeriod`;
- `ChartOfAccounts` and `FinanceAccount`;
- `AccountingBook`;
- `JournalDefinition`;
- `AccountingDimension` and `AccountingDimensionValue`;
- `FinanceNumberSequence`.

Validation enforces structural invariants such as ISO-style code syntax, positive exchange rates, valid date ranges, non-empty identities/codes, and positive number-sequence counters.

## Localization and determination contracts

`IExchangeRateSource` allows exchange-rate acquisition to be implemented by explicit sources. Every returned rate retains a source code and effective timestamp.

`ITaxDeterminationService` is the boundary for jurisdiction-specific tax determination. F0 deliberately provides no implicit tax calculation or fallback rate.

`IFinanceLocalizationProvider` is the boundary for country-specific requirements such as tax-registration schemes. Local requirements can therefore be added without contaminating `Finance.Core` concepts.

## Persistence and schema versioning

Finance uses the existing `DepotFeatureVersions` registry and starts with **Finance feature schema version 1**. This schema is independent of application SemVer and of the core database schema (currently 29).

Finance schema version 1 creates provider-specific equivalents of:

- `FinanceCurrencies`
- `FinanceLegalEntities`
- `FinanceTaxRegistrations`
- `FinanceExchangeRates`
- `FinanceFiscalCalendars`
- `FinanceAccountingPeriods`
- `FinanceChartsOfAccounts`
- `FinanceAccounts`
- `FinanceAccountingBooks`
- `FinanceJournals`
- `FinanceDimensions`
- `FinanceDimensionValues`
- `FinanceNumberSequences`

No currency, country, tax rate, chart of accounts, book, or legal entity is seeded implicitly. The migration runs after core initialization and the Sales feature migration and is implemented for SQLite, SQL Server, and MySQL/MariaDB.

## RBAC

F0 adds dedicated permissions for Finance read/manage plus exchange rates, periods, accounting books, tax configuration, and number sequences. Permissions are generated through the existing `PermissionCatalog`, synchronized by the existing RBAC seeder, and assigned to the existing Finance system role. The Administrator role continues to receive every catalogued permission through persisted RBAC rather than a hidden bypass.

## F1 hand-off

F1 — General Ledger & Posting Engine will build on this foundation and must add:

- immutable journal-entry headers and lines;
- double-entry balance invariant `sum(debit) == sum(credit)`;
- posting profiles/source-document mappings;
- source-document and operation idempotency;
- accounting-period lock enforcement;
- reversal/correction semantics rather than destructive edits;
- transactionally persisted audit evidence;
- optimistic concurrency and race-safe posting;
- provider-neutral repositories and transaction orchestration.

AR, AP, inventory accounting, banking, and statutory reporting must consume the posting engine rather than creating independent ledger truth.
