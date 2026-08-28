# User-facing changes

Updated: 2026-08-28

Depot's current `0.15.x-preview` line includes the previously implemented authentication/RBAC, inventory/traceability, business-record integrity, seller/buyer invoice identity and Finance F0-F2 controls plus the completed **Finance F3 — Accounts Payable** package.

## Finance F3 — Accounts Payable

- Finance now contains two permission-aware pages: **Receivables** and **Payables**.
- **Finance > Payables** allows users with the corresponding permissions to work with supplier documents, AP open items, aging, supplier statements, supplier payments/allocations and AP configuration.
- Supplier invoices and supplier credit notes follow explicit draft → submit → approve/reject → post → reverse states.
- Draft supplier documents support multiple lines and optional Purchase Order / Goods Receipt references.
- PO-linked invoice lines are checked against supplier, ordered unit price, posted/non-reversed receipt quantity and already invoiced quantity.
- Matching is fail-closed: Depot does not silently apply generic percentage, quantity or price tolerances.
- A mismatch becomes a **Match Exception**. Approving it requires the separate `FinanceSupplierMatchExceptions.Approve` permission and a reason that is retained as evidence.
- Non-PO invoices remain supported; Depot does not invent a purchase-order relationship.
- Posting an approved supplier document creates the configured General Ledger posting and AP open item in one transaction.
- Supplier invoices create credit-direction AP balances; supplier credit notes create debit-direction balances.
- Supplier payments support partial/full allocation, overpayment/unapplied debit balances and later allocation.
- Reversing a supplier payment restores every active allocation from that payment, creates the linked General Ledger reversal and retains the original payment/allocation evidence.
- A posted supplier document can be reversed only while its AP open item remains completely unsettled; settlement corrections must occur first.
- AP aging shows due-date buckets by supplier/currency and keeps unapplied debits visible separately.
- Supplier statements are derived from retained AP evidence.

## Permissions and segregation of duties

F3 adds dedicated permissions for AP view/manage, supplier-document create/submit/approve/post/reverse, match-exception approve, and supplier-payment post/reverse.

The default Finance role receives normal AP operational rights but **does not automatically receive supplier-document approval or match-exception approval**. Service-layer authorization remains authoritative regardless of UI visibility.

## Help and documentation

- Help manifest **1.12** adds **Accounts Payable** (`finance.payables`) guarded by `FinancePayables.View`.
- Finance Foundation, General Ledger, Accounts Receivable, Purchasing, Goods Receipts and Audit Help are cross-linked with the AP topic where relevant.
- Central architecture/compliance/status/roadmap/release documentation now identifies F0-F3 as implemented and F4 Inventory Accounting as next.

## Scope limits

F3 does not implement inventory valuation, COGS, GRNI, landed cost, bank payment files, statutory inbound e-invoice validation, or jurisdiction-specific tax determination. Those remain separate future packages/acceptance work.

Provider-neutral Finance schema **4** exists for SQLite, SQL Server and MySQL/MariaDB; live server migration/concurrency/recovery/performance acceptance remains required before production support claims.
