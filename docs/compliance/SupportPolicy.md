# Depot Support Policy

## Status

Technical lifecycle baseline for production planning. Commercial commitments and the support end-date for each marketed release line must be approved and published before production distribution.

## Version states

### Preview

Preview builds are development versions. They may contain incomplete migrations, functionality, security controls and compatibility behavior and are not production-supported or production-certified.

### Supported production

A commercially distributed production release line receives security maintenance for its published support period and according to the vulnerability-management policy.

### End of support

After the published support end-date, a version no longer receives normal fixes/security updates. Users must have a communicated migration/upgrade path where feasible and end-of-support must not silently occur before the published commitment expires.

## CRA support-period baseline

For a commercially placed Depot product in CRA scope, the release/product support period must be determined from expected use, user expectations, intended purpose, relevant law, operating-environment availability and core third-party component support.

The default engineering planning floor is **at least five years** from first commercial placing of the applicable product/release line. A shorter period may be used only when the product is genuinely expected to be in use for less than five years and the justification is documented and approved as part of the CRA technical/conformity record. A longer expected use requires evaluation of a longer support period.

Every production release line must publish an end date (month/year) and the factors used to determine it. The support date must be reviewable against runtime, Windows and database/provider lifecycles.

## Supported release policy

Before 1.0 production distribution, publish for each supported release line:

- product/version identification,
- support start and end month/year,
- supported Windows versions,
- supported SQL Server/MySQL/MariaDB versions where applicable,
- supported upgrade/migration paths,
- database schema/migration support policy,
- security-update distribution method,
- end-of-support notification approach,
- emergency security-update process.

## Security updates

Security updates are prioritized according to severity, exploitability, exposure, affected-user impact and applicable regulatory obligations. Release notes/advisories must allow users to determine affected versions and obtain the fixed version without unnecessarily publishing weaponizable detail before mitigation is available.

## Dependencies and operating environment

A Depot release must not remain nominally supported on an unsupported runtime or critical dependency without a documented migration/mitigation plan. .NET, Windows, database servers/providers and security-critical packages are lifecycle dependencies in support-period planning.

## Update and rollback expectations

Security updates use the normal release-integrity pipeline: locked dependencies, tests, source binding, hashes and production signing when available. Database/schema changes require backup and tested recovery/rollback guidance. Rollback must not reintroduce a known exploitable vulnerability without an explicit emergency decision and compensating controls.
