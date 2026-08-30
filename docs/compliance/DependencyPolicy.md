# Depot Dependency Policy

## Purpose

This policy defines the minimum security, maintenance, and licensing expectations for third-party dependencies shipped with Depot.

## Requirements

- Production dependencies must be traceable through the release SBOM.
- Direct and transitive NuGet packages must be audited for known vulnerabilities.
- Dependency versions used by a release must be recoverable from source/build metadata.
- New dependencies require a clear product need and should be preferred from actively maintained projects with identifiable licensing.
- Abandoned or unsupported dependencies must be replaced, isolated, or explicitly risk accepted.
- Critical known vulnerabilities must not be knowingly shipped in production releases.
- High-severity findings require remediation or documented risk acceptance before release.
- License metadata must be captured in release dependency evidence and reviewed for compatibility with Depot distribution.

## Review triggers

Review a dependency when:

- it is newly introduced,
- its license changes,
- a security advisory affects it,
- it becomes deprecated or unsupported,
- a major version upgrade changes its security or licensing model,
- it is included transitively by another package and materially affects the shipped application.

## Release evidence

The security supply-chain workflow produces:

- `depot.cdx.json` — CycloneDX SBOM including available license evidence,
- `dependencies.txt` — direct/transitive NuGet inventory,
- `deprecated.txt` — NuGet deprecation inventory.

## Risk acceptance

Exceptions must document the dependency/version, reason it cannot be replaced immediately, known exposure, compensating controls, owner, review date, and planned remediation.

## Licensing

Automated metadata collection is evidence, not a legal license compatibility decision. Licenses that are missing, custom, copyleft, unusually restrictive, or otherwise unclear require manual review before a production release.
