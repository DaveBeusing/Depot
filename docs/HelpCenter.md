# Depot Help Center

Updated: 2026-09-01

Depot ships an embedded offline Markdown Help Center rendered natively in WPF. Help is permission-filtered, locally searchable, uses stable topic IDs, and opens in the normal workspace shell.

## Current manifest

Help manifest **1.20** contains the existing Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, Finance, Administration and Troubleshooting topics plus the current User Sessions administration guidance.

The session-related help contract includes:

- `administration.user-sessions` — visible with `Users.View`; documents Active/History views, Online Users/Active Sessions metrics, heartbeat presence, single-session termination, bulk user-session termination, revocation behavior and privacy boundaries.
- `administration.users` — documents that deactivating a user revokes all of that user's still-open sessions.
- `administration.audit-log` — documents that administrative session termination is Audit evidence while heartbeats are intentionally not audited.
- `getting-started.dashboard` — documents distinct Online Users and Active Sessions administration metrics and navigation to User Sessions.

Destructive session actions are not granted by Help visibility. Application services independently require `UserSessions.Terminate` for termination operations.

Eight Finance topics remain part of the manifest:

- `finance.foundation` — `Finance.View`
- `finance.general-ledger` — `FinanceGeneralLedger.View`
- `finance.receivables` — `FinanceReceivables.View`
- `finance.payables` — `FinancePayables.View`
- `finance.inventory-accounting` — `FinanceInventoryAccounting.View`
- `finance.banking` — `FinanceBanking.View`
- `finance.reporting` — `FinanceFinancialReporting.View`
- `finance.localization` — `FinanceLocalization.View`

**Finance > Localization** resolves to `finance.localization`. The topic documents explicit effective-dated assignment, `GENERIC → EU → DE` hierarchy resolution, country validation, custom-pack extensibility, registry support levels, RBAC, retained Audit evidence and the legal/tax/compliance boundary.

Help visibility never grants business access; service authorization remains authoritative.

## Content rules

Help must not imply default credentials, jurisdiction, currency, tax rate, chart/account, accounting standard, matching tolerance, reporting classification, statutory filing conformance or legal certification when those are not explicitly configured/implemented.

Session Help must distinguish online presence from explicit session end state, must not present heartbeats as Audit records, and must distinguish `Users.View` visibility from `UserSessions.Terminate` destructive rights. It must describe administrative revocation and recent ended-session history as implemented while leaving idle/max-age policies, retention, MFA and security analytics as future scope.

Finance Localization Help must clearly state that legal-entity country does not activate a pack automatically; effective localization requires explicit assignment; support levels are responsibility labels rather than pass/fail compliance flags; built-in references are immutable; custom packs can extend the framework; executable statutory behavior still requires code when metadata/configuration is insufficient; and assigning a pack is not legal, tax, HGB, GoBD, XRechnung or other statutory certification.

## Updating Help

1. Verify current UI, ViewModels, services, permissions and routes.
2. Create/update the Markdown topic.
3. Keep stable IDs and deterministic ordering.
4. Use only valid central permission codes and `topic:` links.
5. Increment the manifest version for material topic/permission/mapping changes.
6. Run Help regression validation for duplicate IDs, missing files, unknown permissions and broken links.

Help manifest **1.20** is the current documentation contract.
