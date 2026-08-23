# Depot Security Update Lifecycle

## Purpose

Define the technical lifecycle for creating, validating, distributing, communicating and, where necessary, rolling back Depot security updates.

## Intake and decision

A security update starts from a tracked vulnerability/incident record. The record identifies affected versions, severity, exploitability, owner, target fix, regulatory/reporting status and whether emergency handling is required.

## Fix creation

Security fixes must:

- use a dedicated reviewed change/branch or equivalent controlled change set,
- avoid unrelated refactoring where it increases review risk,
- include a regression test or documented verification where technically feasible,
- update affected threat/risk/compliance documentation,
- update dependency locks/SBOM evidence when dependencies change,
- increment the application version according to project versioning rules.

## Validation

Before release, the update must pass the applicable normal and security gates:

- locked dependency restore,
- NuGet vulnerability audit,
- security risk-acceptance validation,
- security/business-integrity regression tests,
- normal CI tests/build,
- release artifact/source identity verification,
- SHA-256 manifest generation,
- Authenticode signing and timestamp verification for production-tagged releases once the production signing identity is configured.

Emergency releases may shorten process time but must not silently bypass security gates. Any unavailable gate requires an explicit incident decision and retained evidence.

## Distribution

A security release must identify:

- affected versions,
- fixed version,
- severity/impact in sufficient user-facing terms,
- required user action,
- whether database/configuration migration is required,
- support/contact channel,
- any temporary mitigation if immediate upgrade is not possible.

The authoritative binary/package must be distributed from an approved release channel and be verifiable against the release-integrity evidence.

## Rollback

Rollback planning must consider both application and database state. Before a schema-changing update:

- create/verify a backup according to recovery policy,
- document whether database downgrade is supported,
- test recovery/migration behavior for supported providers before production acceptance,
- preserve audit/business-record integrity.

A rollback must not knowingly restore a version with an actively exploited or Critical vulnerability unless required as an emergency containment action, explicitly approved, time-bounded and protected by compensating controls. Such a decision is an incident record, not a normal risk exception.

## Post-release

After a security update:

- verify published artifact/signature/hash,
- update vulnerability status and fix version,
- complete applicable CRA final reporting/user communication,
- update the CRA risk assessment when the event changes product risk,
- retain release, test and reporting evidence.
