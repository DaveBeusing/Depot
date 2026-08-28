# User-facing changes

Updated: 2026-08-28

Depot's current `0.15.x-preview` line includes the completed **Finance F0-F7** baseline.

## Finance workspaces

Finance now provides permission-aware workspaces for **Receivables**, **Payables**, **Inventory Accounting**, **Banking**, **Financial Reporting**, and **Localization**. All financial posting consequences still flow through the existing immutable General Ledger boundary.

## F7 — Finance Localization

Users with `FinanceLocalization.View` can:

- select a Legal Entity and an as-of date;
- resolve the effective localization profile;
- see the inherited pack chain and effective capability/configuration/procedure registry;
- review warnings for requirements that still need deployment configuration or external procedures.

Users with `FinanceLocalization.Manage` can additionally:

- create and close effective-dated localization assignments;
- create custom regional/country packs;
- add effective-dated custom registry entries;
- maintain custom pack/registry metadata under optimistic concurrency and Audit controls.

The built-in reference chain is `GENERIC → EU → DE`. A Germany Legal Entity does **not** automatically receive the Germany pack. An explicit assignment is required. Depot rejects a country-pack assignment when the pack country does not match the Legal Entity and rejects overlapping active assignments for the same entity.

Built-in pack definitions and built-in registry rows are immutable. Additional country packs can be added using the existing F7 data model without another schema change unless new executable software behavior is required.

## Compliance boundary

Registry support levels mean:

- `SoftwareCapability`
- `ConfigurationRequired`
- `ExternalProcedureRequired`
- `ReferenceOnly`

They are not legal/compliance pass/fail states. F7 does not automatically determine VAT rates, statutory chart mappings, tax return classifications, HGB/IFRS policy, filing eligibility or legal retention/signature obligations. Qualified deployment review remains required.

## Permissions and evidence

F7 adds `FinanceLocalization.View` and `FinanceLocalization.Manage`. The default Finance system role includes both. Service-layer authorization remains authoritative regardless of UI visibility.

Localization assignments and registry entries are retained `AuditEvidence`. Built-in reference definitions are protected from mutation; custom changes create structured Audit records.

## Current technical baseline

- Application: **0.15.40-preview**
- Finance schema: **9**
- Help manifest: **1.16**
- Provider-neutral schema/code: SQLite, SQL Server and MySQL/MariaDB

Live remote-provider migration/concurrency/recovery/performance and organization-specific localization acceptance remain required before production-provider or jurisdiction-compliance support claims.
