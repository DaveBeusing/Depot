# Documentation status

Updated: 2026-08-27

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial documents from persisted historical identity rather than mutable current master data.

## Current baseline

- Application line: `0.15.x-preview`
- Current finance-branch version: `0.15.140-preview`
- Help manifest: `1.9`
- Core database schema: `29` plus additive provider-neutral feature/schema extensions
- Sales feature schema: `8`
- Finance feature schema: `1`
- Security/compliance roadmap: phases 1-7 technically implemented; environment/legal release gates remain where documented
- Item master data and traceability: enriched identification/lifecycle/trade/logistics data plus movement-derived serial/lot capture, balance/history, block/expiry and reversal-safe workflow enforcement
- Sales Invoice finalization: immutable seller + Buyer identity, exact persisted XRechnung XML, SHA-256 verification, posted-invoice XML export
- Electronic-invoice limitations: explicit special-tax semantics, electronic credit-note finalization, recipient/channel acceptance and full production-scenario validation remain open
- Finance F0: complete — legal entities, currencies/rates, calendars/periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences, localization/tax/exchange abstractions, provider-neutral schema and granular RBAC
- Finance F1: next — General Ledger & Posting Engine
- Phase 8 enterprise readiness remains planned

## Primary user documentation

- `README.md`
- embedded Help Center under `src/Depot/Help`
- `docs/USER_FACING_CHANGES.md`
- `docs/HELP_CENTER.md`

## Primary engineering/release documentation

- `docs/Architecture.md`
- `docs/FINANCE_ARCHITECTURE.md`
- `docs/FINANCE_COMPLIANCE.md`
- `docs/ITEM_MASTER_DATA.md`
- `docs/ITEM_TRACEABILITY.md`
- `docs/CodingStandard.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/SECURITY_ROADMAP.md`
- `docs/compliance/*`

The Help Center and README must not document removed default credentials, reconstruct historical financial identity from current master data, describe technical compliance controls as legal certification, or imply a jurisdiction/currency/tax/accounting configuration that has not been explicitly configured.
