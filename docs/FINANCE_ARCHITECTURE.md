# Finance architecture

Updated: 2026-08-28

## Purpose

Finance F0, F1, and F2 establish a provider-neutral, jurisdiction-neutral accounting foundation, immutable General Ledger posting engine, and customer Accounts Receivable subledger. They are the base for Accounts Payable, inventory accounting, banking, statutory/localization extensions, and financial reporting.

## Architectural boundary

Finance follows Depot's normal architecture and does not create a parallel subsystem:

```text
Views -> ViewModels -> Services -> Repositories -> DatabaseAccess
                                      |
                        SQLite / SQL Server / MySQL-MariaDB
```

F2 adds a permission-aware **Finance > Receivables** workspace. Domain contracts live under `Models`, accounting/subledger orchestration under `Services`, persistence under `Repositories`, and additive Finance feature schemas under `Data`.

The authority split is explicit:

- `FinanceGeneralLedgerService` owns immutable double-entry accounting truth;
- `FinanceAccountsReceivableService` owns customer open-item/settlement truth;
- Sales owns source business-document truth;
- F2 composes those boundaries transactionally instead of duplicating them.

## Jurisdiction neutrality

The Finance core must not encode assumptions such as Germany as jurisdiction, EUR as default, a specific tax rate, SKR03/SKR04, IFRS/HGB/US-GAAP, XRechnung as a generic finance format, a specific bank/write-off account, or statutory dunning rules.

Country and currency codes validate ISO-style syntax only. Whether codes/rules are currently valid belongs to reference/localization data and deployment governance.

## F0 foundation

F0 introduced currencies, legal entities, tax registrations, exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journal definitions, accounting dimensions/values, Finance number sequences, and exchange-rate/tax/localization extension interfaces. No country/currency/tax/chart/book/entity is seeded implicitly.

## F1 General Ledger model

F1 adds immutable `FinanceJournalEntry`/`FinanceJournalEntryLine`, dimension snapshots, transaction/reporting currency and FX snapshots, posting profiles, operation/source idempotency, and explicit reversal links.

`FinanceGeneralLedgerService` validates balance, period/date/legal entity, active book/journal/accounts, chart membership, direct posting, required dimensions, currency precision/rates, and reporting balance. General Ledger numbers and Audit Log evidence commit in the same transaction. Corrections create linked reversals; posted journals are never edited or deleted.

## F2 Accounts Receivable model

F2 adds:

- `FinanceReceivablesConfiguration`;
- `FinanceReceivableOpenItem`;
- `FinanceReceivableAllocation` plus allocation-operation idempotency;
- `FinanceReceivablePayment` plus explicit reversal metadata;
- `FinanceReceivableWriteOff` plus explicit reversal metadata;
- aging/statement projections;
- `FinanceDunningPolicy`, levels, runs, and retained run lines.

Open items retain legal entity, accounting book, customer, source identity/reference, dates, currency, original/remaining amount, linked GL journal, operation id, version, void state, and actor/time evidence.

Invoices are debit items. Credit notes and customer payments are credit items. Settlement is an explicit allocation between debit and credit open items; it does not rewrite the original Sales document or GL entry.

## Sales -> AR -> GL transaction boundary

F2 is the first Finance package that integrates an operational source directly with the F1 ledger.

With one active AR configuration, Sales Invoice posting performs within the same database transaction:

1. normal Sales invoice state/quantity changes;
2. seller/buyer/XRechnung finalization;
3. F1 posting through the configured Sales Invoice posting profile;
4. creation of one debit AR open item;
5. Finance number allocation;
6. central Audit Log evidence.

Sales Credit Note posting similarly creates a configured F1 journal and credit AR open item and can automatically allocate that credit against the original invoice open item.

Any failure rolls the complete source/subledger/ledger transaction back. When no AR configuration exists, F2 returns without creating accounting records so existing Sales behavior continues; Depot never guesses accounting configuration.

## F2 schema dependency

Unlike F0/F1, F2 genuinely consumes Sales master/source data (`Customers`, Sales Invoice/Credit Note source identities). Therefore `FinanceAccountsReceivableSchemaMigration.Migrate` explicitly executes the current `SalesSchemaMigration` before applying Finance v3.

