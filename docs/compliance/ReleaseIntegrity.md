# Depot Release Integrity

## Authoritative release source

`.github/workflows/release-integrity.yml` is the single authoritative Depot Source-to-Release pipeline.

The workflow binds publication to the exact current `master` commit and performs restore, Release build, regression tests, packaging, release-channel validation, signing policy, final hashing, evidence generation and GitHub Release publication in one controlled path.

`scripts/release.ps1` is only a workflow dispatcher. It cannot build or publish an independent release.

See [Release Pipeline](../ReleasePipeline.md) for the operator procedure and complete channel contract.

## Source identity

The workflow records and validates:

- exact `github.sha`;
- exact Git ref;
- current `origin/master`;
- derived release tag;
- Depot and DepotManager versions;
- Core and feature schema versions.

Release publication is blocked when the dispatched source is not the current `master`.

The GitHub Release is created with the validated source SHA as its target. `ReleaseEvidence.json` and the release manifest retain that SHA.

## Release channels

### Preview

Preview releases:

- retain the `preview` product-version suffix;
- use `<version>-preview` tags;
- are published with GitHub `prerelease=true`;
- may remain unsigned before production signing is available;
- are intentionally ignored by DepotManager's stable install/update discovery.

### Stable

Stable releases:

- use exact numeric version tags;
- are published with GitHub `prerelease=false`;
- remove the preview version suffix;
- require production Authenticode credentials;
- fail closed before publication when signing credentials are missing or incomplete.

## Authenticode

Signing credentials are supplied only through GitHub secrets:

- `DEPOT_SIGNING_PFX_BASE64`;
- `DEPOT_SIGNING_PASSWORD`.

When signing is active, both `Depot.exe` and `DepotManager.exe` are signed with SHA-256 and RFC 3161 timestamping and immediately verified with `signtool`.

The certificate file is materialized only in the ephemeral runner temp directory and removed after use.

The release pipeline's Stable policy requires signing, but actual production-signing acceptance remains blocked until the production publisher identity and timestamp chain are accepted under Track A H3.

## Package identity

`scripts/publish-packaged-artifacts.ps1` is the shared packaging implementation for release and Packaged E2E validation.

It supports explicit `Preview` and `Stable` channels. Stable only changes the established MSBuild stable-release properties; package layout and executable generation remain shared.

There is no separate local release build implementation.

## Manifest and hashes

Each published release contains a format-version-2 manifest:

`Depot-<version>.manifest.json`

The manifest records:

- release channel;
- release tag;
- source SHA;
- Depot version;
- DepotManager version;
- Core database schema;
- Sales schema;
- Finance schema;
- User Sessions schema;
- Security Events schema;
- manager command protocol;
- final executable names, sizes and SHA-256 hashes;
- Authenticode signing state.

The legacy `databaseSchemaVersion` field remains the Core-schema compatibility field for DepotManager.

Hashes are calculated after signing and therefore describe the exact executable bytes that are uploaded.

## Release evidence

The validated release candidate contains:

- `Depot-<version>.exe`;
- `DepotManager-<version>.exe`;
- `Depot-<version>.manifest.json`;
- `ReleaseEvidence.json`;
- `SHA256SUMS.txt`;
- `security-risk-acceptances.json`.

The candidate is uploaded once as a GitHub Actions artifact. The publication job downloads that exact artifact and publishes it without rebuilding.

Regression test results are retained separately as workflow evidence.

## Fail-closed conditions

Publication is blocked when:

- source identity is not current `master`;
- locked restore fails;
- Release build emits warnings/errors;
- regression tests fail;
- packaged channel/version validation fails;
- security risk-acceptance validation fails;
- Stable signing credentials are absent;
- signing or signature verification fails;
- evidence/hash generation fails;
- a conflicting tag or release already exists;
- final channel/source verification fails.

An operator must not replace a failed workflow with a locally built or manually published Stable release.
