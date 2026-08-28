# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. Finance work packages **F0 through F3 are implemented** on branch `finance`. Security/compliance roadmap phases 1 through 7 retain their technically implementable repository/application controls; remaining items are production, environment, legal, accessibility, provider, signing, localization/accounting-policy, and enterprise acceptance gates.

## Finance F0 — International Finance Foundation

F0 established explicit legal entities, currencies/exchange rates, fiscal calendars/accounting periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences and localization extension contracts. Finance feature schema **1**.

## Finance F1 — General Ledger & Posting Engine

F1 added immutable balanced journals, transaction/reporting currency snapshots, posting profiles, operation/source idempotency, open-period/date/legal-entity enforcement, account/dimension validation, transactional Finance number allocation, linked reversals and atomic audit evidence. Finance feature schema **2**.

## Finance F2 — Accounts Receivable

F2 added the customer subledger and **Finance > Receivables**:

- Sales Invoice/Credit Note → AR → GL integration;
- customer open items;
- partial/full payments and unapplied overpayments;
- later allocations;
- payment reversals restoring allocations;
- controlled write-offs/reversal;
- aging and customer statements;
- dunning policies/runs;
- granular AR RBAC.

Finance feature schema **3**.

## Finance F3 — Accounts Payable

**F3 — Accounts Payable is implemented.** It raises Finance feature schema to **4** and adds:

- `FinanceAccountsPayableService` as the supplier-subledger business boundary;
- supplier invoice and supplier credit-note lifecycle;
- draft, submission, approval/rejection, posting and explicit reversal;
- supplier AP open items with retained source/journal linkage;
- configured AP → F1 General Ledger integration in one transaction;
- partial/full supplier payments and unapplied debit balances;
- later allocations and overpayment handling;
- supplier-payment reversal restoring all active allocations from that payment;
- AP aging and supplier statements;
- purchase-order / goods-receipt / invoice matching;
- fail-closed matching with no implicit tolerance;
- explicit match-exception approval and retained reason;
- separate supplier-document approval and match-exception permissions;
- dedicated **Finance > Payables** workspace;
- F3 regression coverage for schema, AP→GL, matching, settlement reversal, RBAC and retained evidence;
- Help manifest **1.12** topic `finance.payables`.

The default Finance role receives normal AP creation/submission/posting/reversal/payment/configuration rights but not supplier-document approval or match-exception approval. Deployment role design remains responsible for assigning incompatible rights to appropriately separated users.

The generic Finance core still contains no implicit Germany, EUR, VAT rate, SKR03/SKR04, HGB, IFRS, US-GAAP, inbound e-invoice, bank account, AP account, expense account or matching-tolerance default.

## Versions

- Application branch line: **0.15.x-preview**
- Current F3 implementation before documentation commit: **0.15.13-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **4**
- Help manifest: **1.12**

The documentation commit itself increments `DepotVersionPatch`; use `Directory.Build.props` as the authoritative exact application patch after that commit.

## Validation boundary

The F3 test commit passes the Release solution build and win-x64 single-file publish at the build stage. F3 regression groups are used to distinguish newly introduced AP failures from repository failures already present before F3.

Provider-neutral schema/code exists for SQLite, SQL Server and MySQL/MariaDB. Live SQL Server/MySQL/MariaDB Finance v4 migration, locking, concurrency, rollback, recovery and representative performance testing remain production acceptance gates.

Current electronic-invoice boundaries remain explicit: Sales XRechnung is separate from generic Finance; F3 does not claim inbound supplier e-invoice compliance or jurisdiction-specific tax determination.

## Next Finance package

The next package is **F4 — Inventory Accounting**, including inventory valuation/accounting consequences and controlled integration into the existing General Ledger boundary. F3 does not pre-implement valuation or COGS logic.

Phase 8 enterprise readiness remains planned.
