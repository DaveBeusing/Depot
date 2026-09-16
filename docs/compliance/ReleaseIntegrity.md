# Depot Release Integrity

## Authoritative release source

`.github/workflows/release-integrity.yml` is the single authoritative Depot Source-to-Release pipeline.

The workflow binds publication to the exact current `master` commit and performs restore, Release build, regression tests, packaging, release-channel validation, signing policy, production signing acceptance for Stable, final hashing, evidence generation and GitHub Release publication in one controlled path.

`scripts/release.ps1` is only a workflow dispatcher. It cannot build or publish an independent release.

See [Release Pipeline](../ReleasePipeline.md) and [Production Signing Acceptance](../ProductionSigningAcceptance.md).

## Source identity

The workflow records and validates:

- exact `github.sha` and Git ref;
- current `origin/master`;
- derived release tag;
- Depot and DepotManager versions;
- Core and feature schema versions;
- final executable hashes;
- production signing acceptance state for Stable.

Release publication and production-signed RC acceptance are blocked when the dispatched source is not current `master`.

## Release channels

### Preview

Preview releases retain the `preview` product-version suffix, use `<version>-preview` tags, publish with GitHub `prerelease=true`, may remain unsigned and are intentionally ignored by DepotManager's stable install/update discovery.

### Stable

Stable releases use exact numeric version tags, publish with GitHub `prerelease=false`, remove the preview suffix and require production Authenticode acceptance to report `PASS` before publication.

## Production Authenticode identity

Production signing uses:

- secret `DEPOT_SIGNING_PFX_BASE64`;
- secret `DEPOT_SIGNING_PASSWORD`;
- Actions variable `DEPOT_SIGNING_PUBLISHER_SUBJECT`.

Before Stable signing, the workflow verifies that the PFX contains a private key, matches the configured publisher subject, is currently valid and carries the Code Signing EKU `1.3.6.1.5.5.7.3.3`.

Both `Depot.exe` and `DepotManager.exe` are signed with SHA-256 and RFC 3161 timestamping and are verified with `signtool /pa /all /v`.

The PFX is materialized only in the ephemeral runner temporary directory and removed after use. No private signing key is stored in the repository or release evidence.

## Production signing acceptance

For Stable, `scripts/signing/Test-ProductionSigningAcceptance.ps1` additionally requires:

- Windows Authenticode status `Valid`;
- exact signer subject match to the configured production publisher;
- Code Signing EKU;
- a timestamp certificate for both executables;
- identical production signer thumbprint for both executables in the same candidate.

The resulting `ProductionSigningAcceptance.json` records the public signer and timestamp certificate evidence, source SHA and final executable hashes.

A Stable candidate cannot proceed to publication unless this acceptance reports `PASS`.

## Production-signed RC E2E

Stable candidates run Packaged E2E after real signing and timestamping. The acceptance covers clean install/update/repair, fail-closed artifact/manifest/signature handling and historical Core schema migration.

The same workflow supports an acceptance-only Stable run with `publish_release=false`, allowing production signing and RC evidence to be proven without creating a GitHub Release.

## Runtime publisher continuity

Stable DepotManager update paths enforce publisher continuity in addition to ordinary Windows trust.

The trusted signer subject of the running Stable DepotManager is the expected publisher for downloaded Depot and DepotManager artifacts. A differently named publisher is rejected even when Windows would otherwise trust that certificate.

This check is repeated before manager self-update replacement to protect against staged-file substitution.

Preview builds remain outside this production publisher-continuity contract.

## Certificate rotation and timestamping

Normal certificate renewal may change thumbprint, serial number or issuer while retaining the same publisher subject. This is supported because runtime continuity binds to the publisher identity rather than a single certificate thumbprint.

A publisher-subject change is intentionally fail-closed and requires an explicit trust-transition plan.

RFC 3161 timestamp evidence is mandatory for Stable so an already-signed artifact can remain verifiable after the signer certificate expires, provided the signature and timestamp remain valid and trusted. An expired certificate cannot be used for new release signing.

## Manifest and hashes

Each release contains a format-version-3 `Depot-<version>.manifest.json` recording:

- release channel and tag;
- source SHA;
- Depot and DepotManager versions;
- Core, Sales, Finance, User Sessions and Security Events schemas;
- manager command protocol;
- production signing acceptance state;
- Stable publisher subject;
- final executable names, sizes, SHA-256 hashes and signing state.

The legacy `databaseSchemaVersion` field remains the Core-schema compatibility field for DepotManager.

Hashes are calculated after signing and therefore describe the exact executable bytes retained for publication.

## Release evidence

The validated release candidate contains:

- `Depot-<version>.exe`;
- `DepotManager-<version>.exe`;
- `Depot-<version>.manifest.json`;
- `ReleaseEvidence.json`;
- `SHA256SUMS.txt`;
- `security-risk-acceptances.json`;
- `ProductionSigningAcceptance.json` for Stable.

The candidate is uploaded once as a GitHub Actions artifact. The publication job downloads that exact artifact and publishes it without rebuilding.

## Fail-closed conditions

Publication is blocked when:

- source identity is not current `master`;
- locked restore, Release build or regression tests fail;
- packaged channel/version validation fails;
- security risk-acceptance validation fails;
- Stable signing credentials or publisher identity are absent/inconsistent;
- the PFX is invalid, expired, lacks a private key or lacks Code Signing EKU;
- Authenticode signing, Windows trust or timestamp verification fails;
- production publisher identity does not match;
- production-signed packaged RC E2E fails;
- Stable production signing acceptance is not `PASS`;
- evidence/hash generation fails;
- a conflicting tag or release already exists;
- final channel/source verification fails.

An operator must not replace a failed workflow with a locally built, differently signed or manually published Stable release.
