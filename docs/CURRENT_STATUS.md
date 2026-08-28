# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. The completed Finance F2 documentation baseline is **0.15.6-preview**. Security/compliance roadmap phases 1 through 7 have their technically implementable repository/application controls in place. Remaining items are tracked as production, environment, legal, accessibility, provider, signing, tax-profile/routing, Finance-localization, accounting-operation, or enterprise acceptance gates.

Key current capabilities include first-run administrator bootstrap, hardened authentication/RBAC, privacy discovery/export, immutable business-record evidence, CRA technical evidence, Company legal-identity master data, enriched Item master data, serial/lot traceability and lifecycle enforcement, immutable seller snapshots, atomic Sales Invoice Buyer/XRechnung finalization with exact XML retention/SHA-256 verification, posted-invoice XRechnung export, pinned KoSIT representative validation, software-quality/accessibility gates, SBOM/dependency audit, release-integrity automation, and Finance F0/F1/F2.

## Finance F0

F0 — International Finance Foundation established explicit legal entities, currencies/exchange rates, fiscal calendars/accounting periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences, exchange-rate/tax/localization extension contracts, granular Finance RBAC, and provider-neutral Finance feature schema **1**.

## Finance F1

F1 — General Ledger & Posting Engine adds immutable balanced journal entries, transaction/reporting currency and FX snapshots, posting profiles, operation/source idempotency, period/date/legal-entity enforcement, account/chart/dimension validation, transactional Finance number allocation, linked reversals, atomic Audit Log evidence, and separate free-manual-journal authorization. Finance feature schema **2** introduced the GL persistence layer.

## Finance F2

**F2 — Accounts Receivable is complete.** It raises Finance feature schema to **3** and adds:

- explicit F2 dependency on Sales feature schema **8** because AR consumes Customers and Sales Invoice/Credit Note sources;
- `FinanceAccountsReceivableService` as the customer-subledger business boundary;
- receivable debit/credit open items with retained source and journal linkage;
- atomic configured Sales Invoice/Credit Note → AR → F1 GL integration;
- partial/full customer-payment allocation and unapplied overpayments;
- later customer-credit allocation;
- explicit payment reversal restoring all active allocations from the payment credit;
- controlled write-offs and linked GL-backed reversal;
- aging and customer statement projections;
- configurable dunning policies and idempotent retained dunning runs;
- dedicated Finance > Receivables workspace;
- granular Receivables/payment/write-off/dunning RBAC, with write-off authority withheld from the default Finance role;
- F2 regression coverage and Help manifest **1.11** topic `finance.receivables`.

When AR is configured, Sales source mutation/finalization, AR mutation, F1 GL posting/reversal, Finance number allocation, and required Audit Log evidence commit or roll back as one database transaction. When no AR configuration exists, Depot does not guess accounts/configuration and the existing Sales posting workflow remains available without AR/GL side effects.

The generic Finance core still contains no Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, bank/write-off-account, or statutory dunning default.

## Versions

- Application: **0.15.6-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **3**
- Help manifest: **1.11**

## Validation boundary

The F2 code path has passed the Release solution build and win-x64 single-file publish after its integration fixes. Acceptance compares test results against the F1 baseline so existing repository failures are not misclassified as Finance regressions. F2-specific regression failures must be resolved before the package is considered complete; pre-existing unrelated failures remain tracked separately.

Live SQL Server/MySQL/MariaDB Finance v3 migration, concurrency, locking, recovery, and representative load tests remain production acceptance gates.

Current electronic-invoice boundaries remain explicit: zero-rated/exempt/reverse-charge commercial scenarios require persisted EN 16931 tax semantics before issuance; electronic credit-note Buyer/XML finalization remains follow-up work; production recipient/channel validation remains a release/deployment gate.

The next Finance package is **F3 — Accounts Payable**: supplier invoices/credit notes, supplier open items, PO/goods-receipt/invoice matching, approval, and controlled GL integration.

Phase 8 enterprise readiness remains planned.
