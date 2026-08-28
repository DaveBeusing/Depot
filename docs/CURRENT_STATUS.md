# Current project status

Updated: 2026-08-27

Depot is on the `0.15.x-preview` line. Security/compliance roadmap phases 1 through 7 have their technically implementable repository/application controls in place. Remaining items are explicitly tracked as production, environment, legal, accessibility, provider, signing, tax-profile/routing, Finance-localization, or enterprise acceptance gates.

Key current capabilities include first-run administrator bootstrap, hardened authentication and RBAC, privacy discovery/export, immutable business-record evidence, CRA technical evidence, Company legal-identity master data, enriched Item master data, serial/lot traceability and lifecycle enforcement, immutable seller snapshots for posted invoices/credit notes, atomic Sales Invoice Buyer/XRechnung finalization with exact XML retention and SHA-256 integrity verification, posted-invoice XRechnung export, pinned KoSIT representative conformance validation, software-quality gates, accessibility static checks, SBOM/dependency audit, release-integrity automation, and Finance F0.

Finance F0 — International Finance Foundation is complete on `finance` at **0.15.140-preview**. It adds legal entities, explicit currencies/exchange rates, fiscal calendars/accounting periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences, exchange-rate/tax/localization extension contracts, granular Finance RBAC, and provider-neutral Finance feature schema **1** for SQLite, SQL Server, and MySQL/MariaDB.

The generic Finance foundation contains no Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, or other jurisdiction/accounting-standard default. Existing electronic-invoice functionality remains a separate capability.

The core database schema remains **29** and Sales feature schema remains **8**. Help manifest **1.9** includes the Finance Foundation topic while retaining the serial/lot traceability help introduced in 1.8.

Current electronic-invoice boundaries remain explicit: zero-rated/exempt/reverse-charge commercial scenarios require persisted EN 16931 tax semantics before issuance, electronic credit-note Buyer/XML finalization remains follow-up work, and production recipient/channel validation remains a release/deployment gate.

The next Finance package is **F1 — General Ledger & Posting Engine**: immutable balanced journal entries, posting profiles, source-document idempotency, period locks, reversals, transaction/audit integration and provider-neutral concurrency/race safety.

Phase 8 enterprise readiness remains planned.
