# Finance Foundation

Depot 0.15 provides a provider-neutral foundation for accounting workflows. The Finance core is intentionally jurisdiction-neutral: it does not assume Germany, EUR, a 19% tax rate, SKR03/SKR04, IFRS, HGB, US-GAAP, XRechnung, or another local accounting configuration.

## What is implemented

The Finance foundation defines and persists:

- legal entities and functional currencies;
- currencies using three-letter ISO 4217-style syntax and explicit minor units;
- exchange rates with effective timestamps and source codes;
- fiscal calendars and accounting periods;
- charts of accounts and accounts;
- accounting books for configured reporting/accounting bases;
- journal definitions;
- accounting dimensions and dimension values;
- structured tax registrations;
- Finance number sequences;
- exchange-rate, tax-determination, and localization provider interfaces.

Finance feature schema **1** established those structures for SQLite, SQL Server, and MySQL/MariaDB.

Finance F1 extends the feature schema to **2** and adds the General Ledger & Posting Engine. See [General Ledger and Posting](topic:finance.general-ledger).

## General Ledger boundary

F1 adds immutable balanced journal entries, posting profiles, transaction/reporting currency and exchange-rate snapshots, open-period enforcement, operation/source-document idempotency, Finance number allocation, explicit reversals, and atomic Audit Log persistence.

This is the accounting engine boundary. It does **not** mean that every Sales, Purchasing, or Inventory transaction already creates a GL entry. Those source integrations are added only with their complete Finance packages so Depot does not expose partial accounting behavior.

## Permissions

`Finance.View` controls the generic Finance read boundary. More granular permissions cover exchange rates, periods, accounting books, tax configuration, number sequences, General Ledger activity, posting profiles, and manual journals.

The protected Administrator role receives all catalogued permissions through normal RBAC. The Finance system role receives controlled General Ledger view/post/reversal and posting-profile permissions, but the sensitive `FinanceManualJournals.Post` permission is deliberately **not** assigned automatically.

General Ledger Help itself requires `FinanceGeneralLedger.View`; having only `Finance.View` does not grant access to GL operations.

## No jurisdiction defaults

Depot does not seed a legal entity, chart, book, tax rate, accounting standard, or currency for Finance. Those choices are deployment/localization data and must be explicit.

Country/currency validation in the generic core is structural syntax validation. Whether a code, tax registration, chart, rate source, or accounting configuration is legally/operationally valid for a deployment remains a reference-data/localization/accounting responsibility.

## Current package boundary

Implemented:

- F0 International Finance Foundation
- F1 General Ledger & Posting Engine

Not yet implemented as complete Finance packages:

- F2 Accounts Receivable and Sales Invoice/Credit Note GL integration
- F3 Accounts Payable
- F4 Inventory Accounting
- F5 Banking/payments/reconciliation
- F6 financial reporting
- F7 localization/statutory packages

The next Finance package is **F2 — Accounts Receivable**.

See also: [General Ledger and Posting](topic:finance.general-ledger), [Sales Invoices and Credit Notes](topic:sales.invoices), [Company Master Data](topic:administration.company), and [Audit Log](topic:administration.audit-log).
