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

`scripts/release.ps1` remains only a dispatcher for the authoritative workflow. It also supports `-Channel Stable -AcceptanceOnly`, which executes the production-signed Stable RC path without creating a GitHub Release.

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

## Production operations and disaster recovery

The H4 technical operating boundary is implemented around the existing provider-native recovery capabilities rather than adding a second backup engine.

- `operations/DisasterRecoveryProfile.example.json` defines the deployment DR contract.
- `scripts/operations/Test-DisasterRecoveryProfile.ps1` requires explicit RPO, RTO, retention, off-host copies, encryption, monitoring, restore-drill age and named ownership.
- CI validates the DR-profile contract as part of `CI Required Gate`.
- SQLite recovery acceptance restores a verified backup into an isolated target, compares SHA-256, runs `PRAGMA integrity_check` and validates Depot schema metadata.
- SQL Server, MariaDB and MySQL run their provider-native backup/restore drill on pull requests as part of `Database Provider Required Gate`.
- provider recovery drills retain structured JSON evidence without database identity or credentials.
- DepotManager support packages include `RecoveryReadiness.json`; log-collection ACL/I/O failures no longer prevent support-package creation and are represented with sanitized recovery guidance.

The authoritative operating model and provider runbooks are in [Production Operations & Disaster Recovery](ProductionOperationsDisasterRecovery.md).

**A specific deployment is not production-DR-accepted merely because repository CI is green. It remains operationally BLOCKED until an ACTIVE deployment DR profile is validated and its real backup infrastructure has passed an isolated restore drill within the accepted RPO/RTO.**

## Accessibility and desktop production acceptance

The H5 technical desktop-accessibility boundary is implemented without treating automation as a substitute for human desktop acceptance.

- `DesktopAccessibilityRuntime` supplies a shared visible keyboard-focus fallback when a focusable WPF control resolves a null `FocusVisualStyle`, including legacy shared styles.
- the accessibility static gate now detects both direct and Setter-based focus suppression and rejects unsafe suppression outside controlled shared resources.
- cyclic Tab-navigation declarations are rejected by the quality gate.
- `TextInput` and `PasswordInput` forward UI Automation labels, required-field state and related metadata to the native inner keyboard focus target.
- Login and first-run administrator inputs expose explicit label and required-field semantics.
- `OperationStatus` and `ConnectionStatusIndicator` raise UI Automation notifications for meaningful dynamic status/error changes.
- standard file/message dialog flows capture and restore keyboard focus.
- Depot and DepotManager explicitly declare Per-Monitor-V2 DPI awareness.
- `Accessibility technical baseline` retains `TechnicalAccessibilityEvidence.json` as part of `Quality Required Gate`.
- `operations/AccessibilityAcceptance.example.json` and `scripts/operations/Test-AccessibilityAcceptance.ps1` define the exact-RC manual acceptance/evidence contract.

The procedure is documented in [Accessibility & Desktop Production Acceptance](AccessibilityProductionAcceptance.md) and [Desktop Accessibility Baseline](compliance/Accessibility.md).

**Desktop production accessibility acceptance remains `MANUAL_REQUIRED` until the exact packaged release candidate has passed keyboard-only, focus/no-trap, Narrator, Accessibility Insights and 100/125/150/200% DPI acceptance and retained evidence passes `Test-AccessibilityAcceptance.ps1 -RequirePass`.**

## Database provider production status

Depot has a dedicated real-provider production acceptance matrix in `.github/workflows/database-provider-acceptance.yml`.

The following database baselines are technically **Supported** when shipped with a release whose full provider matrix is green:

- bundled SQLite runtime through `Microsoft.Data.Sqlite`;
- SQL Server 2022 / engine 16.x; CI certification uses SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

The matrix validates fresh/idempotent provisioning, Core 29→30 and Sales 10→11 migrations, concurrent provisioning, SQL/type/constraint/date/decimal behavior, transactional rollback, concurrency/deadlock/retry behavior, Sales, Procurement, sessions, Finance GL/AR/AP/FIFO, Banking/reconciliation, Financial Reporting/snapshots, server restart, native remote backup/restore and representative 100k indexed access. Pull requests include provider recovery drills and retained recovery evidence for every supported provider family.

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

- Application: **0.15.175-preview**
- DepotManager: **0.1.23-preview**
- Core database schema: **30**
- Sales feature schema: **11**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- Help manifest: **1.21**

Every commit increments `DepotVersionPatch`.

## Validation boundary

Release build with `-warnaserror`, repository regression suites, Security Supply Chain and Software Quality remain release gates. Database-provider support is additionally governed by the real-provider acceptance workflow and its exact version matrix, including restore-drill evidence in the required pull-request gate.

The five stable aggregate status checks remain the intended repository-level merge contract for `master`; detailed matrix jobs remain implementation details behind those aggregates.

Repository implementation does not itself prove the external/manual acceptance gates: production signing still requires a real signed RC, real deployments require their own DR profile/restore drill, and desktop accessibility requires exact-RC manual evidence.

Provider support does not replace deployment-specific accounting/tax/legal, accessibility, OS/client, performance-sizing, backup-retention or organizational acceptance.

## Next steps

All five Track A implementation packages now have repository implementations. Track A closure is therefore an acceptance/administration phase rather than another implementation package:

- activate the source-controlled `master` repository ruleset in GitHub;
- complete a real production-signed Stable RC acceptance run;
- complete deployment-specific DR profile/restore-drill acceptance where production deployment is intended;
- complete and retain exact-RC desktop accessibility acceptance evidence;
- close remaining wider accounting/tax/localization/legal and release-readiness items tracked in [Release 1.0](Release1.0.md) and the [Roadmap](Roadmap.md).
