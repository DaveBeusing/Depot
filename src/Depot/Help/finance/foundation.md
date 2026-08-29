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

Finance F1 extends the feature schema to **2** and adds the [General Ledger and Posting](topic:finance.general-ledger) engine. Finance F2 extends the feature schema to **3** and adds [Accounts Receivable](topic:finance.receivables).

## General Ledger boundary

F1 adds immutable balanced journal entries, posting profiles, transaction/reporting currency and exchange-rate snapshots, open-period enforcement, operation/source-document idempotency, Finance number allocation, explicit reversals, and atomic Audit Log persistence.

`FinanceGeneralLedgerService` remains the accounting posting authority. F2 does not create a parallel ledger; its customer-subledger operations call the F1 boundary inside the same database transaction.

## Accounts Receivable boundary

F2 adds:

- the Finance > Receivables workspace;
- customer receivable open items;
- Sales Invoice and Sales Credit Note source integration;
- customer payments with partial/full allocation and unapplied overpayments;
- later credit allocation;
- explicit payment reversals;
- controlled write-offs and write-off reversals;
- aged receivables and customer statements;
- configurable dunning policies and retained dunning runs.

When an active AR configuration exists, Sales Invoice/Credit Note posting connects Sales, the AR subledger, the F1 General Ledger, Finance number allocation, and Audit Log evidence in one database transaction. If AR has not been configured, Depot does not invent accounting defaults and the existing Sales workflow remains operational without AR/GL posting.

## Permissions

`Finance.View` controls the generic Finance read boundary. More granular permissions cover exchange rates, periods, accounting books, tax configuration, number sequences, General Ledger activity, posting profiles, manual journals, receivables, customer payments, write-offs, and dunning.

The protected Administrator role receives all catalogued permissions through normal RBAC. The Finance system role receives controlled General Ledger and Accounts Receivable operational rights. Sensitive `FinanceManualJournals.Post` and Accounts Receivable write-off post/reverse permissions are deliberately **not** assigned automatically.

General Ledger Help requires `FinanceGeneralLedger.View`. Accounts Receivable Help requires `FinanceReceivables.View`.

## No jurisdiction defaults

Depot does not seed a legal entity, chart, book, tax rate, accounting standard, currency, bank account, write-off account, or dunning/legal process for Finance. Those choices are deployment/localization data and must be explicit.

Country/currency validation in the generic core is structural syntax validation. Whether a code, tax registration, chart, rate source, account, posting profile, dunning policy, or accounting configuration is legally/operationally valid for a deployment remains a reference-data/localization/accounting responsibility.

## Current package boundary

Implemented:

- F0 — International Finance Foundation
- F1 — General Ledger & Posting Engine
- F2 — Accounts Receivable

Not yet implemented as complete Finance packages:

- F3 — Accounts Payable
- F4 — Inventory Accounting
- F5 — Banking/payments/reconciliation
- F6 — financial reporting
- F7 — localization/statutory packages

The next Finance package is **F3 — Accounts Payable**.

See also: [General Ledger and Posting](topic:finance.general-ledger), [Accounts Receivable](topic:finance.receivables), [Sales Invoices and Credit Notes](topic:sales.invoices), [Company Master Data](topic:administration.company), and [Audit Log](topic:administration.audit-log).
