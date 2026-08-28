# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. The current `finance` documentation synchronization is **0.15.2-preview**. Security/compliance roadmap phases 1 through 7 have their technically implementable repository/application controls in place. Remaining items are explicitly tracked as production, environment, legal, accessibility, provider, signing, tax-profile/routing, Finance-localization, or enterprise acceptance gates.

Key current capabilities include first-run administrator bootstrap, hardened authentication and RBAC, privacy discovery/export, immutable business-record evidence, CRA technical evidence, Company legal-identity master data, enriched Item master data, serial/lot traceability and lifecycle enforcement, immutable seller snapshots for posted invoices/credit notes, atomic Sales Invoice Buyer/XRechnung finalization with exact XML retention and SHA-256 integrity verification, posted-invoice XRechnung export, pinned KoSIT representative conformance validation, software-quality gates, accessibility static checks, SBOM/dependency audit, release-integrity automation, and Finance F0/F1.

## Finance F0

**F0 — International Finance Foundation** is the `0.15.0-preview` baseline. It adds legal entities, explicit currencies/exchange rates, fiscal calendars/accounting periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences, exchange-rate/tax/localization extension contracts, granular Finance RBAC, and provider-neutral Finance feature schema **1** for SQLite, SQL Server, and MySQL/MariaDB.

## Finance F1

**F1 — General Ledger & Posting Engine** is implemented from `0.15.1-preview`; `0.15.2-preview` synchronizes the complete central/user/help documentation with that implementation.

F1 adds:

- immutable journal-entry headers and lines;
- balanced double-entry validation in transaction and reporting currency;
- transaction/reporting currency and exchange-rate snapshots;
- posting profiles and named amount-key account determination;
- operation and source-document idempotency;
- accounting-period open/date/legal-entity enforcement;
- active account/chart/direct-posting validation;
- required accounting dimensions;
- Finance General Ledger number-sequence allocation inside the posting transaction;
- explicit linked reversals instead of destructive corrections;
- atomic central Audit Log persistence with rollback on audit failure;
- optimistic posting-profile concurrency plus database uniqueness race-safety boundaries;
- separate sensitive permission for free manual journals.

Finance feature schema is **2**. The core database schema remains **29** and Sales feature schema remains **8**.

The generic Finance core contains no Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, or other jurisdiction/accounting-standard default. Existing electronic-invoice functionality remains a separate capability.

Help manifest **1.10** contains Finance Foundation and General Ledger and Posting topics. The 0.15.2 documentation refresh updates those articles without changing their stable topic IDs, permission contracts, or file mappings; therefore the manifest version remains 1.10.

Free manual journals are protected by `FinanceManualJournals.Post` and are not granted automatically to the Finance system role. Posted Finance journal entries are retained accounting records and corrections use a new linked reversal entry rather than mutating the original.

Current electronic-invoice boundaries remain explicit: zero-rated/exempt/reverse-charge commercial scenarios require persisted EN 16931 tax semantics before issuance, electronic credit-note Buyer/XML finalization remains follow-up work, and production recipient/channel validation remains a release/deployment gate.

The next Finance package is **F2 — Accounts Receivable**: Sales Invoice/Credit Note ledger integration, receivable open items, payment allocation including partial/overpayments, write-offs, dunning, and aging.

Phase 8 enterprise readiness remains planned.
