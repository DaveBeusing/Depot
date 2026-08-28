# General Ledger and Posting

Finance F1 provides Depot's provider-neutral General Ledger posting boundary. It is intended for controlled accounting services and later source-workflow integrations; no country-specific chart, tax rate, accounting standard, or currency is assumed.

## Posting invariants

Every journal entry is posted atomically and becomes immutable immediately. A valid entry must:

- contain at least two lines;
- have exactly one positive debit or credit amount per line;
- satisfy total debit = total credit in transaction currency;
- use active, directly postable accounts from the accounting book's chart of accounts;
- include every accounting dimension configured as required;
- use an active journal belonging to the accounting book;
- post into an open accounting period belonging to the same legal entity and containing the posting date;
- comply with the configured currency minor-unit precision.

A failed validation, persistence operation, number-sequence update, or audit write rolls the complete transaction back.

## Currency and exchange rates

The journal retains both transaction and reporting currency. If they are identical, the stored exchange-rate snapshot is `1` and no exchange-rate reference is accepted.

If they differ, an explicit persisted exchange rate is required. The rate pair must match transaction → reporting currency and cannot become effective after the posting date. Depot stores the used rate on the journal entry so later changes to reference data cannot reinterpret the historical posting.

Converted line amounts are rounded using the reporting currency's configured minor units. If those rounded reporting amounts no longer form a balanced journal, Depot rejects the posting. It does not silently invent a rounding account or adjustment line.

## Idempotency

Every posting has an operation ID and a deterministic request fingerprint. Retrying the same operation returns the existing journal entry instead of consuming another document number.

Source postings are also unique per accounting book, source type, source ID, and source event. Repeating the same source payload is safe; attempting to reuse an operation or source identity for different accounting content is rejected.

## Posting profiles

Posting profiles map named amount keys to configured debit or credit accounts. Later AR, AP, inventory-accounting, and banking workflows can therefore supply business amounts without embedding account numbers in those workflows.

Profiles are versioned for optimistic concurrency and are audited when created or changed. Their journal, accounts, book, legal entity, and General Ledger number sequence are validated before persistence.

## Manual journals and permissions

Controlled profile-based posting uses `FinanceGeneralLedger.Post`. Free manual journals additionally require `FinanceManualJournals.Post`.

The Finance system role receives General Ledger view/post/reversal and posting-profile rights. It does **not** receive the manual-journal permission automatically. Administrators receive all catalogued permissions, and custom roles can be used when an organization needs a different segregation-of-duties model.

## Reversals

Posted journal entries are never edited or deleted. A correction creates a new linked reversal entry that swaps the original debit/credit and reporting debit/credit amounts exactly. The original journal remains unchanged and a second reversal of the same original entry is rejected.

Both the created reversal and the reversal action are written to the Audit Log in the same database transaction.

## Current scope

F1 provides the General Ledger service/repository/schema boundary and does not yet add a dedicated Finance workspace. Sales, Purchasing, and Inventory are not forced to post GL entries until their respective Finance integration packages provide complete account determination and open-item behavior.

Next: **F2 — Accounts Receivable**, including Sales Invoice/Credit Note integration, receivable open items, allocations, write-offs, dunning, and aging.

See also: [Finance Foundation](topic:finance.foundation), [Audit Log](topic:administration.audit-log), and [Sales Invoices and Credit Notes](topic:sales.invoices).
