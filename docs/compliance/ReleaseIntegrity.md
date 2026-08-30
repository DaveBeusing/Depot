# Depot Release Integrity

## Release source

The release-integrity workflow checks out the exact workflow commit and records both the Git commit SHA and Git ref in the release evidence. Tagged `v*` runs are therefore bound to a specific source revision.

## Authenticode

Tagged releases require Authenticode credentials supplied only through GitHub repository/environment secrets:

- `DEPOT_SIGNING_PFX_BASE64`
- `DEPOT_SIGNING_PASSWORD`

The certificate file is materialized only into the ephemeral runner temp directory, used to sign `Depot.exe`, verified with `signtool`, and deleted in a `finally` block. No signing key or password is stored in the repository.

The signing step uses SHA-256 and RFC 3161 timestamping. The certificate itself must be obtained and managed through the chosen code-signing provider. For higher-assurance production use, prefer a hardware- or cloud-backed signing service instead of a long-lived exportable PFX when available.

## Integrity manifest

Every release-integrity run generates:

- `source.txt` — commit SHA and ref;
- `SHA256SUMS.txt` — SHA-256 hashes for published release files;
- the `win-x64` published output.

These files are uploaded as a single GitHub Actions artifact. A production release process should publish the same verified artifact rather than rebuilding source separately after approval.

## Signing gate

Pull-request and manually dispatched validation runs may execute without signing credentials. A tagged `v*` release fails if signing credentials are not configured. This prevents an unsigned tagged production artifact from silently passing the release-integrity workflow.
