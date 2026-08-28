# Documentation status

Updated: 2026-08-28

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial documents/accounting records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application line: `0.15.x-preview`
- Current finance-branch version: `0.15.2-preview`
- Help manifest: `1.10`
- Core database schema: `29` plus additive provider-neutral feature/schema extensions
- Sales feature schema: `8`
- Finance feature schema: `2`
- Security/compliance roadmap: phases 1-7 technically implemented; environment/legal release gates remain where documented
- Item master data and traceability: enriched identification/lifecycle/trade/logistics data plus movement-derived serial/lot capture, balance/history, block/expiry and reversal-safe workflow enforcement
- Sales Invoice finalization: immutable seller + Buyer identity, exact persisted XRechnung XML, SHA-256 verification, posted-invoice XML export
- Electronic-invoice limitations: explicit special-tax semantics, electronic credit-note finalization, recipient/channel acceptance and full production-scenario validation remain open
- Finance F0: complete — legal entities, currencies/rates, calendars/periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences, localization/tax/exchange abstractions, provider-neutral schema v1 and granular RBAC
- Finance F1: complete — immutable balanced General Ledger entries, transaction/reporting currency + FX snapshots, posting profiles, operation/source idempotency, period enforcement, required dimensions, number allocation, linked reversals, atomic Audit Log evidence and schema v2
- Finance F2: next — Accounts Receivable and Sales Invoice/Credit Note integration
- Phase 8 enterprise readiness remains planned

## F1 documentation synchronization

The `0.15.2-preview` documentation commit synchronizes all central documentation with the F1 implementation. It does not introduce a new accounting feature or database schema revision.

Updated documentation surfaces include:

- `README.md`
- `docs/Architecture.md`
- `docs/CURRENT_STATUS.md`
- `docs/DOCUMENTATION_STATUS.md`
- `docs/USER_FACING_CHANGES.md`
- `docs/HELP_CENTER.md`
- `docs/COMPLIANCE_OVERVIEW.md`
- `docs/compliance/COMPLIANCE_MATRIX.md`
- `docs/RELEASE_1_0.md`
- embedded Finance Help articles under `src/Depot/Help/finance`

`docs/FINANCE_ARCHITECTURE.md`, `docs/FINANCE_COMPLIANCE.md`, and `docs/Roadmap.md` already describe F1/schema 2 and remain the detailed Finance architecture/compliance/roadmap references.

The Help manifest remains **1.10** because the refresh changes article wording only. Stable topic IDs, required permissions, related-topic contracts, and content-file mappings are unchanged.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical seller/buyer/accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply a jurisdiction, currency, tax rate, chart of accounts, accounting standard, or statutory filing configuration that has not been explicitly configured;
- describe future AR/AP/inventory-accounting/banking/reporting/localization work as already implemented;
- imply that the existence of a General Ledger engine alone establishes GoBD, HGB, IFRS, GAAP, tax, or audit compliance.

Documentation must clearly distinguish source operational records, retained General Ledger records, electronic-invoice evidence, and later subledger/localization responsibilities.
