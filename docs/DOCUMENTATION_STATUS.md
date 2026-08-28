# Documentation status

Updated: 2026-08-28

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application line: `0.15.x-preview`
- F4 implementation baseline before documentation commit: `0.15.22-preview`
- Help manifest: `1.13`
- Core database schema: `29`
- Sales feature schema: `8`
- Finance feature schema: `6`
- Finance F0: complete — jurisdiction-neutral foundation
- Finance F1: complete — immutable General Ledger and posting engine
- Finance F2: complete — Accounts Receivable and Finance > Receivables
- Finance F3: complete — Accounts Payable and Finance > Payables
- Finance F4: complete — FIFO Inventory Accounting and Finance > Inventory Accounting
- Finance F5: next — Banking and Payments

## F4 documentation synchronization

The F4 documentation commit synchronizes central documentation and embedded Help with the completed F4 implementation. It does not introduce another database schema revision; F4 remains Finance schema **6**.

Synchronized surfaces include README, Finance architecture/compliance/status/roadmap documentation, Help Center documentation, the embedded `finance.inventory-accounting` article and Help manifest **1.13**.

Help manifest **1.13** is a material contract change because F4 adds stable topic `finance.inventory-accounting`, guarded by `FinanceInventoryAccounting.View`, and cross-links Inventory, Warehouse, Purchasing, Sales, Finance and Audit topics.

## F4 documentation invariants

Documentation must describe:

- `FinanceGeneralLedgerService` as the authoritative immutable accounting posting boundary;
- FIFO as the only currently implemented valuation method;
- Goods Receipt and Sales Shipment valuation/GL effects as transaction-coupled when F4 is active;
- inventory-count adjustment processing as idempotent movement-based valuation/catch-up, without rewriting Warehouse history;
- purchase-price variance as a post-AP financial consequence, not a replacement for F3 matching/approval;
- landed cost as explicit user-supplied capitalization/allocation input, not inferred accounting policy;
- historical reconciliation as true as-of reconstruction using consumption/reversal and landed-cost timing;
- reconciliation runs as immutable AuditEvidence snapshots;
- Finance v6 / Sales v8 / core 29 as independent schema levels;
- F5 Banking and Payments as the next Finance package.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical seller/buyer/accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply an unconfigured jurisdiction, currency, tax rate, chart, accounting standard, account or matching tolerance;
- claim weighted-average, standard cost, LIFO, impairment/NRV or manufacturing costing as implemented;
- claim that F4 determines which landed-cost components are legally/accountingly capitalizable;
- describe F5-F7 as implemented;
- imply that GL/AR/AP/inventory controls alone establish GoBD, HGB, IFRS, GAAP, tax or audit compliance;
- hide pre-existing repository failures by attributing them to unrelated Finance changes.
