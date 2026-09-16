# Track A – Final Acceptance Closure

Updated: 2026-09-16

## Purpose

Track A implemented the repository, release, signing, recovery and desktop-acceptance controls required to move Depot toward a production-capable 1.0 release. The implementation packages are complete, but implementation is not the same as production acceptance.

This closure package defines one fail-closed evidence contract for the remaining administrative, production and manual acceptance work. It must never be used to turn missing external evidence into a repository-generated `PASS`.

Authoritative files:

- `operations/TrackAAcceptance.example.json` – non-production template/current closure state;
- `scripts/operations/Test-TrackAAcceptance.ps1` – closure-contract validator;
- `docs/RepositoryGovernance.md` – H1 governance activation requirements;
- `docs/ProductionSigningAcceptance.md` – H3 production signing acceptance;
- `docs/ProductionOperationsDisasterRecovery.md` – H4 deployment DR acceptance;
- `docs/AccessibilityProductionAcceptance.md` – H5 exact-RC desktop acceptance.

## Current closure state

| Package | Repository implementation | Production acceptance state |
| --- | --- | --- |
| H1 – Repository Governance & Required Gates | Implemented | `ADMIN_REQUIRED` – the source-controlled ruleset exists, but GitHub must have an active ruleset protecting `master` |
| H2 – Single Release Pipeline & Release Channels | Implemented | `PASS` at the repository implementation boundary |
| H3 – Production Signing & Release Candidate Acceptance | Implemented | `PRODUCTION_RC_REQUIRED` – requires a real production-signed Stable RC acceptance run |
| H4 – Production Operations & Disaster Recovery | Implemented | `DEPLOYMENT_REQUIRED` – requires an ACTIVE production deployment profile and real isolated restore evidence |
| H5 – Accessibility & Desktop Production Acceptance | Implemented | `MANUAL_REQUIRED` – requires human acceptance of the exact packaged RC |

Therefore Track A remains **BLOCKED for production closure** until H1 through H5 are all backed by concrete evidence and the closure validator succeeds with `-RequirePass`.

## Technical closure defects found after H5

The final closure review intentionally rechecked the required gates rather than assuming merged work was healthy. It found two repository defects that must be resolved before external acceptance begins:

1. SQLite migration-safety backup connections used normal pooling. A newly created backup could therefore retain a provider handle long enough for the immediate recovery drill to fail with a file-sharing violation. The closure fix disables pooling on the short-lived backup/validation connections and adds an explicit exclusive-open regression assertion before the restore drill.
2. Release identity resolution read `DepotVersionSuffix` from an XML element that carried an MSBuild `Condition` attribute. PowerShell converted that node to `System.Xml.XmlElement`, producing an invalid Preview tag such as `0.15.175-System.Xml.XmlElement`. The version suffix is now represented as an unconditional fixed repository value so release and CRA evidence readers receive the literal `preview` text.

The ordinary PR gates remain responsible for proving these fixes. A closure document must not override a red technical gate.

## H1 – Activate repository governance

The source-controlled policy is `.github/rulesets/MasterGovernance.json`. GitHub repository settings must contain an active ruleset that protects `master` with the intended pull-request restrictions and these aggregate required checks:

- `CI Required Gate`;
- `Quality Required Gate`;
- `Security Required Gate`;
- `Packaged E2E Required Gate`;
- `Database Provider Required Gate`.

Record the active GitHub ruleset ID or an equivalent immutable settings/API evidence reference, the verifier and verification timestamp. The JSON template in the repository alone cannot satisfy H1 production closure.

## H2 – Release-pipeline implementation

H2 is the only package that can currently be represented as repository-level `PASS` in the closure template. The authoritative release pipeline and explicit Preview/Stable channel contract are merged and are exercised by pull-request validation.

This status does not imply that a Stable production release has already been accepted; that evidence belongs to H3.

## H3 – Production-signed Stable release candidate

Run the authoritative acceptance-only path from a clean, current `master`:

```powershell
.\scripts\release.ps1 -Channel Stable -AcceptanceOnly
```

A valid H3 closure requires retained evidence from the exact candidate showing production Authenticode identity, SHA-256 signing, RFC 3161 timestamping, trusted publisher continuity and the production-signed packaged RC acceptance. Record the Stable version/tag and the retained production-signing evidence reference.

Do not store PFX data, signing passwords or other secrets in Track A evidence.

## H4 – Deployment disaster-recovery acceptance

Create a deployment-specific copy of `operations/DisasterRecoveryProfile.example.json` with `status=ACTIVE`, real ownership and the accepted RPO/RTO/retention/off-host requirements. Validate it without `-AllowTemplate`:

```powershell
.\scripts\operations\Test-DisasterRecoveryProfile.ps1 `
    -ProfilePath <active-profile.json> `
    -EvidencePath <profile-validation.json>
```

Then execute the real isolated restore drill for the intended production deployment and retain evidence that the restored environment is usable within the accepted RPO/RTO. Record both evidence references and the deployment identifier in the Track A closure evidence.

Credentials, database secrets and production data must not be copied into repository evidence.

## H5 – Exact-RC desktop accessibility acceptance

Populate a deployment/test copy of `operations/AccessibilityAcceptance.example.json` from the exact Stable RC used for closure. Complete the documented keyboard-only, focus/no-trap, Narrator, Accessibility Insights and 100/125/150/200% DPI matrix.

Then validate the actual evidence:

```powershell
.\scripts\operations\Test-AccessibilityAcceptance.ps1 `
    -Path <accessibility-evidence.json> `
    -RequirePass
```

Record the retained evidence reference in the Track A closure evidence. A technical CI accessibility pass is necessary but is not a substitute for this manual acceptance.

## Final closure

Create a controlled copy of `operations/TrackAAcceptance.example.json` and replace the placeholder states/references with concrete evidence. H1 through H5 must all be `PASS`; `knownBlockingIssues` must be empty; the exact Stable source SHA/version and acceptance owner/timestamp must be recorded.

Validate it with:

```powershell
.\scripts\operations\Test-TrackAAcceptance.ps1 `
    -Path <track-a-acceptance.json> `
    -RequirePass `
    -EvidencePath <track-a-validation.json>
```

A successful command proves that the evidence contract is complete. It does not independently prove that the referenced human, GitHub, signing or deployment evidence is truthful; evidence review remains an explicit release responsibility.

## Release boundary

Track A closure is one prerequisite for Depot 1.0, not the entire 1.0 acceptance decision. Accounting/tax/localization procedures, customer-specific sizing, remaining electronic-invoice scenarios and qualified GDPR/CRA/legal review remain separately tracked in `docs/Release1.0.md` and `docs/Roadmap.md`.
