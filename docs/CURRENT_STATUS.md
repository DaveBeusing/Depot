# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. Finance work packages **F0 through F6 are implemented** on branch `finance`. Remaining Finance work is F7 localization plus production/environment/legal/accessibility/provider/signing acceptance.

## Implemented Finance packages

- **F0 — International Finance Foundation:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, books, journals, dimensions, tax registrations and number sequences. Finance schema 1.
- **F1 — General Ledger & Posting Engine:** immutable balanced journals, reporting-currency snapshots, posting profiles, period/account/dimension validation, idempotency, Audit evidence and linked reversals. Finance schema 2.
- **F2 — Accounts Receivable:** customer open items, payments/allocations, write-offs, aging/statements, dunning and Sales → AR → GL integration. Finance schema 3.
- **F3 — Accounts Payable:** supplier documents/open items, three-way matching, exception approval, payments/allocations/reversals, aging/statements and AP → GL integration. Finance schema 4.
- **F4 — Inventory Accounting:** FIFO valuation, GRNI/COGS, inventory adjustments, PPV, landed cost, historical as-of valuation and Inventory ↔ GL reconciliation. Finance schema 6.
- **F5 — Banking and Payments:** bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position. Finance schema 7.
- **F6 — Financial Reporting:** trial balance, GL detail, balance sheet, P&L, cash flow, AR/AP aging, tax summary, historical inventory valuation, COGS, dimension filtering, explicit reporting mappings, deterministic CSV and immutable report snapshots. Finance schema 8.

## F6 accounting/reporting boundary

F6 does not create another ledger. GL-derived reports read persisted F1 journal entries/lines in the accounting book's **Reporting Currency**. Balance Sheet, P&L, Cash Flow, Tax Summary and COGS classification use explicit per-account reporting mappings rather than account-name heuristics.

AR/AP aging remains in each open item's transaction currency; F6 does not invent historical subledger FX. Historical inventory valuation is reconstructed from F4 valuation evidence and persisted FX snapshots. Optional dimension filters use the dimensions already persisted on F1 journal lines.

A `FinanceReportSnapshot` persists the report kind/parameters, parameter hash, content hash, canonical CSV, creator and timestamp. Snapshot creation is operation-idempotent and rejects reuse of an operation ID for different content. Snapshots are retained `AuditEvidence`; they do not make a report a statutory filing.

## Versions

- Application: **0.15.36-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **8**
- Help manifest: **1.15**

`Directory.Build.props` is authoritative for the exact application patch. Each commit increments `DepotVersionPatch`.

## Validation boundary

F6 regression evidence covers schema 8, real F1 ledger cutoff/reporting currency, explicit cash-flow classification, Finance RBAC, retained snapshot classification, snapshot idempotency/content binding and deterministic CSV export. Release Build and win-x64 publish must pass on the final head. Broad repository failures, if any, are classified separately from F6.

Provider-neutral F6 DDL exists for SQLite, SQL Server and MySQL/MariaDB. Live SQL Server/MySQL/MariaDB Finance v8 migration, provider locking/concurrency, recovery and representative reporting load tests remain production acceptance gates.

## Next Finance package

The next package is **F7 — Localization Framework**: generic reference localization, EU/German reference implementation, additional country packs and an effective-dated localization/compliance registry.
