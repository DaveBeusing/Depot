# Finance architecture

Updated: 2026-08-28

## Purpose

Finance F0 and F1 establish a provider-neutral, jurisdiction-neutral accounting foundation and General Ledger posting engine. They are the base for Accounts Receivable, Accounts Payable, inventory accounting, banking, statutory/localization extensions, and financial reporting.

## Architectural boundary

Finance follows Depot's normal architecture and does not create a parallel subsystem:

```text
Views -> ViewModels -> Services -> Repositories -> DatabaseAccess
                                      |
                        SQLite / SQL Server / MySQL-MariaDB
```

F1 remains service/repository-first and intentionally does not expose a partial Finance workspace before source integrations are complete. Domain contracts live under `Models`, accounting orchestration under `Services`, persistence under `Repositories`, and the additive Finance feature schema under `Data`.

## Jurisdiction neutrality

The Finance core must not encode assumptions such as:

- Germany as the legal jurisdiction;
- EUR as a default or functional currency;
- 19% or any other tax rate;
- SKR03/SKR04 or another national chart of accounts;
- IFRS, HGB, US GAAP, or another accounting standard as a fixed enum/default;
- XRechnung as the generic finance-document format.

Country and currency codes validate ISO-style syntax only. Whether a code is currently assigned by the relevant standards body belongs to reference/localization data rather than hard-coded business logic.

## F0 foundation

F0 introduced:

- `CurrencyCode` and `FinanceCurrency`;
- `LegalEntity`;
- `TaxRegistration`;
- `ExchangeRate`;
- `FiscalCalendar` and `AccountingPeriod`;
- `ChartOfAccounts` and `FinanceAccount`;
- `AccountingBook`;
- `JournalDefinition`;
- `AccountingDimension` and `AccountingDimensionValue`;
- `FinanceNumberSequence`;
- exchange-rate, tax-determination, and localization extension interfaces.

No currency, country, tax rate, chart, accounting book, or legal entity is seeded implicitly.

## F1 General Ledger model

F1 adds:

- immutable `FinanceJournalEntry` and `FinanceJournalEntryLine` records;
- per-line dimension snapshots;
- transaction and reporting currency on every journal entry;
- persisted exchange-rate identity and rate snapshot;
- `FinancePostingProfile` and versioned posting-profile lines;
- operation IDs and deterministic request fingerprints;
- source identity (`AccountingBookId`, `SourceType`, `SourceId`, `SourceEvent`);
- explicit original/reversal links.

Journal entries use database identities so the existing Audit Log can retain their creation/reversal evidence directly.

## Posting invariants

`FinanceGeneralLedgerService` is the sole accounting posting boundary. It validates before persistence that:

- every entry has at least two lines;
- each line contains exactly one positive debit or credit amount;
- transaction-currency debit equals credit;
- accounts are active, directly postable, and belong to the book's chart;
- all globally required dimensions are present and use active values;
- book and journal are active and related;
- the accounting period belongs to the book's legal entity, is open, and contains the posting date;
- transaction amounts respect the transaction currency's configured minor units;
- cross-currency postings use an explicit persisted rate matching transaction -> reporting currency;
- the rate is not effective after the posting date;
- reporting-currency debit equals credit after minor-unit rounding.

Depot deliberately does not invent a rounding account. If converted reporting lines are not balanced, the caller must provide an explicit configured balancing/rounding line.

## Idempotency and source-document safety

Every posting carries a GUID operation ID. `FinanceJournalEntries.OperationId` is unique and a SHA-256 request fingerprint excludes only the retry token itself. Repeating an identical operation returns the existing journal entry; reusing an operation ID for different accounting content is rejected.

A second unique boundary protects source documents by accounting book, source type, source ID, and source event. This allows later source workflows to retry safely without creating duplicate accounting truth even when the retry uses a new operation ID.

## Number sequences

General Ledger entries consume only an active Finance number sequence whose `DocumentType` is `Finance.GeneralLedger` and whose legal entity matches the book/period. The sequence row is protected inside the write transaction, advanced with an expected-value guard, and rolled back if any later persistence or audit step fails.

## Posting profiles

Posting profiles map named business amount keys to debit/credit accounts, multiplier, journal, book and number sequence. They avoid embedding account numbers in future AR/AP/inventory/banking services.

Profiles use optimistic `Version` concurrency. Create/update runs transactionally, validates all referenced accounting objects, and writes central Audit Log evidence. Profile-based posting requires normal GL posting permission; free manual journals require a separate sensitive permission.

## Reversals and immutability

Posted journal entries are never updated or deleted. `ReverseAsync` locks the original entry, rejects a second reversal, then creates a new entry with the original transaction and reporting debit/credit amounts swapped exactly. The reversal preserves original currency/rate snapshots and links through `FinanceJournalReversals` plus `ReversalOfEntryId`.

The created reversal and the `Reversed` action on the original journal are written to the Audit Log in the same database transaction.

## Transaction and concurrency model

Finance reuses `IDatabaseTransactionRunner` / `DatabaseAccess` rather than introducing its own unit-of-work implementation:

- SQLite uses an immediate write transaction;
- SQL Server and MySQL/MariaDB use serializable write transactions;
- known transient write conflicts are retried by the existing central data-access layer;
- Finance period and number-sequence rows receive explicit no-op updates before authoritative reads to obtain write ownership in the active transaction;
- unique database constraints remain the final race-safety boundary for operation IDs, source postings, entry numbers, profile codes, line numbers, and reversal links.

Audit writes participate in the same transaction as Finance persistence, so audit failure rolls the journal and number sequence back.

## Persistence and schema versioning

Finance uses the existing `DepotFeatureVersions` registry. F0 is **Finance feature schema version 1**. F1 raises the Finance feature schema to **version 2** without changing the core database schema.

Version 2 adds provider-specific equivalents of:

- `FinanceJournalEntries`
- `FinanceJournalEntryLines`
- `FinanceJournalLineDimensions`
- `FinancePostingProfiles`
- `FinancePostingProfileLines`
- `FinanceJournalReversals`

Migration is sequential: a new database receives Finance v1 and then v2; an existing v1 database receives only the v2 extension. SQLite, SQL Server, and MySQL/MariaDB are all covered.

## RBAC

F1 adds `FinanceGeneralLedger.View`, `.Post`, `.Reverse`, `FinancePostingProfiles.View`, `.Manage`, and `FinanceManualJournals.Post`.

The Finance system role receives controlled GL view/post/reversal and posting-profile rights. `FinanceManualJournals.Post` is intentionally withheld from the default Finance role and remains available to Administrator or explicitly configured custom roles.

## F2 hand-off

F2 — Accounts Receivable must consume the F1 posting engine rather than creating a second ledger truth. Its scope is:

- Sales Invoice and Credit Note accounting integration;
- receivable open items;
- payment allocation including partial and overpayments;
- write-offs;
- dunning and aging;
- source-document posting profiles/account determination.

AP, inventory accounting, banking, and statutory reporting follow the same rule: business processes produce accounting requests; Finance owns immutable accounting truth.
