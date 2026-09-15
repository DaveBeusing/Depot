# Production Signing Acceptance

Updated: 2026-09-16

## Current status

**Production acceptance: BLOCKED**

The repository now contains the technical production-signing acceptance path, publisher-continuity enforcement and release-candidate evidence model. A production signing acceptance must not be reported as PASS until a Stable acceptance-only run on current `master` completes with the real production code-signing identity.

No retained successful production-signed RC acceptance evidence is recorded in the repository at this point. Test or ephemeral certificates do not satisfy this gate.

## Required production configuration

The authoritative release workflow requires all of the following for Stable:

- GitHub secret `DEPOT_SIGNING_PFX_BASE64` containing the production code-signing certificate and private key as base64-encoded PFX;
- GitHub secret `DEPOT_SIGNING_PASSWORD`;
- GitHub Actions variable `DEPOT_SIGNING_PUBLISHER_SUBJECT` containing the exact `SignerCertificate.Subject` expected for the production publisher.

The private key must never be committed to the repository.

Before Stable packaging proceeds, the workflow verifies that the PFX:

- contains a private key;
- matches the configured publisher subject exactly;
- is currently within its certificate validity period;
- contains the Code Signing EKU `1.3.6.1.5.5.7.3.3`.

## Signed release-candidate acceptance

Run from a clean local `master` that exactly matches `origin/master`:

```powershell
.\scripts\release.ps1 -Channel Stable -AcceptanceOnly
```

This dispatches the same authoritative release pipeline used for Stable publication, but sets `publish_release=false`.

The candidate is therefore built from current `master`, tested, packaged as Stable, signed and timestamped exactly through the production release path without creating a Git tag or GitHub Release.

The acceptance run must complete all of the following:

1. locked restore and Release build with `-warnaserror`;
2. Depot and DepotManager regression suites;
3. Stable single-file package generation;
4. SHA-256 Authenticode signing of both `Depot.exe` and `DepotManager.exe`;
5. RFC 3161 timestamping through the configured timestamp service;
6. Windows Authenticode trust verification;
7. exact production publisher-subject verification;
8. Code Signing EKU verification;
9. identical signer-certificate thumbprint for both executables in the same candidate;
10. timestamp-certificate evidence for both executables;
11. packaged release-candidate E2E covering clean install/update/repair, fail-closed artifact/manifest validation and historical schema migration;
12. immutable hashes and source-SHA evidence.

A Stable GitHub Release is publishable only when the same workflow reports `productionSigningAcceptance=PASS`.

## Evidence

A successful Stable acceptance candidate produces:

- `ProductionSigningAcceptance.json`;
- `ReleaseEvidence.json`;
- `Depot-<version>.manifest.json`;
- `SHA256SUMS.txt`;
- release test TRX files.

`ProductionSigningAcceptance.json` records:

- source commit SHA;
- release tag candidate;
- configured publisher subject;
- signer subject, issuer and certificate thumbprint;
- signer certificate validity window;
- Code Signing EKU result;
- timestamp certificate subject, issuer, thumbprint and validity window;
- final file size and SHA-256 for both executables;
- acceptance status.

A report produced by an ephemeral Packaged-E2E certificate is not production evidence.

## Runtime publisher continuity

Stable DepotManager builds enforce publisher continuity during production update paths.

For a Stable DepotManager:

1. the running DepotManager executable must itself have a valid trusted Authenticode signature;
2. its signer certificate subject becomes the expected publisher identity for the update operation;
3. a downloaded `Depot.exe` or `DepotManager.exe` must have a valid trusted Code Signing signature;
4. the candidate signer subject must match the running DepotManager signer subject exactly.

This prevents an executable signed by a different otherwise trusted publisher from being accepted as a Depot update.

Preview builds intentionally do not enforce production publisher continuity. Production release discovery continues to ignore GitHub prereleases.

## Certificate renewal and expiry

Normal certificate renewal should retain the same publisher subject. In that case a replacement trusted code-signing certificate is accepted after the workflow validates the new certificate and the Stable candidate is signed with the same publisher identity.

Every Stable signature must carry an RFC 3161 timestamp. Windows Authenticode validation can therefore continue to validate an already-signed artifact after the signer certificate expires, provided the signature and timestamp remain valid and trusted.

New releases cannot be signed with an expired certificate. The production signing credential must be replaced before the next Stable candidate is generated.

The acceptance evidence records the signer and timestamp certificate validity windows so expiry can be monitored operationally.

## Publisher identity change

A certificate replacement that changes only certificate serial number, thumbprint or issuer but retains the same publisher subject is a normal rotation.

A legal publisher-subject change is intentionally not a silent auto-update path. Existing Stable DepotManager installations will reject a differently named publisher. Such a change requires an explicit trust-transition plan and, where necessary, a manually distributed trusted DepotManager transition package.

This fail-closed boundary is deliberate.

## Recovery

If production signing fails or the certificate is unavailable:

- Stable publication remains blocked;
- Preview engineering builds may continue according to the Preview policy;
- do not substitute an ephemeral or self-signed certificate for production acceptance;
- do not weaken DepotManager publisher validation;
- restore or rotate the production signing credential and rerun the acceptance-only Stable candidate.

If timestamping fails, the Stable candidate is rejected and must be regenerated after the timestamp service is available or an approved timestamp provider has been configured.
