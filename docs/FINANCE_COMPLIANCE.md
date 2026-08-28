# Finance compliance boundary

Updated: 2026-08-28

Finance F0/F1/F2 is an engineering foundation and accounting-control implementation, not an accounting, tax, audit, statutory, collections, or software certification. It provides technical controls that later jurisdiction-specific implementations can use and that operators can include in their own control environment.

## Implemented technical controls

F0 provides explicit legal-entity/currency/period/book/chart/tax-registration/dimension/number-sequence foundations, localization/tax/exchange-rate extension boundaries, dedicated Finance RBAC, provider-neutral feature versioning, and no seeded jurisdiction/accounting defaults.

F1 adds balanced immutable double entry, explicit reversals, open-period/date/legal-entity enforcement, operation/source idempotency, atomic number allocation and Audit Log evidence, historical currency/FX snapshots, account/chart/dimension validation, posting-profile concurrency, and separate manual-journal authorization.

F2 additionally provides:

- customer receivable open-item evidence with immutable source identity/original amount and controlled remaining balance;
- transactionally coupled Sales Invoice/Credit Note → AR → GL processing when AR is configured;
- explicit customer-payment records and allocation evidence;
- partial settlement and unapplied-credit treatment without destructive source-document mutation;
- payment reversal that restores every active allocation from the payment while preserving original payment/allocation evidence;
- controlled write-off records with dedicated authorization and linked GL reversal;
- aged receivable and customer-statement projections from retained subledger evidence;
- configurable dunning policies and persisted dunning-run evidence;
- operation/request idempotency for payments, allocation operations, and dunning runs;
- optimistic/race-safety boundaries for settlement and corrections;
- service-layer segregation between normal Finance Receivables operations and sensitive write-off authority;
- provider-neutral Finance feature schema version **3**.

`FinanceReceivableOpenItem`, `FinanceReceivablePayment`, and `FinanceReceivableWriteOff` are classified as accounting-relevant retained business records. `FinanceDunningRun` is classified as retained audit evidence. Original source Sales documents and GL journals are not rewritten by AR settlement/correction workflows.

## Compliance relevance

These controls are relevant building blocks for accounting-record integrity, subledger/GL traceability, segregation of duties, change evidence, receivable reconciliation, repeatable processing, and correction history. They support future jurisdiction-specific GoBD/HGB, IFRS/GAAP, VAT/GST/sales-tax, audit-export, SAF-T, DATEV, XBRL, collections/dunning, and filing work, but do not by themselves establish conformity with any regime.

GoBD-relevant engineering characteristics such as traceability, immutability/correction history, authorization, audit evidence, reproducible processing, and subledger linkage are technical controls only. They do not replace organization-specific procedures, retention/export rules, legal/accounting review, or deployment acceptance.

## Accounts Receivable compliance boundary

F2 does **not** claim that Depot's dunning feature constitutes a legally compliant reminder/collections process for any country. F2 stores configured overdue levels and run evidence; it does not determine statutory reminder wording, default interest, reminder fees, notice periods, service/delivery proof, limitation periods, insolvency treatment, debt-collection licensing, court procedures, or consumer-protection obligations.

F2 write-offs are controlled accounting corrections, not a tax-law determination that a receivable is legally/tax-deductibly irrecoverable. Deployment rules must define who may authorize a write-off, what evidence is required, thresholds/approvals, tax consequences, and reporting/export requirements.

Aging and customer statements are operational/accounting projections from the retained AR subledger. Their existence does not establish statutory financial-statement presentation, confirmation-of-balances procedures, audit confirmation, or legal account-statement requirements.

Customer payment posting in F2 is an accounting/subledger record. F2 is **not** a banking/payment-execution package and does not claim PSD2/Open Banking, payment-service-provider, card/PCI, ISO 20022 bank statement, cash application, or bank reconciliation functionality. Those concerns belong to later banking integration.

## Electronic invoicing separation

Existing XRechnung/EN 16931 functionality remains a separate electronic-invoicing capability. F2 consumes finalized Sales invoice/credit-note monetary values and source identity; it does not treat XRechnung as a generic tax-determination or accounting-compliance engine.

Special tax semantics, electronic credit-note Buyer/XML finalization, recipient routing, and full production-profile validation remain separate implementation/acceptance work.

## What F2 does not claim

F0/F1/F2 do not demonstrate conformity with HGB, GoBD, IFRS, US GAAP, VAT/GST/sales-tax law, SAF-T, DATEV, XBRL, statutory retention, statutory account plans, statutory receivable valuation/impairment, tax filing, legal dunning/collections, banking regulation, or audit standards.

ISO-style country/currency syntax validation is a structural guard only. Production reference-data governance must define valid codes, currencies, exchange-rate sources, charts/accounts, posting profiles, tax registrations, accounting standards, effective dates, AR configuration, dunning policies, write-off procedures, and approval roles.

## Remaining assurance work

Before Finance can be represented as production accounting evidence for a particular organization/jurisdiction, acceptance must additionally cover:

- live SQL Server/MySQL/MariaDB Finance v1→v3 migration, locking, deadlock/retry, recovery, and load tests;
- role design and segregation-of-duties approval, including manual journals and write-offs;
- chart/account/posting-profile approval;
- legal-entity/fiscal-calendar/period-close and privileged reopen procedures;
- exchange-rate source/effective-date governance;
- AR subledger-to-GL reconciliation procedures and exception handling;
- customer payment evidence/import/reconciliation procedure until Banking is implemented;
- write-off policy, evidence, approval thresholds, and tax treatment;
- dunning/collections policy, wording, fees/interest, delivery proof, escalation, and applicable legal review;
- backup, retention, restore, and export procedures for accounting/AR records;
- statutory/localization rules, reports, exports, and filing interfaces;
- documented operator procedures and qualified legal/accounting review.

The next Finance package is F3 Accounts Payable and must preserve the same immutable/idempotent posting boundary while adding supplier subledger truth and source/matching controls.
