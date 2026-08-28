# Documentation status

Updated: 2026-08-28

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial documents/accounting records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application line: `0.15.x-preview`
- Current finance-branch documentation baseline: `0.15.6-preview`
- Help manifest: `1.11`
- Core database schema: `29` plus additive provider-neutral feature/schema extensions
- Sales feature schema: `8`
- Finance feature schema: `3`
- Security/compliance roadmap: phases 1-7 technically implemented; environment/legal release gates remain where documented
- Item master data and traceability: enriched identification/lifecycle/trade/logistics data plus movement-derived serial/lot capture, balance/history, block/expiry and reversal-safe workflow enforcement
- Sales Invoice finalization: immutable seller + Buyer identity, exact persisted XRechnung XML, SHA-256 verification, posted-invoice XML export
- Electronic-invoice limitations: explicit special-tax semantics, electronic credit-note finalization, recipient/channel acceptance and full production-scenario validation remain open
- Finance F0: complete — jurisdiction-neutral legal entity/currency/calendar/chart/book/tax/dimension/number-sequence foundation
- Finance F1: complete — immutable balanced General Ledger, posting profiles, FX snapshots, idempotency, period/dimension validation, number allocation, linked reversals, atomic Audit Log evidence, schema v2
- Finance F2: complete — Accounts Receivable, configured Sales→AR→GL integration, open items, payments/allocations/overpayments, payment/write-off reversals, aging/statements, dunning, Finance workspace, schema v3
- Finance F3: next — Accounts Payable
- Phase 8 enterprise readiness remains planned

## F2 documentation synchronization

The `0.15.6-preview` documentation commit synchronizes all central documentation and embedded Help with the completed F2 implementation. It does not introduce another Finance database schema revision; F2 remains Finance schema **3**.

Updated documentation surfaces include:

- `README.md`
- `docs/Architecture.md`
- `docs/FINANCE_ARCHITECTURE.md`
- `docs/FINANCE_COMPLIANCE.md`
- `docs/Roadmap.md`
- `docs/CURRENT_STATUS.md`
- `docs/DOCUMENTATION_STATUS.md`
- `docs/USER_FACING_CHANGES.md`
- `docs/HELP_CENTER.md`
- `docs/COMPLIANCE_OVERVIEW.md`
- `docs/compliance/COMPLIANCE_MATRIX.md`
- `docs/RELEASE_1_0.md`
- embedded Finance/Sales Help articles under `src/Depot/Help`

Help manifest **1.11** is a material contract change because F2 adds the stable `finance.receivables` topic and its `FinanceReceivables.View` permission boundary.

## F2 documentation invariants

Documentation must describe:

- `FinanceGeneralLedgerService` as the authoritative immutable accounting posting boundary;
- `FinanceAccountsReceivableService` as the AR subledger boundary;
- configured Sales Invoice/Credit Note → AR → GL effects as one transaction;
- optional AR activation: without active AR configuration, no Finance defaults or GL postings are invented;
- F2's explicit dependency on the current Sales schema/customer master;
- receivable allocations and reversals as controlled settlement/correction evidence;
- write-off authority as sensitive and not granted to the default Finance role;
- dunning as configurable evidence, not jurisdiction-specific legal collections compliance;
- Finance v3 / Sales v8 / core 29 as independent schema levels;
- F3 Accounts Payable as the next Finance package.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical seller/buyer/accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply a jurisdiction, currency, tax rate, chart of accounts, accounting standard, bank/write-off account, or statutory dunning rule that has not been explicitly configured;
- describe future AP/inventory-accounting/banking/reporting/localization work as implemented;
- imply that GL/AR controls alone establish GoBD, HGB, IFRS, GAAP, tax, collections, or audit compliance;
- hide pre-existing repository test failures by attributing them to or resolving them through unrelated Finance business-logic changes.

Documentation must clearly distinguish Sales source records, AR subledger evidence, immutable General Ledger records, electronic-invoice evidence, and later AP/banking/localization responsibilities.
