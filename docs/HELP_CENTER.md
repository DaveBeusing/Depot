# Depot Help Center

Updated: 2026-08-28

Depot ships an embedded offline Markdown Help Center rendered natively in WPF. Help is permission-filtered, locally searchable, uses stable topic IDs, and opens in the normal workspace shell.

## Current manifest

Help manifest **1.12** contains four Finance topics:

- `finance.foundation` — `Finance.View`
- `finance.general-ledger` — `FinanceGeneralLedger.View`
- `finance.receivables` — `FinanceReceivables.View`
- `finance.payables` — `FinancePayables.View`

The new Accounts Payable topic documents F3 configuration, supplier-document lifecycle, three-way matching, explicit match-exception approval, AP open items, supplier payments/allocations/overpayments, reversals, aging/statements, RBAC, audit/concurrency behavior, provider boundaries and the F4 hand-off.

Purchasing Purchase Order/Goods Receipt topics and Finance Foundation/GL/AR/Audit topics are cross-linked where relevant.

## F3 context help

**Finance > Payables** resolves to `finance.payables`. Missing/unavailable context falls back through the existing Help service behavior. Help visibility does not grant business access; service authorization remains authoritative.

## Content rules

Help must not imply default credentials, jurisdiction, currency, tax rate, chart/account, accounting standard, AP/expense/bank account, matching tolerance, statutory invoice validation or legal certification when those are not explicitly configured/implemented.

F3 Help must clearly state that:

- matching is fail-closed with no implicit tolerance;
- match-exception approval requires a separate permission and retained reason;
- supplier-document approval and exception approval are distinct rights;
- posted accounting evidence is corrected through explicit reversal rather than destructive editing;
- F4 Inventory Accounting and later banking/reporting/localization packages are not yet implemented.

## Updating Help

1. Verify current UI, ViewModels, services, permissions and routes.
2. Create/update the Markdown topic.
3. Keep stable IDs and deterministic ordering.
4. Use only valid central permission codes and `topic:` links.
5. Increment the manifest version for material topic/permission/mapping changes.
6. Run Help regression validation for duplicate IDs, missing files, unknown permissions and broken links.

Help manifest 1.12 is the F3 documentation contract.
