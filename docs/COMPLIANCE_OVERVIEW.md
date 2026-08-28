# Compliance overview

Updated: 2026-08-28

Depot separates implemented technical controls from legal, accounting, tax, audit or certification claims. Security/compliance roadmap phases 1-7 retain their technically implementable controls; remaining acceptance gates are tracked in the security, release, Finance and compliance documentation.

## Finance F0-F3 technical baseline

F0 provides explicit legal entities, currencies/exchange rates, periods, books/charts/accounts, tax registrations, dimensions, number sequences and localization/tax/exchange-rate extension boundaries with no seeded jurisdiction/accounting defaults.

F1 provides immutable balanced double entry, historical FX evidence, period/date/legal-entity/account/dimension validation, source/operation idempotency, transactional number allocation, explicit reversal, atomic Audit evidence and separate free-manual-journal authorization.

F2 adds the customer subledger: retained AR open items, Sales→AR→GL atomicity, payments/allocations/overpayments, reversal, controlled write-offs, aging/statements and dunning evidence.

F3 raises Finance feature schema to **4** and adds the supplier subledger:

- retained supplier invoices/credit notes, AP open items and supplier payments linked to GL evidence;
- explicit draft/submission/approval/post/reversal lifecycle;
- PO/goods-receipt/invoice matching based on supplier, PO price, non-reversed received quantity and previously invoiced quantity;
- fail-closed matching with no implicit generic tolerance;
- separate match-exception approval permission and retained reason;
- partial/full supplier-payment allocation and unapplied debit balances;
- supplier-payment reversal restoring active allocations and creating linked GL reversal;
- AP aging and supplier-statement projections;
- service-layer segregation between operational AP rights and supplier-document/match-exception approval.

These are engineering controls that improve traceability, repeatability, subledger/GL reconciliation capability, correction history, authorization and retry safety. They do not by themselves establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, statutory retention, inbound e-invoice, payment-services, audit or tax-filing conformity.

## Current versions

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **4**
- Help manifest: **1.12**

## Remaining acceptance

Production use still requires live provider migration/concurrency/recovery testing, deployment-specific chart/book/calendar/posting-profile approval, AR/AP-to-GL reconciliation procedures, AP approval/exception segregation-of-duties review, payment evidence/reconciliation, retention/export procedures, localization/accounting/tax review, signing/deployment acceptance and qualified organizational/legal/accounting validation.

F3 does not include inventory valuation/COGS/GRNI; that remains F4 Inventory Accounting.

This document is engineering evidence and not a certification statement.
