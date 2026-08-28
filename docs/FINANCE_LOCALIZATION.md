# Finance Localization Framework

Updated: 2026-08-28

## Scope

Finance package **F7** supplies the jurisdiction-localization extension framework for Depot Finance. It is implemented on top of F0 legal entities and does not replace the General Ledger, tax configuration, reporting mappings or organization-specific accounting policy.

The design goal is to make country/regional requirements explicit, effective-dated, auditable and extensible while keeping generic Finance jurisdiction-neutral.

## Core invariants

1. **Country does not activate behavior.** `LegalEntity.CountryCode` validates country packs but never selects one automatically.
2. **Assignment is explicit and effective-dated.** One active root assignment can be effective for a legal entity at a time; overlapping active ranges fail closed.
3. **Hierarchy is resolved explicitly.** A root pack inherits broader parents. The built-in Germany chain is `GENERIC → EU → DE`.
4. **Built-in reference definitions are immutable.** Changes in legal/regulatory understanding are represented by later effective custom entries or repository updates, not silent mutation of old evidence.
5. **No compliance flag exists.** Registry support levels describe responsibility/capability boundaries only.
6. **No accounting/tax defaults are invented.** Tax rates, charts of accounts, statutory filing mappings, valuation policy and legal conclusions remain explicit deployment decisions.
7. **F1 remains the only GL authority.** F7 does not post journals.

## Persistence

Finance schema **9** adds:

- `FinanceLocalizationPacks`
- `FinanceLocalizationAssignments`
- `FinanceLocalizationRegistryEntries`

DDL exists for SQLite, SQL Server and MySQL/MariaDB.

### FinanceLocalizationPacks

A pack defines:

- stable code and name;
- layer: `Generic`, `Regional`, or `Country`;
- optional ISO 3166-1 alpha-2 country code for country packs;
- parent pack code;
- description;
- built-in/active flags;
- optimistic version.

Generic packs have no parent/country. Regional packs require a broader parent and no country. Country packs require both a broader parent and a country. A parent must belong to a broader layer. Dependency cycles and excessive hierarchy depth fail closed.

### FinanceLocalizationAssignments

Assignments bind a Legal Entity to one root pack for an effective date range. Legal entity, pack and effective-from identity are immutable after creation. Effective-to and active state can close an assignment. Active ranges for one legal entity may not overlap.

Assignments record creator/time and are classified as `AuditEvidence`.

### FinanceLocalizationRegistryEntries

Registry entries are effective-dated references attached to a pack. They include:

- stable requirement code;
- requirement category;
- support level;
- effective range;
- title/description/reference;
- built-in/active state;
- optimistic version.

Built-in rows are immutable. Custom rows can be added to built-in or custom packs, subject to effective-date collision rules. Registry entries are retained `AuditEvidence`.

## Built-in reference hierarchy

### GENERIC

Jurisdiction-neutral baseline. It exists so country and regional packs inherit an explicit common layer rather than duplicating generic requirements.

### EU

European Union reference layer. It identifies EU-level capability/configuration/procedure boundaries without claiming that a deployment meets EU law merely because the pack is assigned.

### DE

German reference layer. It is a reference implementation demonstrating country validation and inherited requirements. It can describe existing technical capabilities such as XRechnung-related support and external-procedure boundaries such as GoBD process/retention responsibilities, but it deliberately does not define VAT rates, tax filing decisions, SKR mappings or legal compliance status.

## Support levels

`FinanceLocalizationSupportLevel` has four meanings:

- `SoftwareCapability`: Depot contains a relevant technical capability.
- `ConfigurationRequired`: the deployment must supply/approve configuration or policy.
- `ExternalProcedureRequired`: a process, review, filing, retention, signature, legal/tax/accounting decision or organizational control remains outside the software capability itself.
- `ReferenceOnly`: informational context only.

The effective profile returns warnings when configuration or external procedures remain. Those warnings are operational prompts, not legal advice.

## Service boundary

`FinanceLocalizationService` owns:

- permissions;
- legal-entity and pack validation;
- hierarchy resolution;
- effective-date rules;
- overlap prevention;
- optimistic concurrency;
- Audit evidence;
- normalization of pack/requirement codes;
- immutable built-in protection.

`FinanceLocalizationRepository` owns SQL and row mapping. `FinanceLocalizationViewModel` owns UI state and commands. `FinanceLocalizationView` contains presentation only.

## RBAC

F7 permissions are:

- `FinanceLocalization.View`
- `FinanceLocalization.Manage`

The Finance system role receives both. Administrator continues to receive the complete permission catalog. UI visibility is convenience only; service authorization is authoritative.

## Workspace

**Finance > Localization** provides:

- Effective Profile: legal entity + as-of date, resolved pack chain and effective registry;
- Assignments: effective-dated root-pack assignment;
- Pack Catalog: built-in and custom pack definitions;
- Registry: effective-dated capability/configuration/procedure/reference entries.

The workspace uses the shared Depot dark design system and existing asynchronous command/operation handling.

## Extensibility

Additional regional/country packs do not require another schema migration solely to add a jurisdiction. A custom pack can be created with a broader parent and then assigned to matching Legal Entities. Custom registry entries can be effective-dated independently.

A future code package may still be required if a jurisdiction needs new software behavior rather than configuration/reference metadata. F7 does not pretend metadata alone implements a missing statutory workflow.

## Evidence and tests

F7 regression coverage verifies:

- Finance migration reaches schema 9 and creates all localization tables;
- built-in reference packs exist;
- Finance role receives F7 permissions;
- localization records are classified as retained Audit evidence;
- legal-entity country alone does not activate localization;
- Germany resolves `GENERIC → EU → DE` after explicit assignment;
- country mismatch fails closed;
- overlapping active assignments fail closed;
- built-in definitions/registry rows reject mutation;
- a new custom country pack and requirement work without changing schema version.

## Production acceptance

Before claiming a jurisdiction is production-ready, retain qualified review of:

- Legal Entity country and assigned effective pack;
- accounting books, charts, posting profiles and tax configuration;
- all `ConfigurationRequired` entries;
- all `ExternalProcedureRequired` entries;
- statutory document/report/filing requirements;
- retention/archive/signature/export procedures;
- live SQL Server/MySQL/MariaDB migration/concurrency/recovery behavior where applicable.

F7 is engineering infrastructure and reference evidence. It is not certification, legal advice or a statutory compliance opinion.
