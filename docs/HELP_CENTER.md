# Depot Help Center

Updated: 2026-08-28

Depot ships an embedded offline Markdown Help Center rendered natively in WPF. Help is permission-filtered, locally searchable, uses stable topic IDs, and opens in the normal workspace shell.

## Current manifest

Help manifest **1.16** contains eight Finance topics:

- `finance.foundation` — `Finance.View`
- `finance.general-ledger` — `FinanceGeneralLedger.View`
- `finance.receivables` — `FinanceReceivables.View`
- `finance.payables` — `FinancePayables.View`
- `finance.inventory-accounting` — `FinanceInventoryAccounting.View`
- `finance.banking` — `FinanceBanking.View`
- `finance.reporting` — `FinanceFinancialReporting.View`
- `finance.localization` — `FinanceLocalization.View`

## F7 context help

**Finance > Localization** resolves to `finance.localization`. The topic documents explicit effective-dated assignment, `GENERIC → EU → DE` hierarchy resolution, country validation, custom-pack extensibility, registry support levels, RBAC, retained Audit evidence and the legal/tax/compliance boundary.

Help visibility never grants business access; service authorization remains authoritative.

## Content rules

Help must not imply default credentials, jurisdiction, currency, tax rate, chart/account, accounting standard, matching tolerance, reporting classification, statutory filing conformance or legal certification when those are not explicitly configured/implemented.

F7 Help must clearly state that:

- legal-entity country does not activate a pack automatically;
- localization requires an explicit effective-dated assignment;
- support levels describe capability/configuration/procedure/reference responsibility and are not pass/fail compliance flags;
- built-in pack/registry definitions are immutable;
- custom packs can extend the framework without another schema change;
- additional executable jurisdiction behavior still requires code when metadata/configuration is insufficient;
- assigning a pack is not legal, tax, HGB, GoBD, XRechnung or other statutory certification.

## Updating Help

1. Verify current UI, ViewModels, services, permissions and routes.
2. Create/update the Markdown topic.
3. Keep stable IDs and deterministic ordering.
4. Use only valid central permission codes and `topic:` links.
5. Increment the manifest version for material topic/permission/mapping changes.
6. Run Help regression validation for duplicate IDs, missing files, unknown permissions and broken links.

Help manifest **1.16** is the F7 documentation contract.
