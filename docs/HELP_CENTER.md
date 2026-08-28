# Depot Help Center

Updated: 2026-08-28

Depot ships an embedded offline Markdown Help Center rendered natively in WPF. Help is permission-filtered, locally searchable, uses stable topic IDs, and opens in the normal workspace shell.

## Current manifest

Help manifest **1.15** contains seven Finance topics:

- `finance.foundation` — `Finance.View`
- `finance.general-ledger` — `FinanceGeneralLedger.View`
- `finance.receivables` — `FinanceReceivables.View`
- `finance.payables` — `FinancePayables.View`
- `finance.inventory-accounting` — `FinanceInventoryAccounting.View`
- `finance.banking` — `FinanceBanking.View`
- `finance.reporting` — `FinanceFinancialReporting.View`

## F6 context help

**Finance > Financial Reporting** resolves to `finance.reporting`. The topic documents report types, reporting-currency behavior, explicit account mappings, dimension filters, CSV export, immutable snapshots, RBAC and the statutory/localization boundary.

Help visibility never grants business access; service authorization remains authoritative.

## Content rules

Help must not imply default credentials, jurisdiction, currency, tax rate, chart/account, accounting standard, matching tolerance, reporting classification, statutory filing conformance or legal certification when those are not explicitly configured/implemented.

F6 Help must clearly state that:

- F1 is the authoritative General Ledger;
- GL-derived reports use persisted reporting-currency values;
- AR/AP aging is shown in transaction currency;
- Cash Flow and Tax Summary classifications are explicit mappings, not account-name guesses;
- snapshots retain canonical CSV plus parameter/content hashes and are immutable AuditEvidence;
- deterministic CSV export is not a jurisdiction-specific filing format;
- F7 localization packs remain future scope.

## Updating Help

1. Verify current UI, ViewModels, services, permissions and routes.
2. Create/update the Markdown topic.
3. Keep stable IDs and deterministic ordering.
4. Use only valid central permission codes and `topic:` links.
5. Increment the manifest version for material topic/permission/mapping changes.
6. Run Help regression validation for duplicate IDs, missing files, unknown permissions and broken links.

Help manifest **1.15** is the F6 documentation contract.
