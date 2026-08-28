# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. Finance work packages **F0 through F5 are implemented** on branch `finance`. Remaining items are production, environment, legal, accessibility, provider, signing, localization/accounting-policy, and enterprise acceptance gates.

## Finance F0 — International Finance Foundation
F0 established explicit legal entities, currencies/exchange rates, fiscal calendars/accounting periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences and localization extension contracts. Finance feature schema **1**.

## Finance F1 — General Ledger & Posting Engine
F1 added immutable balanced journals, transaction/reporting currency snapshots, posting profiles, operation/source idempotency, open-period/date/legal-entity enforcement, account/dimension validation, transactional Finance number allocation, linked reversals and atomic audit evidence. Finance feature schema **2**.

## Finance F2 — Accounts Receivable
F2 added the customer subledger and **Finance > Receivables** with Sales source integration, customer open items, payments/allocations, controlled write-offs, aging/statements, dunning and granular RBAC. Finance feature schema **3**.

## Finance F3 — Accounts Payable
F3 added the supplier subledger and **Finance > Payables** with supplier-document lifecycle, AP open items, three-way matching, explicit exception approval, payments/allocations/reversals, aging/statements and configured F1 GL integration. Finance feature schema **4**.

## Finance F4 — Inventory Accounting
F4 added provider-neutral FIFO valuation, Goods Receipt inventory/GRNI posting, Sales Shipment FIFO/COGS posting, controlled valuation reversals, inventory-count valuation, purchase-price variance, landed-cost allocation, historical as-of valuation and inventory-to-GL reconciliation. Finance feature schema **6**.

## Finance F5 — Banking and Payments

**F5 — Banking and Payments is implemented.** Finance feature schema is **7** and the package adds:

- bank-account master/configuration tied to legal entity, accounting book, active direct-posting GL account and explicit currency;
- immutable bank statements and normalized statement lines;
- CSV and ISO 20022 `camt.053` statement import;
- operation/content idempotency and exact opening/transaction/closing balance validation;
- bank-line reconciliation against F2 AR payment, F3 AP payment or F1 GL bank-account evidence;
- explicit reconciliation reversal preserving original evidence;
- supplier-payment proposals from AP open items;
- creator/approver segregation for payment runs;
- idempotent payment-run execution through the existing F3 Accounts Payable service;
- cash-position comparison of latest statement closing balance versus bank GL balance and unreconciled-line count;
- dedicated **Finance > Banking** workspace;
- granular Banking/Statement/Reconciliation/Payment/Cash Position RBAC and retained-record classifications;
- provider-neutral schema DDL for SQLite, SQL Server and MySQL/MariaDB.

F5 statement import is not direct bank connectivity. Depot does not claim EBICS, PSD2/open-banking conformance, payment initiation certification, sanctions/AML/KYC decisioning or bank-specific ISO 20022 profile certification.

## Versions

- Application: **0.15.28-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **7**
- Help manifest: **1.14**

`Directory.Build.props` is authoritative for the exact application patch. Each commit increments `DepotVersionPatch`.

## Validation boundary

F5-specific regression evidence covers Finance schema 7, CSV and `camt.053` parsing, cross-currency fail-closed behavior, Banking RBAC/segregation and retained accounting evidence. Release Build, win-x64 publish and Release Integrity are required on the final head; broad repository test failures are classified against the pre-existing baseline.

Provider-neutral schema/code exists for SQLite, SQL Server and MySQL/MariaDB. Live SQL Server/MySQL/MariaDB Finance v7 migration, locking, concurrency, rollback, recovery and representative statement/payment/reconciliation load testing remain production acceptance gates.

## Next Finance package

The next package is **F6 — Financial Reporting**: trial balance, GL detail, balance sheet, profit/loss, cash-flow, subledger aging, tax/inventory summaries, dimension-aware reporting and exports.

After F6, **F7 — Localization Framework** remains planned. Phase 8 enterprise readiness remains planned.
