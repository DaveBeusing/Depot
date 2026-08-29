# Accounts Payable

Accounts Payable (F3) manages supplier invoices and supplier credit notes, supplier open items, settlements, aging, supplier statements, purchase-order / goods-receipt matching, approvals, and controlled General Ledger integration.

## Configuration

Accounts Payable has no implicit legal entity, currency, tax, account, journal, payment account, or localization default. Before posting financial transactions, configure an active Accounts Payable setup with:

- the legal entity;
- the fiscal calendar used to resolve the posting period;
- an active supplier-invoice posting profile;
- an active supplier-credit-note posting profile;
- an active supplier-payment posting profile.

All configured posting profiles must belong to the configured legal entity and use one accounting book. Account selection remains configuration-driven through the General Ledger posting profiles.

## Supplier documents

Use **Finance > Payables > Supplier Documents** to create supplier invoices or supplier credit notes. A document remains editable only while it is a draft. Each line stores its quantity, unit price, explicit tax amount and optional purchase-order-line / goods-receipt-line references.

The workflow is:

1. create and save the draft;
2. submit it for approval;
3. approve or reject it;
4. post an approved document;
5. use an explicit reversal if an unsettled posted document must be corrected.

Posting creates the supplier subledger open item and the General Ledger journal entry in the same database transaction. A failure in Finance validation, number allocation, General Ledger posting, AP persistence, or Audit persistence rolls the accounting operation back.

## Three-way matching

For invoice lines linked to a purchase-order line, Depot evaluates the supplier, ordered unit price, actually posted and non-reversed goods-receipt quantity, and quantity already represented by previously approved or posted supplier invoices.

F3 is intentionally fail-closed. There are no implicit percentage, quantity, currency, or price tolerances. A line matches only when the supplier is consistent, the invoiced quantity does not exceed the currently received/uninvoiced quantity, and the invoiced unit price equals the purchase-order unit price. Missing receipt quantity or a price/quantity mismatch creates a **Match Exception**.

An invoice with a match exception cannot be approved as a normal approval. The approver must explicitly approve the exception, have `FinanceSupplierMatchExceptions.Approve`, and provide a reason. This preserves evidence of the exception instead of silently widening a tolerance.

Non-PO supplier documents remain supported. Their lines are marked as matching not required; Depot does not invent a purchase-order relationship.

## Approval and segregation of duties

Supplier-document approval uses the dedicated `FinanceSupplierInvoices.Approve` permission. Match-exception approval uses the separate `FinanceSupplierMatchExceptions.Approve` permission. The default Finance system role receives normal AP creation, submission, posting, reversal, payment and configuration permissions, but does not receive either approval permission automatically.

Role design should keep supplier-document preparation and approval assigned to different people. Administrator-level role design remains an organizational control and must be reviewed during deployment acceptance.

## Posting and open items

A posted supplier invoice creates a credit-direction AP open item. A posted supplier credit note creates a debit-direction open item. Posted documents keep their source identity, posting operation, journal entry, approval evidence, match evidence and timestamps.

Posted General Ledger entries remain immutable. Corrections use explicit linked reversals rather than destructive edits.

## Supplier payments and allocations

A supplier payment creates a debit-direction AP open item and a configured General Ledger posting. It may be allocated immediately to one or more supplier invoice open items, or it may remain partly/unapplied for later allocation.

This supports:

- partial payments;
- full settlement;
- overpayments;
- later allocation of unapplied supplier debits;
- supplier credit-note allocation;
- explicit payment reversal.

Allocations are allowed only between compatible open items for the same supplier, currency, accounting book and legal entity. An allocation cannot exceed the available debit or the invoice balance.

Reversing a supplier payment restores every active allocation made from that payment, voids the payment open item and creates the linked General Ledger reversal. Repeating the same operation ID is idempotent; reusing an operation ID with different content is rejected.

## Document reversal

A posted supplier document can be reversed only while its AP open item is completely unsettled. If settlement activity exists, correct or reverse the allocations/payment first. This avoids rewriting settlement history underneath an already allocated payable.

A document reversal creates a linked General Ledger reversal and voids the original AP open item. The original supplier document and journal entry remain retained evidence.

## Aging and supplier statements

The **Open Items & Aging** page groups current supplier balances by supplier and currency into current, 1-30, 31-60, 61-90 and over-90-day buckets. Unapplied debit balances are reported separately so the net supplier exposure remains visible.

Supplier statements list AP source activity for one supplier/currency/date range and show debit, credit and remaining amounts.

## Permissions

Key permissions are:

- `FinancePayables.View`
- `FinancePayables.Manage`
- `FinanceSupplierInvoices.Create`
- `FinanceSupplierInvoices.Submit`
- `FinanceSupplierInvoices.Approve`
- `FinanceSupplierMatchExceptions.Approve`
- `FinanceSupplierInvoices.Post`
- `FinanceSupplierInvoices.Reverse`
- `FinancePayablePayments.Post`
- `FinancePayablePayments.Reverse`

The service layer is the security boundary. Hiding a control in the UI is not authorization.

## Audit, concurrency, and retention

Supplier documents, AP open items and supplier payments are classified as accounting-relevant retained records. Mutating operations use optimistic version checks, database transactions and Audit Log persistence. Payment and allocation workflows use operation IDs and request hashes where retry safety is required.

## Provider support

Finance feature schema 4 contains the Accounts Payable tables and is implemented for SQLite, SQL Server, and MySQL/MariaDB. Live provider migration, concurrency, recovery and performance acceptance remain deployment/release gates; provider-neutral code does not by itself constitute production certification for every server/version combination.

## Scope boundary

F3 does not perform inventory valuation or cost-of-goods accounting. That is the responsibility of **F4 - Inventory Accounting**. F3 also does not introduce jurisdiction-specific tax determination, statutory payment formats, or country-specific invoice-compliance defaults into Finance core.
