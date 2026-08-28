# Finance Foundation

Depot 0.15 introduces the provider-neutral foundation for future accounting workflows. This foundation is intentionally jurisdiction-neutral: it does not assume Germany, EUR, a 19% tax rate, SKR03/SKR04, IFRS, or any other local accounting configuration.

## What F0 provides

The Finance foundation defines the data contracts and database structures for:

- legal entities and their functional currencies;
- currencies using three-letter ISO 4217 syntax;
- exchange rates with effective timestamps and explicit source codes;
- fiscal calendars and accounting periods;
- charts of accounts and accounts;
- accounting books for parallel reporting/accounting bases;
- journal definitions;
- accounting dimensions and dimension values;
- tax registrations;
- finance number sequences;
- exchange-rate, tax-determination, and localization provider interfaces.

The database initializes these structures automatically for SQLite, SQL Server, and MySQL/MariaDB through Finance feature schema version 1.

## Permissions

Finance configuration is protected by dedicated permissions. `Finance.View` controls visibility of this Help topic and the generic Finance read boundary. More granular permissions cover exchange rates, periods, accounting books, tax configuration, and number sequences. The protected Administrator role receives all catalogued permissions; the Finance system role receives the Finance foundation permissions.

## Important boundary

F0 does **not** yet post general-ledger transactions. It does not create debit/credit journal entries, close periods, calculate statutory tax, or produce statutory financial statements. Existing Sales Invoice and XRechnung behavior remains separate from the generic Finance foundation.

The next Finance package (F1) adds the General Ledger and Posting Engine with balanced double-entry journals, posting profiles, idempotency, period locking, reversals, and audit integration.

See also: [Sales Invoices and Credit Notes](topic:sales.invoices) and [Company Master Data](topic:administration.company).
