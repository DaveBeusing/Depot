# Compliance overview

Updated: 2026-08-28

Depot separates implemented technical controls from legal, accounting, tax, audit or certification claims. Security/compliance roadmap phases retain their technically implementable controls; remaining acceptance gates are tracked in the security, release, Finance and compliance documentation.

## Finance technical baseline

Depot Finance provides explicit legal entities, currencies/exchange rates, periods, accounting books/charts/accounts, immutable balanced double-entry posting, customer and supplier subledgers, FIFO inventory valuation, Banking and Payments, configurable Financial Reporting and an effective-dated Localization framework.

These capabilities improve traceability, repeatability, reconciliation, correction history, authorization and retry safety. They do not by themselves establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, statutory retention, payment-services, audit or tax-filing conformity.

GL-derived financial reports use persisted reporting-currency values and preserve posting-time FX evidence. AR/AP Aging remains in open-item transaction currency. Cash Flow and Tax Summary require explicit account mappings. Historical Inventory Valuation uses retained valuation evidence. `FinanceReportSnapshot` is immutable `AuditEvidence` containing report parameters, canonical CSV, hashes, creator and creation time.

Localization requires explicit effective-dated assignment. The built-in `GENERIC → EU → DE` hierarchy and support-level registry distinguish software capability, deployment configuration, external procedures and reference-only information; assignment is not a compliance certification.

## Current versions

- Application: **0.15.42-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **9**
- Help manifest: **1.17**

## Remaining acceptance

Production use still requires live provider migration/concurrency/recovery/performance testing, deployment-specific accounting/reporting policy approval, reconciliation and period-end procedures, segregation-of-duties review, retention/export procedures, accessibility/signing/deployment acceptance and qualified organizational/legal/accounting validation.

This document is engineering evidence and not a certification statement.