This makes the dependency correct for normal composition, isolated migrations, clean databases, and tests rather than relying on initialization order as an undocumented assumption.

## AR configuration and account determination

An active AR configuration identifies one Finance legal entity/fiscal calendar and four F1 posting profiles:

- Sales Invoice;
- Sales Credit Note;
- customer payment;
- write-off.

Configuration validation requires expected source type/event and amount-key mappings, active profiles, one legal entity, and one accounting book. Account numbers themselves remain configuration data in posting profiles.

Source amount keys include gross/net/tax for invoice/credit-note posting and dedicated payment/write-off values. Tax determination remains outside F2; F2 consumes the finalized Sales monetary values and does not reinterpret XRechnung tax semantics.

## Payments, allocation, and overpayments

Payment posting is operation-idempotent. It validates an active customer/configuration, resolves an open period and optional FX rate, posts the configured F1 payment journal, creates a payment credit open item, and applies requested allocations atomically.

Allocations require same customer, currency, accounting book, and legal entity. Partial settlement is allowed. An overpayment remains as unallocated credit and can later be assigned to another debit item.

Allocation operations carry an operation ID/request hash so identical retries are safe and conflicting reuse is rejected.

## Reversals and correction integrity

Payment reversal uses the F1 reversal boundary and then reverses every active allocation whose credit source is that payment, including allocations created later. Affected debit balances are restored, allocation rows receive reversal evidence, and the payment open item is voided. The original payment remains retained.

Write-off posting reduces an active debit open item and creates a configured GL journal. Reversal creates the F1 counter-journal and restores the receivable balance. Original write-off evidence remains retained.

## Aging, statements, and dunning

Aging is calculated from current outstanding AR evidence as of a selected date and groups by customer/currency. Debit invoices are bucketed Current, 1-30, 31-60, 61-90, and >90 days. Unapplied credit is reported separately rather than silently netted into an invoice bucket.

Customer statement rows come from retained AR open-item evidence for a customer/currency/date range.

Dunning policies define ordered overdue-day levels. Dunning runs snapshot qualifying outstanding invoice items and selected levels and are idempotent by operation/request hash. F2 deliberately does not implement statutory fee/interest calculation, mandated wording, legal escalation, or delivery proof.

## Idempotency and concurrency

F1 idempotency remains authoritative for GL postings. F2 adds subledger uniqueness/operation records around source open items, payments, allocations, write-offs, and dunning runs.

Settlement/reversal paths lock or version-check authoritative rows within the existing provider-controlled write transaction. Optimistic `Version` guards plus unique constraints remain the final race-safety boundary.

## Persistence and schema versioning

Finance uses `DepotFeatureVersions`:

- v1: F0 foundation;
- v2: F1 General Ledger/posting profiles/reversals;
- v3: F2 Accounts Receivable.

Finance v3 adds provider-specific equivalents of:

- `FinanceReceivablesConfigurations`
- `FinanceReceivableOpenItems`
- `FinanceReceivableAllocations`
- `FinanceReceivableAllocationOperations`
- `FinanceReceivablePayments`
- `FinanceReceivableWriteOffs`
- `FinanceDunningPolicies`
- `FinanceDunningPolicyLevels`
- `FinanceDunningRuns`
- `FinanceDunningRunLines`

SQLite, SQL Server, and MySQL/MariaDB definitions exist. Live remote-provider migration, locking, recovery, and representative concurrent-load acceptance remain production gates.

## RBAC

F2 adds `FinanceReceivables.View`, `FinanceReceivables.Manage`, `FinanceReceivablePayments.Post`, `.Reverse`, `FinanceReceivableWriteOffs.Post`, `.Reverse`, `FinanceDunning.View`, and `.Manage`.

The Finance system role receives normal Receivables, payment, and dunning operations. Write-off post/reverse remains sensitive and is withheld from that default role. `FinanceManualJournals.Post` remains separately protected. Administrator receives catalogued permissions through normal RBAC.

## F3 hand-off

F3 — Accounts Payable must reuse the same architecture: supplier business documents/open items own AP truth while all accounting entries flow through F1.

Planned F3 scope includes supplier invoices/credit notes, AP open items, purchase-order/goods-receipt/invoice matching, approval, supplier settlement preparation, and controlled GL integration. F4 inventory accounting and F5 banking follow the same no-parallel-ledger rule.
