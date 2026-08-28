# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. Security/compliance roadmap phases 1 through 7 have their technically implementable repository/application controls in place. Remaining items are explicitly tracked as production, environment, legal, accessibility, provider, signing, tax-profile/routing, Finance-localization, or enterprise acceptance gates.

Key current capabilities include first-run administrator bootstrap, hardened authentication and RBAC, privacy discovery/export, immutable business-record evidence, CRA technical evidence, Company legal-identity master data, enriched Item master data, serial/lot traceability and lifecycle enforcement, immutable seller snapshots for posted invoices/credit notes, atomic Sales Invoice Buyer/XRechnung finalization with exact XML retention and SHA-256 integrity verification, posted-invoice XRechnung export, pinned KoSIT representative conformance validation, software-quality gates, accessibility static checks, SBOM/dependency audit, release-integrity automation, and Finance F0/F1.

Finance F0 — International Finance Foundation is the **0.15.0-preview** baseline. It adds legal entities, explicit currencies/exchange rates, fiscal calendars/accounting periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences, exchange-rate/tax/localization extension contracts, granular Finance RBAC, and provider-neutral Finance feature schema **1** for SQLite, SQL Server, and MySQL/MariaDB.

Finance F1 — General Ledger & Posting Engine is implemented at **0.15.1-preview**. It adds immutable journal-entry headers/lines, balanced double-entry validation, transaction/reporting currency and FX snapshots, posting profiles, operation/source-document idempotency, open-period enforcement, required dimensions, Finance number-sequence allocation, explicit linked reversals, atomic Audit Log persistence, optimistic profile concurrency, and provider-neutral transaction/race-safety boundaries. Finance feature schema is **2**.

The generic Finance core contains no Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, or other jurisdiction/accounting-standard default. Existing electronic-invoice functionality remains a separate capability.

The core database schema remains **29** and Sales feature schema remains **8**. Help manifest **1.10** adds the General Ledger and Posting topic alongside the Finance Foundation and existing serial/lot traceability help.

Free manual journals are protected by the dedicated `FinanceManualJournals.Post` permission and are not granted automatically to the Finance system role. Posted Finance journal entries are retained accounting records and corrections use a new reversal entry rather than mutating the original.

Current electronic-invoice boundaries remain explicit: zero-rated/exempt/reverse-charge commercial scenarios require persisted EN 16931 tax semantics before issuance, electronic credit-note Buyer/XML finalization remains follow-up work, and production recipient/channel validation remains a release/deployment gate.

The next Finance package is **F2 — Accounts Receivable**: Sales Invoice/Credit Note ledger integration, receivable open items, payment allocation, partial/overpayments, write-offs, dunning, and aging.

Phase 8 enterprise readiness remains planned.
