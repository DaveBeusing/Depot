# Documentation status

Updated: 2026-08-28

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application: `0.15.36-preview`
- Help manifest: `1.15`
- Core database schema: `29`
- Sales feature schema: `8`
- Finance feature schema: `8`
- Finance F0-F6: complete
- Finance F7: next — Localization Framework

## F6 synchronization

The F6 documentation baseline synchronizes README, Architecture, Compliance Overview, Roadmap, Current Status, Finance Architecture/Compliance/Reporting, Release checklist, User-facing Changes, Help Center, embedded `finance.reporting` Help and the Help manifest.

Help manifest **1.15** adds stable topic `finance.reporting`, guarded by `FinanceFinancialReporting.View`.

## F6 documentation invariants

Documentation must state that:

- F1 remains the authoritative immutable General Ledger;
- GL-derived F6 reports use persisted reporting-currency journal values;
- AR/AP aging remains in open-item transaction currency unless a future persisted conversion model is added;
- cash-flow and tax classification require explicit account mappings and are not inferred from account names/numbers;
- historical inventory valuation comes from F4 valuation evidence;
- dimension filtering uses persisted F1 journal-line dimensions;
- CSV export is deterministic and permission-controlled;
- `FinanceReportSnapshot` is immutable `AuditEvidence` with parameter/content hashes and operation idempotency;
- Finance schema 8 is provider-neutral code for SQLite, SQL Server and MySQL/MariaDB, not live-provider certification;
- F7 localization/statutory packs are not implemented by F6.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply an unconfigured jurisdiction, currency, tax rate, chart/account, accounting standard or reporting classification;
- claim weighted-average, standard cost, LIFO, impairment/NRV or manufacturing costing as implemented;
- claim that F6 reports are jurisdiction-specific statutory filings;
- describe F7 as implemented;
- hide repository failures by attributing them to unrelated Finance changes.
