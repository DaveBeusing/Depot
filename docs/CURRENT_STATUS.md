# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` line. Finance work packages **F0 through F7 are implemented** on branch `finance`. Remaining Finance work is production/environment/legal/accessibility/provider/signing acceptance plus demand-driven jurisdiction-pack extensions.

## Implemented Finance packages

- **F0 — International Finance Foundation:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, books, journals, dimensions, tax registrations and number sequences. Finance schema 1.
- **F1 — General Ledger & Posting Engine:** immutable balanced journals, reporting-currency snapshots, posting profiles, period/account/dimension validation, idempotency, Audit evidence and linked reversals. Finance schema 2.
- **F2 — Accounts Receivable:** customer open items, payments/allocations, write-offs, aging/statements, dunning and Sales → AR → GL integration. Finance schema 3.
- **F3 — Accounts Payable:** supplier documents/open items, three-way matching, exception approval, payments/allocations/reversals, aging/statements and AP → GL integration. Finance schema 4.
- **F4 — Inventory Accounting:** FIFO valuation, GRNI/COGS, inventory adjustments, PPV, landed cost, historical as-of valuation and Inventory ↔ GL reconciliation. Finance schema 6.
- **F5 — Banking and Payments:** bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position. Finance schema 7.
- **F6 — Financial Reporting:** trial balance, GL detail, balance sheet, P&L, cash flow, AR/AP aging, tax summary, historical inventory valuation, COGS, dimension filtering, explicit reporting mappings, deterministic CSV and immutable report snapshots. Finance schema 8.
- **F7 — Localization Framework:** effective-dated localization packs and legal-entity assignments, built-in `GENERIC → EU → DE` reference hierarchy, capability/compliance registry, custom country-pack extensibility, service RBAC, Audit evidence and **Finance > Localization**. Finance schema 9.

## F7 localization boundary

F7 is intentionally explicit. `LegalEntity.CountryCode` is a validation attribute, not an activation switch. A Germany legal entity remains jurisdiction-neutral until an authorized Finance user assigns an effective root localization pack. Active assignments for the same entity cannot overlap.

Effective profiles resolve the assigned root pack and its parents. Built-in `GENERIC`, `EU` and `DE` pack definitions and built-in registry rows are immutable. Custom packs can extend the hierarchy without another database schema change.

Registry rows classify a requirement as `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` or `ReferenceOnly`. These values describe responsibility and support boundaries, not legal/compliance status. F7 does not invent VAT rates, tax-return boxes, SKR03/SKR04 mappings, HGB/IFRS policy choices, statutory filing decisions, legal opinions or organization-specific procedures.

Assignments and registry entries are retained `AuditEvidence`. Custom writes use optimistic concurrency and structured Audit records.

## Versions

- Application: **0.15.40-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **9**
- Help manifest: **1.16**

`Directory.Build.props` is authoritative for the exact application patch. Each commit increments `DepotVersionPatch`.

## Validation boundary

F7 regression evidence covers Finance schema 9, built-in reference packs, Finance RBAC, retained localization evidence, explicit activation semantics, `GENERIC → EU → DE` resolution, country mismatch rejection, active-range overlap rejection, built-in immutability and custom-country extension without schema change.

Release Build, win-x64 publish, bounded repository tests, Release Integrity, Security Supply Chain and Software Quality gates must pass on the final head. Any pre-existing broad-suite failures are classified separately from F7.

Provider-neutral F7 DDL exists for SQLite, SQL Server and MySQL/MariaDB. Live SQL Server/MySQL/MariaDB Finance v9 migration, provider locking/concurrency/recovery and organization-specific localization acceptance remain production gates.

## Next steps

There is no additional mandatory Finance feature package after F7 in the current roadmap. Finance work now moves to production acceptance, deployment policy approval and demand-driven country/statutory extensions using the F7 framework.
