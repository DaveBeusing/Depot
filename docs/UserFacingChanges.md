# User-facing changes

Updated: 2026-09-08

Depot's current `0.15.x-preview` line includes the integrated Finance platform, persistent session/security administration and a production-tested database-provider matrix.

## Scoped Sales pricing

Sales price lists use Global, Regional or optional Customer scope. Price resolution falls back Customer → Region → Global independently for each item, so special and regional lists only need to contain exceptions. Customer-specific price-list assignments and Sales Regions are optional. Quotes and Sales Orders display the resolved source and preserve submitted/finalized pricing snapshots.

Sales feature schema **11** preserves the pricing structures introduced through schema 10 and adds the provider-equivalent active inventory-reservation uniqueness invariant required by the order-to-cash path.

## Finance workspaces

Finance provides permission-aware workspaces for **Receivables**, **Payables**, **Inventory Accounting**, **Banking**, **Financial Reporting**, and **Localization**. Financial posting consequences flow through the immutable General Ledger boundary.

## Finance Localization

Users with `FinanceLocalization.View` can select a Legal Entity and as-of date, resolve the effective localization profile, inspect the inherited pack chain and review capability/configuration/procedure references and warnings.

Users with `FinanceLocalization.Manage` can create/close effective-dated assignments, create custom regional/country packs, add effective-dated registry entries and maintain custom metadata under optimistic concurrency and Audit controls.

The built-in reference chain is `GENERIC → EU → DE`. A Germany Legal Entity does **not** automatically receive the Germany pack. Explicit assignment is required. Depot rejects country-pack assignments whose country does not match the Legal Entity and rejects overlapping active assignments.

Built-in pack definitions and built-in registry rows are immutable. Additional country packs can use the existing data model without another schema change when new executable behavior is not required.

## Database provider production support

The production database path is now accepted on the exact baselines in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md):

- bundled SQLite runtime;
- SQL Server 2022 / engine 16.x;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

The matrix uses real providers and exercises migration, transaction/concurrency/retry behavior, Sales and Procurement, Finance GL/AR/AP/FIFO, Banking/reconciliation, Financial Reporting/snapshots, persistent sessions, server restart, provider-native remote backup/restore and representative 100k indexed access.

This changes the technical support status from “provider-neutral implementation only” to a defined certified baseline. It does not change the jurisdiction/compliance boundary and does not automatically certify newer/older database versions.

## Compliance boundary

Localization support levels are `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly`. They are not legal/compliance pass/fail states. Depot does not automatically determine VAT rates, statutory chart mappings, tax return classifications, HGB/IFRS policy, filing eligibility or legal retention/signature obligations. Qualified deployment review remains required.

Provider-native remote backup scheduling/retention and disaster recovery remain operator responsibilities. SQLite's dynamic `NUMERIC` storage does not guarantee the same very-large fixed-decimal range as the server providers.

## Permissions and evidence

The default Finance system role includes operational Finance permissions according to the existing role definitions. Service-layer authorization remains authoritative regardless of UI visibility. Localization assignments and registry entries are retained `AuditEvidence`; custom changes create structured Audit records.

## Current technical baseline

- Application: **0.15.169-preview**
- Core database schema: **30**
- Sales schema: **11**
- Finance schema: **9**
- User Sessions schema: **3**
- Security Events schema: **2**
- Help manifest: **1.21**

Database-provider technical acceptance is complete for the certified matrix. Organization-specific accounting/localization, accessibility, signing, deployment and legal acceptance remain separate release gates.
