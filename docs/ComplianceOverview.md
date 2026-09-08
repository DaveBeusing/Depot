# Compliance overview

Updated: 2026-09-08

Depot separates implemented technical controls from legal, accounting, tax, audit or certification claims. Security/compliance roadmap phases retain their technically implementable controls; remaining acceptance gates are tracked in the security, release, Finance and compliance documentation.

## Finance technical baseline

Depot Finance provides explicit legal entities, currencies/exchange rates, periods, accounting books/charts/accounts, immutable balanced double-entry posting, customer and supplier subledgers, FIFO inventory valuation, Banking and Payments, configurable Financial Reporting and an effective-dated Localization framework.

These capabilities improve traceability, repeatability, reconciliation, correction history, authorization and retry safety. They do not by themselves establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, statutory retention, payment-services, audit or tax-filing conformity.

GL-derived financial reports use persisted reporting-currency values and preserve posting-time FX evidence. AR/AP Aging remains in open-item transaction currency. Cash Flow and Tax Summary require explicit account mappings. Historical Inventory Valuation uses retained valuation evidence. `FinanceReportSnapshot` is immutable `AuditEvidence` containing report parameters, canonical CSV, hashes, creator and creation time.

Localization requires explicit effective-dated assignment. The built-in `GENERIC → EU → DE` hierarchy and support-level registry distinguish software capability, deployment configuration, external procedures and reference-only information; assignment is not a compliance certification.

## Current versions

- Application: **0.15.169-preview**
- Core database schema: **30**
- Sales feature schema: **11**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- Help manifest: **1.21**

## Database-provider acceptance

The database-provider technical gate is complete for the exact baselines documented in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md): Depot's bundled SQLite runtime, SQL Server 2022 engine 16.x, MariaDB 11.8.9 LTS and MySQL 8.4.11 LTS.

The real-provider matrix covers provisioning/migration, locking/deadlock/retry, rollback, constraints, date/decimal/timestamp handling, representative Finance/Sales/Procurement/session workflows, Banking/reconciliation, Financial Reporting/snapshots, remote restart and provider-native backup/restore boundaries plus representative 100k indexed access.

Database-provider certification is an engineering/runtime statement. It does not establish jurisdiction-specific accounting, tax, legal, accessibility, operating-system, banking-network or regulatory certification.

## Remaining acceptance

Production use still requires deployment-specific accounting/reporting policy approval, reconciliation and period-end procedures, segregation-of-duties review, retention/export/restore procedures, realistic customer-specific sizing, accessibility/signing/deployment acceptance and qualified organizational/legal/accounting validation.

This document is engineering evidence and not a certification statement.
