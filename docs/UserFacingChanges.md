# User-facing changes

Updated: 2026-09-17

Depot's current `0.15.x-preview` line includes the integrated Finance platform, persistent session/security administration, production-tested database-provider support, advanced commercial price generation and electronic-invoice issuance based on XRechnung CII.

## Scoped and advanced Sales pricing

Sales price lists use Global, Regional or optional Customer scope. Price resolution falls back Customer → Region → Global independently for each item, so special and regional lists only need to contain exceptions. Customer-specific price-list assignments and Sales Regions are optional. Quotes and Sales Orders display the resolved source and preserve submitted/finalized pricing snapshots.

Sales feature schema **12** retains the provider-equivalent active inventory-reservation uniqueness invariant from schema 11 and extends controlled bulk pricing with:

- explicit Percentage Markup and Target Gross Margin methods;
- direct effective-dated Cost Currency → PriceList Currency FX rates with source and version evidence;
- fail-closed behavior when required FX evidence is missing;
- deterministic commercial rounding to currency precision, 0.01, 0.05, 0.10, 0.50 or `.99` endings where applicable;
- explicit Preferred Supplier, Last Purchase, Manual Standard and Inventory Cost Reference base-cost strategies;
- Preview columns showing cost, FX, pricing-formula and rounding intermediates before Apply.

Advanced bulk Apply remains atomic and revalidates target-list, cost and FX evidence. Historical Sales documents are not repriced.

## Electronic invoicing

Sales feature schema **13** adds the retained production evidence for the bounded XRechnung 3.0 CII path. Depot supports explicit Standard Rated (`S`), Zero Rated (`Z`), Exempt (`E`) and Reverse Charge (`AE`) VAT semantics, retains exact finalized XML plus SHA-256 integrity evidence and supports electronic Sales Credit Note finalization using immutable source-invoice Buyer evidence.

Sales feature schema **14** adds the first ZUGFeRD 2.5.2 / Factur-X 1.09.2 hybrid-document implementation using the XRECHNUNG profile. During invoice and credit-note finalization Depot creates a PDF/A-3B document, embeds the exact finalized XML as `xrechnung.xml`, adds Factur-X XMP metadata and persists the exact PDF bytes plus XML/PDF SHA-256 evidence in the same transaction. Later export uses the retained artifact instead of rebuilding it from mutable master data.

The XRechnung XML matrix remains externally validated through the pinned KoSIT configuration. ZUGFeRD/Factur-X production acceptance additionally requires independent PDF/A-3 validation; that validator evidence is tracked separately and is not implied by the implementation alone.

## Finance workspaces

Finance provides permission-aware workspaces for **Receivables**, **Payables**, **Inventory Accounting**, **Banking**, **Financial Reporting**, and **Localization**. Financial posting consequences flow through the immutable General Ledger boundary.

## Finance Localization

Users with `FinanceLocalization.View` can select a Legal Entity and as-of date, resolve the effective localization profile, inspect the inherited pack chain and review capability/configuration/procedure references and warnings.

Users with `FinanceLocalization.Manage` can create/close effective-dated assignments, create custom regional/country packs, add effective-dated registry entries and maintain custom metadata under optimistic concurrency and Audit controls.

The built-in reference chain is `GENERIC → EU → DE`. A Germany Legal Entity does **not** automatically receive the Germany pack. Explicit assignment is required. Depot rejects country-pack assignments whose country does not match the Legal Entity and rejects overlapping active assignments.

Built-in pack definitions and built-in registry rows are immutable. Additional country packs can use the existing data model without another schema change when new executable behavior is not required.

## Security Event export and delivery

Security Events feature schema **3** adds persistent export targets and durable at-least-once delivery state while preserving the existing Security Event source records unchanged. Administrators can configure bounded export targets; delivery keeps filter identity, a durable checkpoint, a fixed in-flight snapshot boundary, retry/suspension state and a short worker lease. The checkpoint advances only after successful sink delivery. A crash after remote acceptance but before the local checkpoint update can therefore produce a duplicate, so the deterministic delivery identity is the receiver deduplication boundary.

## Database provider production support

The production database path is accepted on the exact baselines in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md):

- bundled SQLite runtime;
- SQL Server 2022 / engine 16.x;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

The matrix uses real providers and exercises migration, transaction/concurrency/retry behavior, Sales and Procurement, Finance GL/AR/AP/FIFO, Banking/reconciliation, Financial Reporting/snapshots, persistent sessions, server restart, provider-native remote backup/restore and representative 100k indexed access.

This technical support status does not change the jurisdiction/compliance boundary and does not automatically certify newer/older database versions.

## Compliance boundary

Localization support levels are `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly`. They are not legal/compliance pass/fail states. Depot does not automatically determine VAT rates, statutory chart mappings, tax return classifications, HGB/IFRS policy, filing eligibility or legal retention/signature obligations. Qualified deployment review remains required.

Provider-native remote backup scheduling/retention and disaster recovery remain operator responsibilities. SQLite's dynamic `NUMERIC` storage does not guarantee the same very-large fixed-decimal range as the server providers.

## Permissions and evidence

The default Finance system role includes operational Finance permissions according to the existing role definitions. Service-layer authorization remains authoritative regardless of UI visibility. Localization assignments and registry entries are retained `AuditEvidence`; custom changes create structured Audit records.

Advanced pricing uses existing Item and Sales Pricing permissions. FX-rate maintenance and bulk Apply require Sales Pricing management permission; item-cost maintenance requires Item edit/manage permission.

## Current technical baseline

- Application: **0.15.x-preview**
- Core database schema: **30**
- Sales schema: **14**
- Finance schema: **9**
- User Sessions schema: **3**
- Security Events schema: **3**
- User Preferences schema: **2**
- Help manifest: **1.21**

The exact application patch/version is authoritative in `Directory.Build.props`; this user-facing baseline intentionally records the moving development line instead of duplicating the patch number.

Database-provider technical acceptance is complete for the certified matrix. Organization-specific accounting/localization, accessibility, signing, deployment and legal acceptance remain separate release gates.
