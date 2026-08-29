# User-facing changes

Updated: 2026-08-29

Depot's current `0.15.x-preview` line includes the integrated Finance platform.

## Scoped Sales pricing

Sales price lists now use Global, Regional or optional Customer scope. Price resolution falls back Customer → Region → Global independently for each item, so special and regional lists only need to contain exceptions. Customer-specific price-list assignments and Sales Regions are optional. Quotes and Sales Orders display the resolved source and preserve submitted/finalized pricing snapshots.

## Finance workspaces

Finance provides permission-aware workspaces for **Receivables**, **Payables**, **Inventory Accounting**, **Banking**, **Financial Reporting**, and **Localization**. Financial posting consequences flow through the immutable General Ledger boundary.

## Finance Localization

Users with `FinanceLocalization.View` can select a Legal Entity and as-of date, resolve the effective localization profile, inspect the inherited pack chain and review capability/configuration/procedure references and warnings.

Users with `FinanceLocalization.Manage` can create/close effective-dated assignments, create custom regional/country packs, add effective-dated registry entries and maintain custom metadata under optimistic concurrency and Audit controls.

The built-in reference chain is `GENERIC → EU → DE`. A Germany Legal Entity does **not** automatically receive the Germany pack. Explicit assignment is required. Depot rejects country-pack assignments whose country does not match the Legal Entity and rejects overlapping active assignments.

Built-in pack definitions and built-in registry rows are immutable. Additional country packs can use the existing data model without another schema change when new executable behavior is not required.

## Compliance boundary

Registry support levels are `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly`. They are not legal/compliance pass/fail states. Depot does not automatically determine VAT rates, statutory chart mappings, tax return classifications, HGB/IFRS policy, filing eligibility or legal retention/signature obligations. Qualified deployment review remains required.

## Permissions and evidence

The default Finance system role includes `FinanceLocalization.View` and `FinanceLocalization.Manage`. Service-layer authorization remains authoritative regardless of UI visibility. Localization assignments and registry entries are retained `AuditEvidence`; custom changes create structured Audit records.

## Current technical baseline

- Application: **0.15.x-preview**
- Finance schema: **9**
- Help manifest: **1.18**
- Provider-neutral schema/code: SQLite, SQL Server and MySQL/MariaDB

Live remote-provider migration/concurrency/recovery/performance and organization-specific localization acceptance remain required before production-provider or jurisdiction-compliance support claims.
