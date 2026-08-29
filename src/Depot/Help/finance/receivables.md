# Accounts Receivable

Finance F2 adds Depot's customer subledger on top of the F1 General Ledger posting engine. The Receivables workspace is available under **Finance > Receivables** when the signed-in user has `FinanceReceivables.View`.

## Configuration boundary

Accounts Receivable is enabled by an active AR configuration that identifies:

- the legal entity;
- the fiscal calendar used to resolve the posting period;
- the Sales Invoice posting profile;
- the Sales Credit Note posting profile;
- the customer-payment posting profile;
- the write-off posting profile.

All configured posting profiles must belong to the same legal entity and accounting book and must use the expected source type/event and amount keys. Depot does not seed account numbers, tax accounts, bank accounts, write-off accounts, currencies, or jurisdiction-specific defaults.

F2 depends on the Sales feature schema because the customer subledger uses the existing Customer master and consumes Sales Invoice/Credit Note sources. The F2 migration therefore ensures the current Sales schema before applying Finance feature schema 3.

## Sales Invoice and Credit Note integration

When an active AR configuration exists, posting a Sales Invoice also creates, in the **same database transaction**:

- the controlled F1 General Ledger entry resolved through the configured invoice posting profile;
- one debit receivable open item for the invoice gross amount;
- source/operation identifiers that make an identical retry idempotent;
- central Audit Log evidence.

If AR/GL validation, period resolution, number allocation, persistence, or audit evidence fails, the Sales Invoice posting transaction rolls back rather than leaving Sales and Finance out of sync.

A posted Sales Credit Note creates its own controlled GL entry and credit open item. When the original invoice still has an outstanding debit balance in the same customer/currency/book/legal-entity context, Depot automatically applies as much of the credit as possible to that invoice. Any remaining credit stays available for later allocation.

If Accounts Receivable has not been configured, the existing Sales posting workflow continues without creating AR/GL records. Depot does not silently invent accounting configuration.

## Open items and settlement

Receivable open items are retained accounting-relevant records. Their original amount and source identity remain historical evidence; settlement changes only the controlled remaining balance/version.

F2 supports:

- invoice debit open items;
- credit-note credit open items;
- customer-payment credit open items;
- partial allocations;
- full allocations;
- overpayments that remain as customer credit;
- later allocation of an existing customer credit to another invoice.

An allocation is allowed only between active items for the same customer, currency, accounting book, and legal entity. The allocation amount cannot exceed the available credit or debit balance. Operation IDs plus request fingerprints provide retry safety.

## Customer payments

Posting a payment requires `FinanceReceivablePayments.Post` and an active AR configuration. The payment posting profile creates the GL entry first inside the same transaction, then Depot creates the payment credit open item and any requested allocations.

A payment can be posted with no allocations, a partial allocation, or allocations up to the payment amount. Unallocated value remains available as customer credit.

Payment reversal requires `FinanceReceivablePayments.Reverse`. Reversal creates an explicit F1 reversal journal, reopens every active debit allocation made from that payment credit — including allocations performed after the original payment — voids the payment open item, and retains the original payment/allocation evidence.

## Write-offs

Write-off posting is intentionally separated from normal Finance-role authority. It requires `FinanceReceivableWriteOffs.Post`; reversal requires `FinanceReceivableWriteOffs.Reverse`.

A write-off can reduce only an active debit receivable and cannot exceed its remaining balance. Depot posts the configured write-off profile and updates the open item atomically. Reversal creates a linked GL reversal and restores the written-off balance.

The default Finance system role does **not** receive write-off post/reverse permissions automatically. Administrator receives all catalogued permissions; deployments can assign dedicated custom roles for stricter segregation of duties.

## Aging and customer statements

The Receivables workspace exposes open-item balances and aging by customer and currency. Invoice balances are classified as current, 1-30, 31-60, 61-90, or over 90 days past due based on the selected as-of date. Unapplied customer credits are shown separately rather than being silently netted into an invoice aging bucket.

Customer statement rows are sourced from retained AR open items for the selected customer, currency, and date range.

## Dunning

Dunning requires `FinanceDunning.View` to inspect policies/results and `FinanceDunning.Manage` to maintain policies or create runs.

A dunning policy contains ordered levels with unique level numbers and overdue-day thresholds. A run evaluates active outstanding invoice debit items as of its run date and stores the selected level and outstanding amount as run evidence. Dunning runs are idempotent by operation ID/request content.

F2 records dunning evidence only. It does not claim jurisdiction-specific reminder wording, statutory fee/interest calculation, delivery-channel proof, or collection/legal-process compliance.

## Currency, periods, and General Ledger rules

All AR financial postings reuse the F1 posting boundary. They therefore inherit:

- open-period/date/legal-entity validation;
- transaction/reporting currency handling;
- persisted exchange-rate snapshots for foreign-currency postings;
- account/chart/direct-posting checks;
- required accounting dimensions;
- balanced double entry;
- transactional Finance number allocation;
- operation/source idempotency;
- immutable journals and explicit reversals;
- atomic Audit Log persistence.

F2 does not contain a hidden EUR, country, VAT, chart-of-accounts, or bank-account default.

## Permissions

Key permissions are:

- `FinanceReceivables.View`
- `FinanceReceivables.Manage`
- `FinanceReceivablePayments.Post`
- `FinanceReceivablePayments.Reverse`
- `FinanceReceivableWriteOffs.Post`
- `FinanceReceivableWriteOffs.Reverse`
- `FinanceDunning.View`
- `FinanceDunning.Manage`

The standard Finance system role receives Receivables view/manage, payment post/reverse, and dunning rights. Sensitive write-off authority remains separate.

## Current package boundary

Implemented Finance packages:

- F0 — International Finance Foundation
- F1 — General Ledger & Posting Engine
- F2 — Accounts Receivable

Next is **F3 — Accounts Payable**. Supplier invoices/open items, three-way matching, supplier payment execution, Inventory Accounting, Banking, financial statements, and jurisdiction-specific statutory/localization packages are not provided by F2.

See also: [Finance Foundation](topic:finance.foundation), [General Ledger and Posting](topic:finance.general-ledger), [Sales Invoices and Credit Notes](topic:sales.invoices), and [Audit Log](topic:administration.audit-log).
