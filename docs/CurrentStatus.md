# Current project status

Updated: 2026-09-16

Depot is on the `0.15.x-preview` development line. Finance, inventory, purchasing, sales, reporting, localization, notifications, Audit, persistent user sessions and operational security monitoring are integrated in the repository.

## Repository governance

Repository governance exposes five stable aggregate GitHub Actions checks intended for `master` protection:

- `CI Required Gate`;
- `Quality Required Gate`;
- `Security Required Gate`;
- `Packaged E2E Required Gate`;
- `Database Provider Required Gate`.

Each aggregate check fails unless every underlying workflow dependency succeeds. The packaged DepotManager E2E workflow runs its Smoke tier on every pull request targeting `master`, preventing required-check deadlocks caused by pull-request path filtering.

The target `master` policy requires pull requests, blocks force pushes and branch deletion, requires the five aggregate checks, requires zero external approvals for the current one-person project and does not require branches to be up to date before merging. The source-controlled ruleset template is `.github/rulesets/MasterGovernance.json`; repository-setting activation is documented in [Repository Governance](RepositoryGovernance.md).

## Release pipeline

`.github/workflows/release-integrity.yml` is the single authoritative Source-to-Release path.

The workflow performs locked restore, Release `-warnaserror` build, Depot and DepotManager regression tests, shared packaged publishing, channel validation, signing policy, manifest/hash/evidence generation and GitHub Release publication from the exact validated artifact.

Release channels are explicit:

- **Preview** uses `<version>-preview`, retains the preview product-version suffix and publishes with `prerelease=true`. Preview may remain unsigned.
- **Stable** uses the exact numeric version, removes the preview suffix and publishes with `prerelease=false`. Stable requires production signing acceptance to report `PASS` before publication.

`scripts/release.ps1` remains only a dispatcher for the authoritative workflow. It now also supports `-Channel Stable -AcceptanceOnly`, which executes the production-signed Stable RC path without creating a GitHub Release.

DepotManager installation/update/repair discovery remains a Stable-channel consumer and ignores `prerelease=true` releases.

See [Release Pipeline](ReleasePipeline.md), [Production Signing Acceptance](ProductionSigningAcceptance.md) and [Release Integrity](compliance/ReleaseIntegrity.md).

## Production signing acceptance

The technical H3 signing boundary is implemented:

- Stable requires the production PFX/password plus exact `DEPOT_SIGNING_PUBLISHER_SUBJECT`;
- the production certificate is preflight-checked for private key, validity period, publisher subject and Code Signing EKU;
- both `Depot.exe` and `DepotManager.exe` are SHA-256 Authenticode signed and RFC 3161 timestamped;
- Stable acceptance verifies Windows trust, exact publisher identity, Code Signing EKU, timestamp certificate evidence and one signer certificate for both executables;
- a production-signed packaged RC E2E runs before Stable evidence is finalized;
- Stable DepotManager update and self-update paths enforce publisher continuity against the trusted signer of the running Stable manager.

**Production acceptance remains BLOCKED until a real production-signed Stable acceptance-only run on current `master` completes successfully and retained evidence reports `PASS`.** Ephemeral Packaged-E2E certificates do not satisfy this gate.

## Database provider production status

Depot has a dedicated real-provider production acceptance matrix in `.github/workflows/database-provider-acceptance.yml`.

The following database baselines are technically **Supported** when shipped with a release whose full provider matrix is green:

- bundled SQLite runtime through `Microsoft.Data.Sqlite`;
- SQL Server 2022 / engine 16.x; CI certification uses SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

The full matrix validates fresh/idempotent provisioning, Core 29→30 and Sales 10→11 migrations, concurrent provisioning, SQL/type/constraint/date/decimal behavior, transactional rollback, concurrency/deadlock/retry behavior, Sales, Procurement, sessions, Finance GL/AR/AP/FIFO, Banking/reconciliation, Financial Reporting/snapshots, server restart, native remote backup/restore and representative 100k indexed access.

MariaDB and MySQL are independently certified; support for one never implies support for the other. Versions outside the listed baselines remain untested/best-effort until explicitly added to the matrix.

SQLite remains the embedded baseline but does not provide the full fixed `DECIMAL(28,9)` magnitude/precision semantics of the server providers because SQLite uses dynamic `NUMERIC` affinity. See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) for the exact boundary.

## Session and authentication security

Depot persists one session per successful login and derives online presence from heartbeat freshness. The shared session policy covers idle timeout, absolute maximum session age, concurrent-session mode/limit/action and ended-session history retention. Concurrent limits can reject a new login or supersede the oldest session under a database serialization lock.

Credential changes invalidate other open sessions with `CredentialsChanged`; account deactivation atomically revokes open sessions with `Revoked`. Administrative single/bulk termination remains permissioned through `UserSessions.Terminate` and is coupled to Audit/Security Event evidence in the production transaction path.

Production login throttling is database-shared across Depot clients. `AuthenticationSecurityPolicy` controls failure window, lockout threshold, lockout duration and Security Event retention. Local credentials are behind `IAuthenticationProvider` / `LocalAuthenticationProvider`, preserving a future OIDC/SSO boundary.

**Administration → User Sessions** exposes lifetime, concurrency and history-retention policy plus active/history views and termination controls. **Administration → Security Center** exposes review KPIs, filters, authentication-policy maintenance, event/session/client correlation and controlled response actions for terminating sessions or deactivating a resolved user.

A bounded maintenance service enforces ended-session history retention, Security Event retention and stale authentication-throttle cleanup in fixed-size batches. High/Critical event notifications are routed through a separate `SecurityAlertPolicy` boundary.

The security feature does not collect source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates or external-window activity.

## Versions

- Application: **0.15.172-preview**
- DepotManager: **0.1.23-preview**
- Core database schema: **30**
- Sales feature schema: **11**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- Help manifest: **1.21**

Every commit increments `DepotVersionPatch`.

Sales schema 11 restores/enforces the active inventory-reservation uniqueness invariant for every supported provider. This feature-schema correction does not change Core schema 30.

## Validation boundary

Release build with `-warnaserror`, repository regression suites, Security Supply Chain and Software Quality remain release gates. Database-provider support is additionally governed by the full real-provider acceptance workflow and its exact version matrix.

The five stable aggregate status checks are the intended repository-level merge contract for `master`; detailed matrix jobs remain implementation details behind those aggregates.

The release workflow now contains the complete technical production-signing acceptance path, but repository implementation is not itself evidence that the real production certificate has passed RC acceptance. That external evidence must exist before H3 can be marked production-accepted.

Provider support does not replace deployment-specific accounting/tax/legal, accessibility, OS/client, performance-sizing, backup-retention or organizational acceptance.

## Next steps

Remaining authentication roadmap items include MFA, OIDC/SSO/external identity providers, optional deployment-specific alert delivery/routing implementations, and explicit privacy/threat-model work before IP/geolocation/device-trust signals are considered.

For Track A, repository governance and the single release pipeline are implemented; the production-signing implementation is technically complete but production acceptance remains blocked pending a real signed RC run. The remaining implementation packages are production operations/disaster recovery and accessibility/manual desktop acceptance, together with the wider accounting/tax/localization and legal items tracked in [Release 1.0](Release1.0.md) and the [Roadmap](Roadmap.md).
