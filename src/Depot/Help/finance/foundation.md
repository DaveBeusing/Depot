# Finance Foundation

Depot 0.15 provides a provider-neutral foundation for accounting workflows. The Finance core is intentionally jurisdiction-neutral: it does not assume Germany, EUR, a 19% tax rate, SKR03/SKR04, IFRS, or another local accounting configuration.

## Foundation scope

The Finance foundation defines and persists:

- legal entities and functional currencies;
- currencies using three-letter ISO 4217 syntax and explicit minor units;
- exchange rates with effective timestamps and source codes;
- fiscal calendars and accounting periods;
- charts of accounts and accounts;
- accounting books for parallel reporting/accounting bases;
- journal definitions;
- accounting dimensions and dimension values;
- structured tax registrations;
- Finance number sequences;
- exchange-rate, tax-determination, and localization provider interfaces.

Finance feature schema 1 established these structures for SQLite, SQL Server, and MySQL/MariaDB. F1 extends the Finance feature schema to version 2 with the General Ledger and Posting Engine.

## General Ledger

F1 adds immutable balanced journal entries, posting profiles, transaction/reporting currency snapshots, period-lock enforcement, operation and source-document idempotency, explicit reversals, and atomic audit persistence.

See [General Ledger and Posting](topic:finance.general-ledger) for the posting rules and permissions.

## Permissions

`Finance.View` controls the generic Finance read boundary. More granular permissions cover exchange rates, periods, accounting books, tax configuration, number sequences, General Ledger posting, reversals, posting profiles, and manual journals.

The protected Administrator role receives every catalogued permission. The Finance system role receives controlled General Ledger and posting-profile rights, but the sensitive `FinanceManualJournals.Post` permission is deliberately not assigned to that role automatically.

## Important boundary

F1 is the accounting posting engine, not yet Accounts Receivable, Accounts Payable, inventory valuation, banking, statutory filing, or a dedicated Finance workspace. Existing Sales Invoice/XRechnung behavior remains separate until later Finance packages connect source workflows to the posting engine.

The next Finance package is F2 — Accounts Receivable.

See also: [Sales Invoices and Credit Notes](topic:sales.invoices) and [Company Master Data](topic:administration.company).
