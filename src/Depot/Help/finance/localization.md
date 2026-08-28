# Finance Localization

Finance Localization provides an explicit, effective-dated framework for jurisdiction-specific Finance requirements without turning a country code into an automatic accounting or compliance decision.

## What the workspace does

Use **Finance > Localization** to review the effective localization profile for a legal entity, maintain localization assignments, add custom localization packs, and maintain effective-dated registry entries.

The built-in pack chain is:

- `GENERIC` — jurisdiction-neutral baseline
- `EU` — European Union reference layer
- `DE` — Germany reference layer

A Germany legal entity does **not** automatically activate the `DE` pack. A user with `FinanceLocalization.Manage` must create an explicit assignment with an effective date. Depot rejects overlapping active root assignments and rejects country packs whose country does not match the legal entity.

## Effective profile

Select a legal entity and an **As of** date, then choose **Resolve profile**. Depot resolves the assigned root pack and all parent packs in order and shows the effective capability/compliance registry entries for that date.

The registry uses four support levels:

- **SoftwareCapability** — Depot contains a technical capability relevant to the requirement.
- **ConfigurationRequired** — deployment-specific Finance, tax, numbering, account, document, or process configuration is still required.
- **ExternalProcedureRequired** — organizational, professional, legal, tax, filing, retention, signing, or other external procedure remains outside the software control itself.
- **ReferenceOnly** — informational reference; it must not be interpreted as implementation or compliance status.

A localization pack is never a compliance certificate. Built-in references deliberately do not invent tax rates, charts of accounts, filing decisions, accounting policies, or organization-specific legal conclusions.

## Assignments

Assignments connect one legal entity to one root localization pack for an effective date range. Legal entity, pack, and effective-from identity are immutable after creation. To change localization over time, close the existing effective range and create a later assignment.

## Pack catalog

`GENERIC`, `EU`, and `DE` are immutable built-in definitions. Custom regional or country packs can be added without a database schema change. A regional pack requires a broader parent. A country pack requires both a parent and an ISO 3166-1 alpha-2 country code.

## Registry

Registry entries are effective-dated and audit-retained. Built-in registry rows are immutable. Custom entries can be used for deployment-specific references or additional country packs. Close an existing effective range before creating a replacement requirement with the same pack and requirement code.

## Audit and permissions

Viewing requires `FinanceLocalization.View`; changing assignments, packs, or registry entries requires `FinanceLocalization.Manage`. The Finance system role includes both permissions. Assignments and registry entries are retained as Audit Evidence, and changes use optimistic concurrency and structured audit records.

## Deployment responsibility

Before production use, qualified Finance/tax/legal owners must approve the configured localization profile and the external procedures relevant to the operating entity. Live provider migration and organization-specific acceptance remain deployment gates.
