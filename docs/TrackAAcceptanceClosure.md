# Track A – Final Acceptance Closure

Updated: 2026-09-20

## Purpose

Track A implemented the repository, release, signing, recovery and desktop-acceptance controls required to move Depot toward a production-capable 1.0 release. The implementation packages are complete, but implementation is not the same as production acceptance.

This closure package defines one fail-closed evidence contract for the remaining administrative, production and manual acceptance work. It must never be used to turn missing external evidence into a repository-generated `PASS`.

Authoritative files:

- `docs/operations/TrackAAcceptance.example.json` – controlled current closure-state template;
- `scripts/operations/Test-TrackAAcceptance.ps1` – closure-contract validator;
- `scripts/operations/Test-RepositoryGovernance.ps1` – H1 source/live governance validator;
- `docs/RepositoryGovernance.md` – H1 governance requirements and live closure;
- `docs/ProductionSigningAcceptance.md` – H3 production signing acceptance;
- `docs/ProductionOperationsDisasterRecovery.md` – H4 deployment DR acceptance;
- `docs/AccessibilityProductionAcceptance.md` – H5 exact-RC desktop acceptance.

## Current closure state

| Package | Repository implementation | Production acceptance state |
| --- | --- | --- |
| H1 – Repository Governance & Required Gates | Implemented | `BLOCKED` – active ruleset `23590604` still protects pull-request delivery plus deletion/non-fast-forward updates, but the five aggregate required-status-check bindings must be restored and revalidated |
| H2 – Single Release Pipeline & Release Channels | Implemented | `PASS` at the repository implementation boundary |
| H3 – Production Signing & Release Candidate Acceptance | Implemented | `PRODUCTION_RC_REQUIRED` – requires a real production-signed Stable RC acceptance run |
| H4 – Production Operations & Disaster Recovery | Implemented | `DEPLOYMENT_REQUIRED` – requires an ACTIVE production deployment profile and real isolated restore evidence |
| H5 – Accessibility & Desktop Production Acceptance | Implemented | `MANUAL_REQUIRED` – requires human acceptance of the exact packaged RC |

Track A therefore remains **BLOCKED for production closure**. H2 remains closed; H1 is reopened until live ruleset `23590604` again enforces the five aggregate required checks. H3 through H5 also still require their concrete evidence before the closure validator can succeed with `-RequirePass`.

## Technical closure defects found after H5

The final closure review intentionally rechecked the required gates rather than assuming merged work was healthy. It found two repository defects that had to be resolved before external acceptance begins:

1. SQLite migration-safety backup connections used normal pooling. A newly created backup could therefore retain a provider handle long enough for the immediate recovery drill to fail with a file-sharing violation. The closure fix disables pooling on the short-lived backup/validation connections and adds an explicit exclusive-open regression assertion before the restore drill.
2. Release identity resolution read `DepotVersionSuffix` from an XML element that carried an MSBuild `Condition` attribute. PowerShell converted that node to `System.Xml.XmlElement`, producing an invalid Preview tag. The version suffix is now represented as an unconditional fixed repository value so release and CRA evidence readers receive the literal `preview` text.

The ordinary PR gates remain responsible for proving these fixes. A closure document must not override a red technical gate.

## H1 – Repository governance: BLOCKED by live drift

The source-controlled policy is `.github/rulesets/MasterGovernance.json`. CI validates the template and all five required aggregate workflow job names through `scripts/operations/Test-RepositoryGovernance.ps1`.

The required checks are:

- `CI Required Gate`;
- `Quality Required Gate`;
- `Security Required Gate`;
- `Packaged E2E Required Gate`;
- `Database Provider Required Gate`.

GitHub now exposes active repository ruleset **23590604**, named `Depot master governance`. The live ruleset targets exactly `refs/heads/master`, requires pull-request delivery, requires the five aggregate checks above, blocks deletion and non-fast-forward updates, has no bypass actor, requires zero approvals for the current one-person delivery model and keeps strict branch-up-to-date enforcement disabled.

