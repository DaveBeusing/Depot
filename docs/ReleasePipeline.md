# Release Pipeline

## Authority

`.github/workflows/release-integrity.yml` is the single authoritative Source-to-Release pipeline for Depot.

A published release must follow:

```text
current master source
→ locked restore
→ Release build with warnings as errors
→ Depot and DepotManager regression tests
→ shared packaged-artifact publish
→ release-channel validation
→ signing policy
→ manifest and SHA-256 evidence
→ immutable workflow artifact
→ GitHub Release
```

`scripts/release.ps1` is only a dispatcher for this workflow. It does not build, sign, stage, tag or publish release artifacts locally.

Manual GitHub Release creation, locally built release assets and ad-hoc tag publication are not authoritative Depot release procedures.

## Release channels

Depot has two explicit release channels.

### Preview

Preview is the normal pre-1.0 engineering channel.

- tag: `<DepotVersion>-preview`, for example `0.15.171-preview`;
- Depot and DepotManager retain their configured `preview` version suffix;
- GitHub Release is published with `prerelease=true`;
- Authenticode is optional while no production signing identity is available;
- if signing credentials are configured, the same signing and verification path is used;
- DepotManager production install/update discovery intentionally ignores Preview releases.

An unsigned Preview is permitted only because its channel is explicitly non-stable. Its release evidence records `authenticodeSigned=false`.

### Stable

Stable is the production release channel.

- tag: exact numeric Depot version, for example `1.0.0`;
- preview suffixes are removed from packaged product versions;
- GitHub Release is published with `prerelease=false`;
- Authenticode credentials are mandatory;
- both `Depot.exe` and `DepotManager.exe` must sign and verify successfully;
- missing or incomplete signing credentials fail closed before a GitHub Release can be created.

Production signing acceptance remains a separate Track A gate until a real production publisher identity and timestamp chain are available.

## Source identity

Release publication is permitted only through `workflow_dispatch` on the current `master`.

The workflow verifies:

1. checked-out `HEAD` equals `github.sha`;
2. the workflow ref is `refs/heads/master`;
3. `github.sha` equals the current `origin/master`;
4. the resolved release tag does not already exist;
5. the GitHub Release does not already exist.

The GitHub Release is created with `--target <github.sha>`. `ReleaseEvidence.json` and the release manifest retain the same source SHA.

## Version and tag contract

The release tag is derived by the workflow. It is not supplied independently by the operator.

Given Depot version `X.Y.Z`:

```text
Preview → X.Y.Z-preview
Stable  → X.Y.Z
```

This prevents tag/version drift.

DepotManager keeps its independent version from `src/DepotManager/DepotManager.Version.props`. The release manifest records both versions.

## Shared package path

`scripts/publish-packaged-artifacts.ps1` is the shared package builder used by release validation and packaged E2E.

Its `Channel` parameter controls version suffix behavior:

```powershell
-Channel Preview
-Channel Stable
```

Preview is the default so existing packaged E2E validation continues to exercise development artifacts. Stable adds only the established `DepotStableRelease` and `DepotManagerStableRelease` MSBuild properties; it does not introduce a second publish implementation.

## Release manifest

Each release contains `Depot-<version>.manifest.json`.

Format version 2 records:

- release channel;
- release tag;
- exact source SHA;
- Depot version;
- DepotManager version;
- Core database schema;
- Sales feature schema;
- Finance feature schema;
- User Sessions feature schema;
- Security Events feature schema;
- manager command protocol;
- final executable file names;
- final executable sizes;
- SHA-256 hashes;
- whether Authenticode signing was applied.

`databaseSchemaVersion` remains present as the Core-schema compatibility field consumed by the current DepotManager stable update path.

## Integrity and evidence

The workflow creates:

- `Depot-<version>.exe`;
- `DepotManager-<version>.exe`;
- `Depot-<version>.manifest.json`;
- `ReleaseEvidence.json`;
- `SHA256SUMS.txt`;
- `security-risk-acceptances.json`.

Hashes are calculated after signing so they describe the exact bytes uploaded to the GitHub Release.

The same files are first retained as a GitHub Actions artifact. The publish job downloads that exact artifact and uploads it to the GitHub Release; it never rebuilds after validation.

## DepotManager discovery boundary

DepotManager installation, update, repair metadata and self-update discovery remain Stable-channel consumers.

They reject or skip GitHub releases with `prerelease=true`. This is deliberate: a Preview release cannot accidentally enter the production update path merely because it has a higher numeric version.

Preview-channel installation is not introduced by this hardening package.

## Operator usage

From a clean, current local `master`:

```powershell
.\scripts\release.ps1 -Channel Preview
.\scripts\release.ps1 -Channel Stable
```

The script verifies that local `master` exactly matches `origin/master`, then dispatches the authoritative GitHub Actions workflow.

The same workflow can be dispatched directly from the GitHub Actions UI on `master`.

## Failure policy

A release is not published when any of the following occurs:

- source is not current `master`;
- restore, build or tests fail;
- packaged version/channel validation fails;
- security risk-acceptance validation fails;
- signing configuration is incomplete;
- Stable has no signing credentials;
- Authenticode signing or verification fails;
- manifest/evidence generation fails;
- the tag or release already exists;
- final GitHub Release publication fails.

No failure is converted into a Stable release by a fallback local process.
