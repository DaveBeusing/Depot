# General Ledger and Posting

Finance F1 provides Depot's provider-neutral General Ledger posting boundary. It enforces double entry with immutable balanced journals and is intended for controlled accounting services and later source-workflow integrations; no country-specific chart, tax rate, accounting standard, or currency is assumed.

## Posting invariants

Every journal entry is posted atomically and becomes immutable immediately. A valid entry must:

- contain at least two lines;
- have exactly one positive debit or credit amount per line;
- satisfy total debit = total credit in transaction currency;
- use active, directly postable accounts from the accounting book's chart of accounts;
- include every accounting dimension configured as required;
- use an active journal belonging to the accounting book;
- post into an open accounting period belonging to the same legal entity and containing the posting date;
- comply with the configured transaction-currency minor-unit precision.

A failed validation, persistence operation, number-sequence update, reversal-link write, or Audit Log write rolls the complete transaction back.

## Currency and exchange rates

Every journal retains transaction and reporting currency.

If both currencies are identical, the stored exchange-rate snapshot is `1` and no unrelated exchange-rate reference is accepted.

If they differ, an explicit persisted exchange rate is required. The rate pair must match transaction → reporting currency and cannot become effective after the posting date. Depot stores the used exchange-rate identity/rate snapshot on the journal so later reference-data changes cannot reinterpret historical accounting evidence.

Converted line amounts are rounded using the reporting currency's configured minor units. If those rounded reporting amounts no longer form a balanced journal, Depot rejects the posting. It does **not** silently invent a rounding account or hidden adjustment line.

## Idempotency and retries

Every posting has an operation ID and deterministic request fingerprint. Retrying the same operation/content returns the existing journal entry instead of consuming another document number.

Source postings are also unique per accounting book, source type, source ID, and source event. This allows later Sales/AR, Purchasing/AP, Inventory Accounting, and Banking workflows to retry safely.

Reusing an operation ID or source identity for different accounting content is rejected rather than silently treating different accounting requests as the same posting.

## Number sequences

General Ledger entries use an active Finance number sequence for the same legal entity and General Ledger document type.

Number allocation happens inside the posting transaction. If a later line, reversal-link, or Audit Log write fails, the sequence update rolls back with the journal. An identical retry therefore does not create gaps caused by a failed Depot transaction.

## Posting profiles

Posting profiles map named business amount keys to configured debit or credit accounts. A profile also identifies the accounting book, journal, and General Ledger number sequence used by the posting flow.

This lets later business workflows provide business amounts without embedding account numbers in Sales, Purchasing, Inventory, or Banking services.

Profiles are versioned for optimistic concurrency. Create/update validates their referenced accounting objects and writes Audit Log evidence transactionally.

## Manual journals and permissions

Controlled profile-based posting uses `FinanceGeneralLedger.Post`.

Free manual journals additionally require `FinanceManualJournals.Post`. This is intentionally a separate sensitive permission because a free journal can select arbitrary allowed accounts/amounts instead of using a controlled posting profile.

The default Finance system role receives General Ledger view/post/reversal and posting-profile rights. It does **not** receive the manual-journal permission automatically. Administrators receive all catalogued permissions through normal RBAC; custom roles can enforce a stricter segregation-of-duties model.

## Reversals

Posted journal entries are never edited or deleted by the F1 workflow.

A correction creates a new linked reversal entry that swaps the original transaction debit/credit and reporting debit/credit amounts exactly. The original journal remains unchanged, preserves its source/currency/rate evidence, and cannot be reversed a second time.

Both creation of the reversal entry and the reversal action on the original are written to the central Audit Log in the same database transaction.

## Audit and failure behavior

Journal posting is considered successful only when accounting persistence and its required Audit Log evidence commit together.

If the Audit Log write fails, Depot rolls back:

- the journal entry;
- its lines/dimensions;
- the consumed Finance number;
- any reversal link;
- the associated accounting mutation in that transaction.

This prevents an F1 posting from being committed without its required central audit evidence.

## Current UI/source-integration boundary

F1 provides the General Ledger service/repository/schema boundary and does not yet add a dedicated Finance workspace.

Sales, Purchasing, and Inventory are not forced to post GL entries until their respective Finance integration packages provide complete account determination/subledger behavior. This is deliberate: source-document state and accounting truth must become connected atomically by a complete package rather than by partial UI wiring.

Next: **F2 — Accounts Receivable**, including Sales Invoice/Credit Note integration, receivable open items, payment allocations including partial/overpayments, write-offs, dunning, and aging.

See also: [Finance Foundation](topic:finance.foundation), [Audit Log](topic:administration.audit-log), and [Sales Invoices and Credit Notes](topic:sales.invoices).