The live ruleset metadata was verified through the GitHub rulesets API on 2026-09-17 and was valid H1 `PASS` evidence at that time. A later temporary development-phase relaxation removed the live required-status-check rule. The ruleset remains active, targets `master`, requires PR delivery and blocks deletion/non-fast-forward updates, but H1 is now `BLOCKED` until the five aggregate checks are restored and the live validator passes again.

The authoritative live validator remains:

```powershell
.\scripts\operations\Test-RepositoryGovernance.ps1 `
    -Repository DaveBeusing/Depot `
    -RequireActiveRuleset `
    -EvidencePath <h1-governance-evidence.json>
```

Future drift must fail closed. Removing or weakening the live ruleset reopens H1 even if the source-controlled JSON template remains unchanged.

## H2 – Release-pipeline implementation

H2 is repository-level `PASS`. The authoritative release pipeline and explicit Preview/Stable channel contract are merged and exercised by pull-request validation.

This status does not imply that a Stable production release has already been accepted; that evidence belongs to H3.

## H3 – Production-signed Stable release candidate

Run the authoritative acceptance-only path from a clean, current `master`:

```powershell
.\scripts\release.ps1 -Channel Stable -AcceptanceOnly
```

`-AcceptanceOnly` dispatches `release-integrity.yml` with `channel=Stable` and `publish_release=false`. The workflow still performs the full production-signing and packaged-RC acceptance path, but the `publish-release` job is not eligible to run, so no Git tag or GitHub Release is created by the acceptance-only request.

A valid H3 closure requires retained evidence from the exact candidate showing production Authenticode identity, SHA-256 signing, RFC 3161 timestamping, trusted publisher continuity and the production-signed packaged RC acceptance. The required workflow outputs include `ProductionSigningAcceptance.json`, `ReleaseEvidence.json`, the release manifest, hashes and test results.

As of the 2026-09-17 AP-08 readiness check, the repository Actions API exposes **no completed or attempted `workflow_dispatch` run**. H3 therefore remains `PRODUCTION_RC_REQUIRED`; the repository must not infer that production signing credentials are configured merely because the workflow supports them.

Do not store PFX data, signing passwords or other secrets in Track A evidence.

## H4 – Deployment disaster-recovery acceptance

Create a deployment-specific copy of `docs/operations/DisasterRecoveryProfile.example.json` with `status=ACTIVE`, real ownership and the accepted RPO/RTO/retention/off-host requirements. Validate it without `-AllowTemplate`:

```powershell
.\scripts\operations\Test-DisasterRecoveryProfile.ps1 `
    -ProfilePath <active-profile.json> `
    -EvidencePath <profile-validation.json>
```

Then execute the real isolated restore drill for the intended production deployment and retain evidence that the restored environment is usable within the accepted RPO/RTO. Record both evidence references and the deployment identifier in the Track A closure evidence.

Credentials, database secrets and production data must not be copied into repository evidence.

## H5 – Exact-RC desktop accessibility acceptance

Populate a deployment/test copy of `docs/operations/AccessibilityAcceptance.example.json` from the exact Stable RC used for closure. Complete the documented keyboard-only, focus/no-trap, Narrator, Accessibility Insights and 100/125/150/200% DPI matrix.

Then validate the actual evidence:

```powershell
.\scripts\operations\Test-AccessibilityAcceptance.ps1 `
    -Path <accessibility-evidence.json> `
    -RequirePass
```

Record the retained evidence reference in the Track A closure evidence. A technical CI accessibility pass is necessary but is not a substitute for this manual acceptance.

## Final closure

Create a controlled copy of `docs/operations/TrackAAcceptance.example.json` and replace the remaining placeholder states/references with concrete evidence. H1 through H5 must all be `PASS`; `knownBlockingIssues` must be empty; the exact Stable source SHA/version and acceptance owner/timestamp must be recorded.

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
