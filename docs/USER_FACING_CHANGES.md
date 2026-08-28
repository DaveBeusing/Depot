# User-facing hardening changes

Updated: 2026-08-28

- New databases no longer use shared default administrator credentials. Depot requires creation of the initial administrator during first-run setup.
- Password policy and login throttling are enforced.
- Remote SQL Server/MySQL/MariaDB configurations require encrypted transport through supported settings.
- Administration includes Audit Log evidence export and Privacy Data discovery/export workflows.
- Administration > Company is the authoritative legal seller/document identity for the current database.
- Inventory > Items provides enriched product master data including GTIN, model/revision/product family, lifecycle dates, origin/customs/ECCN, RoHS/REACH, dangerous-goods/battery data, explicit kg/mm logistics measurements, intended tracking mode and an active replacement-item selector.
- GTIN checksum and uniqueness are validated; item replacement, physical values, dangerous-goods classification and lifecycle dates have consistency checks before saving.
- Tracking mode, item type and lifecycle status participate in physical/purchasing/sales workflow controls; serial/lot capture is enforced where tracked physical movements are posted.
- Posted/finalized business records use correction/reversal/credit workflows instead of destructive edits.
- Posted Sales Invoices freeze seller and Buyer identity and persist the exact generated XRechnung XML with SHA-256 integrity verification in the posting transaction.
- Posted invoices expose **Export XRechnung**, which exports the verified issued XML instead of regenerating it from current Company or Customer master data.
- Invoice posting fails closed when mandatory electronic-invoice identity is incomplete or when a zero-rated, exempt, or reverse-charge scenario cannot yet be represented explicitly by the commercial tax model.

## Finance F0/F1/F2

- Finance F0 provides jurisdiction-neutral legal-entity, currency/exchange-rate, fiscal-calendar/period, chart/account, accounting-book, journal-definition, dimension, tax-registration and number-sequence foundations.
- Finance F1 provides the immutable provider-neutral General Ledger posting engine, posting profiles, period/currency/dimension validation, idempotency, number allocation, and explicit reversal.
- Finance F2 raises Finance feature schema to **3** and adds the **Finance > Receivables** workspace.
- F2 explicitly ensures Sales schema **8** before AR schema migration because Accounts Receivable uses the existing Customer master and Sales Invoice/Credit Note source records.
- When AR is actively configured, posting a Sales Invoice or Credit Note also creates its configured General Ledger entry and AR open item in the same transaction. If AR/GL validation or persistence fails, the Sales posting rolls back.
- When AR is not configured, Depot does not invent accounts or legal/entity defaults; existing Sales posting continues without AR/GL records.
- Invoice open items are debit receivables. Credit notes and customer payments create credit open items.
- Customer payments support partial/full allocation and unapplied overpayments. Remaining customer credit can be allocated later to another invoice.
- Reversing a payment creates a linked GL reversal, restores every active allocation made from that payment credit — including later allocations — and voids the payment open item without deleting original evidence.
- Receivable write-offs require dedicated sensitive permissions, post through a configured profile, and can be reversed through a linked GL correction that restores the receivable balance.
- The default Finance role receives normal Receivables/payment/dunning permissions but not write-off post/reverse permissions. Free manual journals remain separately protected as well.
- Receivables aging groups current and overdue invoice balances by customer/currency and shows unapplied credits separately.
- Customer statements are built from retained AR open-item evidence.
- Dunning policies/runs are configurable and retained for audit. F2 does not claim jurisdiction-specific reminder wording, fees/interest, legal escalation, or collection compliance.
- General Ledger entries remain immutable, foreign-currency postings retain used FX evidence, and all F2 financial posting/reversal paths reuse F1 rather than maintaining a second ledger.

## Help and documentation

- Help manifest **1.11** adds **Accounts Receivable** (`finance.receivables`) guarded by `FinanceReceivables.View`.
- Finance Foundation, General Ledger, Sales Invoice, and Accounts Receivable articles are cross-linked and describe the F2 transaction/permission boundaries consistently.
- README, architecture, Finance architecture/compliance, roadmap, status, release checklist, compliance overview/matrix, and Help now identify F2 as complete and F3 Accounts Payable as next.

Accessibility and software-quality gates continue to run in CI. F2 acceptance distinguishes newly introduced regressions from test failures already present on the F1 baseline.
