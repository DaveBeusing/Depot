# Documentation status

Updated: 2026-08-28

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application line: `0.15.x-preview`
- F3 documentation baseline: `0.15.14-preview`
- Help manifest: `1.12`
- Core database schema: `29`
- Sales feature schema: `8`
- Finance feature schema: `4`
- Finance F0: complete — jurisdiction-neutral foundation
- Finance F1: complete — immutable General Ledger and posting engine
- Finance F2: complete — Accounts Receivable and Finance > Receivables
- Finance F3: complete — Accounts Payable and Finance > Payables
- Finance F4: next — Inventory Accounting

## F3 documentation synchronization

The `0.15.14-preview` documentation commit synchronizes the central documentation and embedded Help with the completed F3 implementation. It does not introduce another Finance database schema revision; F3 remains Finance schema **4**.

Synchronized surfaces include:

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
- embedded `src/Depot/Help/finance/payables.md`
- Help manifest `1.12`

Help manifest **1.12** is a material contract change because F3 adds stable topic `finance.payables`, guarded by `FinancePayables.View`, and cross-links Purchasing/Finance/Audit topics.

## F3 documentation invariants

Documentation must describe:

- `FinanceGeneralLedgerService` as the authoritative immutable accounting posting boundary;
- `FinanceAccountsPayableService` as the supplier-subledger boundary;
- supplier document, AP open-item, GL and Audit effects as one transaction for posting/reversal;
- explicit supplier invoice/credit-note lifecycle rather than destructive editing after posting;
- PO/goods-receipt/invoice matching as fail-closed with no implicit tolerance;
- match exceptions as explicit evidence requiring `FinanceSupplierMatchExceptions.Approve` and a reason;
- supplier-document approval and match-exception approval as separate permissions not granted to the default Finance role;
- supplier payment/allocation/reversal behavior as controlled settlement evidence;
- Finance v4 / Sales v8 / core 29 as independent schema levels;
- F4 Inventory Accounting as the next Finance package.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical seller/buyer/accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply an unconfigured jurisdiction, currency, tax rate, chart, accounting standard, account or matching tolerance;
- imply that F3 performs tax determination or inbound statutory e-invoice validation;
- describe F4 inventory valuation/COGS/GRNI or later banking/reporting/localization work as implemented;
- imply that GL/AR/AP controls alone establish GoBD, HGB, IFRS, GAAP, tax, audit or statutory payment compliance;
- hide pre-existing repository failures by attributing them to unrelated Finance changes.

Documentation must distinguish Sales/Purchasing source records, AR/AP subledger evidence, immutable General Ledger evidence, electronic-invoice evidence and later localization/banking/inventory-accounting responsibilities.
