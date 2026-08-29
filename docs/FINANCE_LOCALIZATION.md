# Finance Localization Framework

Updated: 2026-08-28

## Scope

Finance Localization supplies the jurisdiction-extension framework for Depot Finance. It is built on existing Legal Entity data and does not replace the General Ledger, tax configuration, reporting mappings or organization-specific accounting policy.

The design goal is to make country/regional requirements explicit, effective-dated, auditable and extensible while keeping generic Finance jurisdiction-neutral.

## Core invariants

1. **Country does not activate behavior.** `LegalEntity.CountryCode` validates country packs but never selects one automatically.
2. **Assignment is explicit and effective-dated.** One active root assignment can be effective for a legal entity at a time; overlapping active ranges fail closed.
3. **Hierarchy is resolved explicitly.** A root pack inherits broader parents. The built-in Germany chain is `GENERIC → EU → DE`.
4. **Built-in reference definitions are immutable.** Changes in understanding are represented by later effective custom entries or controlled repository updates, not silent mutation of historical evidence.
5. **No compliance flag exists.** Registry support levels describe responsibility/capability boundaries only.
6. **No accounting/tax defaults are invented.** Tax rates, charts of accounts, statutory filing mappings, valuation policy and legal conclusions remain explicit deployment decisions.
7. **The General Ledger remains the only GL authority.** Localization never posts journals.

## Persistence

Finance schema **9** contains:

- `FinanceLocalizationPacks`
- `FinanceLocalizationAssignments`
- `FinanceLocalizationRegistryEntries`

DDL exists for SQLite, SQL Server and MySQL/MariaDB.

### Localization packs

A pack defines a stable code/name, layer (`Generic`, `Regional`, `Country`), optional country code, parent, description, built-in/active state and optimistic version. Parent layers must be broader than children. Country packs require ISO 3166-1 alpha-2 country codes. Dependency cycles and excessive hierarchy depth fail closed.

### Assignments

Assignments bind a Legal Entity to one root pack for an effective date range. Legal entity, pack and effective-from identity are immutable after creation. Effective-to and active state can close an assignment. Active ranges for one legal entity may not overlap. Assignments record creator/time and are retained as `AuditEvidence`.

### Registry entries

Registry entries are effective-dated references attached to a pack. They contain a stable requirement code, category, support level, effective range, title/description/reference, built-in/active state and optimistic version. Built-in rows are immutable. Custom rows can extend built-in or custom packs under the effective-date collision rules and are retained as `AuditEvidence`.

## Built-in reference hierarchy

- `GENERIC` — jurisdiction-neutral baseline.
- `EU` — European Union reference layer.
- `DE` — Germany reference layer demonstrating country validation and inherited requirements.

The Germany reference can describe technical capabilities such as XRechnung-related support and external-procedure boundaries such as GoBD process/retention responsibilities. It deliberately does not define VAT rates, tax filing decisions, SKR mappings or legal compliance status.

## Support levels

- `SoftwareCapability` — Depot contains a relevant technical capability.
- `ConfigurationRequired` — deployment-specific Finance, tax, numbering, account, document or policy configuration is required.
- `ExternalProcedureRequired` — organizational, professional, legal, tax, filing, retention, signing or other external procedure remains outside the software control itself.
- `ReferenceOnly` — informational context only.

These are not legal/compliance pass/fail states.

## Service and UI boundary

`FinanceLocalizationService` owns permissions, legal-entity/pack validation, hierarchy resolution, effective-date rules, overlap prevention, optimistic concurrency, Audit evidence and immutable built-in protection. `FinanceLocalizationRepository` owns persistence. `FinanceLocalizationViewModel` owns UI state and commands. `FinanceLocalizationView` contains presentation only.

**Finance > Localization** provides Effective Profile, Assignments, Pack Catalog and Registry views. Viewing requires `FinanceLocalization.View`; mutations require `FinanceLocalization.Manage`.

## Extensibility and production acceptance

Additional regional/country packs do not require a schema migration when metadata/configuration is sufficient. New executable statutory behavior still requires separately scoped code.

Before production use, qualified Finance/tax/legal owners must approve configured profiles and external procedures. Live provider migration/concurrency/recovery and organization-specific acceptance remain deployment gates. The localization framework is engineering infrastructure and reference evidence, not certification, legal advice or a statutory compliance opinion.
