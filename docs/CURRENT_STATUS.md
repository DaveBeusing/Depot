# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. Finance work packages **F0 through F4 are implemented** on branch `finance`. Security/compliance roadmap phases 1 through 7 retain their technically implementable repository/application controls; remaining items are production, environment, legal, accessibility, provider, signing, localization/accounting-policy, and enterprise acceptance gates.

## Finance F0 — International Finance Foundation

F0 established explicit legal entities, currencies/exchange rates, fiscal calendars/accounting periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences and localization extension contracts. Finance feature schema **1**.

## Finance F1 — General Ledger & Posting Engine

F1 added immutable balanced journals, transaction/reporting currency snapshots, posting profiles, operation/source idempotency, open-period/date/legal-entity enforcement, account/dimension validation, transactional Finance number allocation, linked reversals and atomic audit evidence. Finance feature schema **2**.

## Finance F2 — Accounts Receivable

F2 added the customer subledger and **Finance > Receivables** with Sales source integration, customer open items, payments/allocations, controlled write-offs, aging/statements, dunning and granular RBAC. Finance feature schema **3**.

## Finance F3 — Accounts Payable

F3 added the supplier subledger and **Finance > Payables** with supplier-document lifecycle, AP open items, three-way matching, explicit exception approval, payments/allocations/reversals, aging/statements and configured F1 GL integration. Finance feature schema **4**.

## Finance F4 — Inventory Accounting

**F4 — Inventory Accounting is implemented.** Finance feature schema is **6** and the package adds:

- provider-neutral FIFO valuation layers and valuation-consumption evidence;
- atomic Goods Receipt → valuation layer → inventory/GRNI GL posting when Inventory Accounting is active;
- atomic Sales Shipment → FIFO consumption → COGS/inventory GL posting when Inventory Accounting is active;
- linked receipt/shipment reversal behavior that restores valuation state or fails closed when downstream valuation has already consumed a receipt layer;
- inventory-count correction valuation with explicit positive/negative adjustment posting and linked reversal support;
- controlled idempotent inventory-count catch-up by immutable count/movement reference;
- purchase-price variance calculation from posted supplier documents against referenced PO quantities/prices, plus explicit GL reversal;
- landed-cost allocation to fully unconsumed layers by quantity or existing value, with explicit reversal before downstream consumption;
- historical **as-of** valuation reconstruction that respects later consumption and later reversal timing instead of using current-state balances for prior dates;
- period-end reconciliation between inventory valuation and the configured inventory-control General Ledger account in reporting currency;
- immutable reconciliation runs and per-item snapshot lines;
- dedicated **Finance > Inventory Accounting** workspace;
- Finance Inventory Accounting View/Manage RBAC and retained-record classifications;
- provider-neutral schema DDL for SQLite, SQL Server and MySQL/MariaDB.

FIFO is the only implemented valuation method in F4. Depot does not silently substitute weighted-average, standard-cost, LIFO or jurisdiction-specific valuation policy.

## Versions

- Application branch line: **0.15.x-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **6**
- Help manifest after F4 documentation: **1.13**

`Directory.Build.props` is authoritative for the exact application patch. Each commit increments `DepotVersionPatch`.

## Validation boundary

F4-specific regression evidence covers Finance schema 6, F4 RBAC, retained accounting/audit evidence and historical as-of valuation reconstruction. CI also builds all broad test groups so compiler/integration regressions can be separated from repository failures that already existed before F4.

Provider-neutral schema/code exists for SQLite, SQL Server and MySQL/MariaDB. Live SQL Server/MySQL/MariaDB Finance v6 migration, locking, concurrency, rollback, recovery and representative performance testing remain production acceptance gates.

Current electronic-invoice boundaries remain explicit: Sales XRechnung is separate from generic Finance; F4 does not add country-specific tax, statutory inventory-valuation or inbound supplier e-invoice compliance.

## Next Finance package

The next package is **F5 — Banking and Payments**: bank-account and statement models, CSV/ISO 20022 import, payment proposal/execution abstractions, bank reconciliation, cash position and controlled linkage of AR/AP settlements to bank evidence.

After F5, the planned Finance packages are **F6 — Financial Reporting** and **F7 — Localization Framework**.

Phase 8 enterprise readiness remains planned.
