# Release Pipeline

Updated: 2026-09-18

## Authority

`.github/workflows/release-integrity.yml` is the single authoritative Source-to-Release pipeline for Depot.

A published release follows:

```text
current master source
→ locked restore
→ Release build with warnings as errors
→ Depot and DepotManager regression tests
→ shared packaged-artifact publish
→ release-channel validation
→ signing policy
→ production signing acceptance for Stable
→ manifest and SHA-256 evidence
→ immutable workflow artifact
→ GitHub Release
```

`scripts/release.ps1` is only a dispatcher for this workflow. It does not build, sign, stage, tag or publish release artifacts locally.

Manual GitHub Release creation, locally built release assets and ad-hoc tag publication are not authoritative Depot release procedures.

## Pull request validation versus authoritative runs

Pull requests validate release/package/signing/evidence behavior without re-running the complete Depot and DepotManager regression suites a second time after the required CI shards. This keeps PR validation bounded while preserving the release-contract checks needed before merge.

Authoritative `workflow_dispatch` Preview, Stable and Stable `-AcceptanceOnly` runs execute the full Depot and DepotManager regression suites in the release-candidate job. Those full suites remain mandatory before release evidence is finalized or a Stable candidate can be accepted/published.

## Release channels

Depot has two explicit release channels.

### Preview

Preview is the normal pre-1.0 engineering channel.

- tag: `<DepotVersion>-preview`;
- Depot and DepotManager retain their configured `preview` version suffix;
- GitHub Release is published with `prerelease=true`;
- Authenticode is optional while no production signing identity is available;
- DepotManager production install/update discovery intentionally ignores Preview releases.

An unsigned Preview is permitted only because its channel is explicitly non-stable. Its release evidence records `authenticodeSigned=false` and `productionSigningAcceptance=NOT_APPLICABLE`.

### Stable

Stable is the production release channel.

- tag: exact numeric Depot version;
- preview suffixes are removed from packaged product versions;
- GitHub Release is published with `prerelease=false`;
- production Authenticode credentials are mandatory;
- both `Depot.exe` and `DepotManager.exe` must sign and verify successfully;
- the signing certificate must match `DEPOT_SIGNING_PUBLISHER_SUBJECT`;
- Code Signing EKU and certificate validity are checked before signing;
- both executables require a trusted RFC 3161 timestamp;
- production signing acceptance must report `PASS` before Stable publication.

Missing or incomplete production signing configuration fails closed.

## Stable release-candidate acceptance

The Stable path can be run without publication:

```powershell
.\scripts\release.ps1 -Channel Stable -AcceptanceOnly
```

This dispatches the same release workflow with `publish_release=false`. The workflow still performs production signing, publisher verification, timestamp verification, release-candidate evidence generation and packaged RC E2E, but it does not create a Git tag or GitHub Release.

This is the required production-signing acceptance path before claiming Stable readiness. See [Production Signing Acceptance](ProductionSigningAcceptance.md).

## Source identity

Release publication and production-signed RC acceptance are permitted only through `workflow_dispatch` on current `master`.

The workflow verifies:

1. checked-out `HEAD` equals `github.sha`;
2. the workflow ref is `refs/heads/master`;
3. `github.sha` equals current `origin/master`;
4. for publication, the resolved release tag and GitHub Release do not already exist.

The GitHub Release is created with `--target <github.sha>`. `ReleaseEvidence.json`, production signing acceptance evidence and the release manifest retain the same source SHA.

## Version and tag contract

The release tag is derived by the workflow. It is not supplied independently by the operator.

Given Depot version `X.Y.Z`:

```text
Preview → X.Y.Z-preview
Stable  → X.Y.Z
```

DepotManager keeps its independent version from `src/DepotManager/DepotManager.Version.props`. The release manifest records both versions.

## Shared package path

`scripts/publish-packaged-artifacts.ps1` remains the shared package builder used by release validation and packaged E2E.

Its `Channel` parameter controls version suffix behavior:

```powershell
-Channel Preview
-Channel Stable
```

There is no second production package implementation.

## Production signing configuration

Stable requires:

- secret `DEPOT_SIGNING_PFX_BASE64`;
- secret `DEPOT_SIGNING_PASSWORD`;
- Actions variable `DEPOT_SIGNING_PUBLISHER_SUBJECT`.

The publisher variable is intentionally not secret. It identifies the public code-signing identity that the workflow expects.

The PFX is materialized only in the ephemeral runner temporary directory and removed after use. The private key is never stored in the repository or release artifacts.

## Production-signed packaged RC E2E

After Stable signing and timestamp validation, the workflow builds a previous Stable package baseline from the first parent of current `master` and runs packaged acceptance against the real signed current candidate.

The RC acceptance covers:

- clean install, normal update and repair while preserving provisioned data;
- fail-closed artifact, manifest and signature behavior;
- historical Core schema 29 → 30 migration after verified SQLite safety backup.

These tests run before release evidence is finalized and before any Stable publication job can start.

## Release manifest

Each release contains `Depot-<version>.manifest.json`.

Format version 3 records:

- release channel and tag;
- exact source SHA;
- Depot and DepotManager versions;
- Core, Sales, Finance, User Sessions and Security Events schema versions;
- manager command protocol;
- production signing acceptance state;
- production publisher subject for Stable;
- final executable file names, sizes and SHA-256 hashes;
- whether Authenticode signing was applied.

`databaseSchemaVersion` remains present as the Core-schema compatibility field consumed by the current DepotManager stable update path.

## Integrity and evidence

The workflow creates:

- `Depot-<version>.exe`;
- `DepotManager-<version>.exe`;
- `Depot-<version>.manifest.json`;
- `ReleaseEvidence.json`;
- `SHA256SUMS.txt`;
- `security-risk-acceptances.json`;
- `ProductionSigningAcceptance.json` for Stable.

Hashes are calculated after signing so they describe the exact executable bytes retained for publication.

The same files are first retained as a GitHub Actions artifact. The publish job downloads that exact artifact and uploads it to the GitHub Release; it never rebuilds after validation.

## DepotManager publisher boundary

DepotManager installation, update, repair metadata and self-update discovery remain Stable-channel consumers and ignore `prerelease=true` releases.

A Stable DepotManager also enforces publisher continuity. The running manager's trusted Authenticode signer subject becomes the expected publisher for downloaded Depot and DepotManager update artifacts. A different otherwise trusted publisher is rejected.

Preview builds do not assert this production publisher boundary.

## Operator usage

From a clean, current local `master`:

```powershell
.\scripts\release.ps1 -Channel Preview
.\scripts\release.ps1 -Channel Stable -AcceptanceOnly
.\scripts\release.ps1 -Channel Stable
```

The script verifies that local `master` exactly matches `origin/master`, then dispatches the authoritative GitHub Actions workflow.

## Failure policy

A release is not published when any of the following occurs:

- source is not current `master`;
- restore, build or tests fail;
- packaged version/channel validation fails;
- security risk-acceptance validation fails;
- Stable production signing configuration is missing or inconsistent;
- the configured publisher does not match the production PFX;
- Code Signing EKU or certificate validity checks fail;
- Authenticode signing, Windows trust verification or timestamp verification fails;
- production-signed packaged RC E2E fails;
- production signing acceptance is not `PASS` for Stable;
- manifest/evidence generation fails;
- the tag or release already exists;
- final GitHub Release publication fails.

No failure is converted into a Stable release by a fallback local process.
