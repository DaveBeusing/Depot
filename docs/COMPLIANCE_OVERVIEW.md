# Compliance overview

Updated: 2026-08-28

Depot separates implemented technical controls from legal, accounting, tax, audit or certification claims. Security/compliance roadmap phases retain their technically implementable controls; remaining acceptance gates are tracked in the security, release, Finance and compliance documentation.

## Finance F0-F6 technical baseline

- **F0** provides explicit legal entities, currencies/exchange rates, periods, books/charts/accounts, tax registrations, dimensions, number sequences and localization extension boundaries with no seeded jurisdiction/accounting defaults.
- **F1** provides immutable balanced double entry, historical FX evidence, period/account/dimension validation, idempotency, transactional number allocation, explicit reversal and atomic Audit evidence.
- **F2** provides the customer subledger with Sales→AR→GL, payments/allocations, controlled write-offs, aging/statements and dunning.
- **F3** provides the supplier subledger with document lifecycle, three-way matching, explicit match exceptions, payments/allocations/reversals, aging/statements and segregation of duties.
- **F4** provides FIFO Inventory Accounting, GRNI/COGS, inventory adjustments, PPV, landed cost, historical valuation and Inventory↔GL reconciliation.
- **F5** provides bank-account configuration, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position.
- **F6** provides configurable financial reports, explicit report classification mappings, deterministic CSV and immutable SHA-256-bound report snapshots.

These are engineering controls that improve traceability, repeatability, reconciliation capability, correction history, authorization and retry safety. They do not by themselves establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, statutory retention, payment-services, audit or tax-filing conformity.

## F6 control boundary

GL-derived financial reports use persisted F1 reporting-currency values and therefore preserve posting-time FX evidence. AR/AP Aging remains in open-item transaction currency. Cash Flow and Tax Summary require explicit account mappings; F6 does not infer accounting meaning from account names or numbers. Historical Inventory Valuation uses F4 evidence.

`FinanceReportSnapshot` is immutable AuditEvidence containing report parameters, canonical CSV, SHA-256 parameter/content hashes, creator and creation time. A snapshot is technical evidence, not statutory filing certification.

## Current versions

- Application: **0.15.35-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **8**
- Help manifest: **1.15**

## Remaining acceptance

Production use still requires live provider migration/concurrency/recovery/performance testing, deployment-specific accounting/reporting policy approval, reconciliation and period-end procedures, segregation-of-duties review, retention/export procedures, accessibility/signing/deployment acceptance and qualified organizational/legal/accounting validation.

F7 Localization Framework remains the next Finance package and is responsible for country/statutory extension infrastructure.

This document is engineering evidence and not a certification statement.
