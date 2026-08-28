# Depot Help Center

Updated: 2026-08-28

Depot ships an embedded offline Markdown Help Center rendered natively in WPF. Help is permission-filtered, locally searchable, uses stable topic IDs, and opens in the normal workspace shell.

## Current manifest

Help manifest **1.13** contains five Finance topics:

- `finance.foundation` — `Finance.View`
- `finance.general-ledger` — `FinanceGeneralLedger.View`
- `finance.receivables` — `FinanceReceivables.View`
- `finance.payables` — `FinancePayables.View`
- `finance.inventory-accounting` — `FinanceInventoryAccounting.View`

The Inventory Accounting topic documents F4 configuration/policy, FIFO valuation, Goods Receipt/GRNI, Sales Shipment/COGS, inventory-count adjustments, purchase-price variance, landed cost, historical as-of reconciliation, reversals, RBAC and accounting-policy/provider boundaries.

Inventory, Warehouse, Purchasing, Sales, Finance and Audit topics are cross-linked where relevant.

## F4 context help

**Finance > Inventory Accounting** resolves to `finance.inventory-accounting`. Missing/unavailable context falls back through the existing Help service behavior. Help visibility does not grant business access; service authorization remains authoritative.

## Content rules

Help must not imply default credentials, jurisdiction, currency, tax rate, chart/account, accounting standard, matching tolerance, capitalization decision, statutory invoice validation or legal certification when those are not explicitly configured/implemented.

F4 Help must clearly state that:

- FIFO is the only currently implemented costing method;
- valued inventory cannot silently go negative;
- receipt reversal can be blocked by downstream valuation consumption;
- PPV is separate from F3 matching/approval;
- landed-cost capitalization eligibility is an accounting-policy decision outside generic Finance;
- period-end reconciliation is technical accounting evidence, not a statutory compliance opinion;
- F5 Banking and Payments and later reporting/localization packages are not yet implemented.

## Updating Help

1. Verify current UI, ViewModels, services, permissions and routes.
2. Create/update the Markdown topic.
3. Keep stable IDs and deterministic ordering.
4. Use only valid central permission codes and `topic:` links.
5. Increment the manifest version for material topic/permission/mapping changes.
6. Run Help regression validation for duplicate IDs, missing files, unknown permissions and broken links.

Help manifest **1.13** is the F4 documentation contract.
