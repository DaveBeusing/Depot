# Depot 1.0 Release Candidate Readiness

Updated: 2026-09-17

## Purpose

This document records the read-only-first Depot 1.0 technical gap reconciliation against the current repository implementation, automated tests, packaged acceptance, release evidence and canonical documentation.

It deliberately separates repository engineering defects from administrative, production-RC, deployment, manual, legal and demand-driven work. A missing external acceptance item must not be converted into substitute application code merely to make a checklist appear complete.

## Audit result

**Repository technical reconciliation: COMPLETE**

The audit found no currently known generic Depot 1.0 implementation defect and no currently known missing automated-test/evidence defect inside the advertised repository product boundary.

H1 repository governance is now closed through active GitHub ruleset `23590604`; H2 was already repository-level `PASS`. Depot nevertheless remains **not ready for a Stable 1.0 release** because H3 production signing, H4 deployment disaster recovery and H5 exact-RC manual accessibility acceptance remain explicit gates.

Ordinary CI, quality, security, database-provider and packaged-E2E gates remain authoritative. This document does not override a red or incomplete workflow run.

## Classification

The audit uses six categories:

1. repository implementation defect;
2. missing automated test/evidence defect;
3. documentation/baseline defect;
4. repository administration requirement;
5. production-RC/deployment/manual/legal acceptance requirement;
6. demand-driven extension outside the current 1.0 product promise.

### Category 1 — repository implementation defects

**No unresolved defect identified by this reconciliation.**

The audit rechecked the implemented boundaries that had previously produced concrete repair work: packaged executable replacement, electronic-invoice conformance, enterprise identity acceptance, durable Security Event delivery and DepotManager Windows integration. Those repairs are already represented by the merged repository state.

The repository search also exposes no `NotImplementedException` placeholder in product code and no open GitHub issue currently recording a known 1.0 implementation defect.

### Category 2 — automated test/evidence defects

**No unresolved generic evidence defect identified by this reconciliation.**

The repository contains dedicated acceptance for:

- CI/regression and warning-free Release builds;
- software quality, coverage, performance and static accessibility;
- security supply chain, dependency audit, lock files and SBOM/CRA evidence;
- SQLite, SQL Server, MariaDB and MySQL provider acceptance for the advertised exact baselines;
- XRechnung/KoSIT and ZUGFeRD/Factur-X/veraPDF repository conformance for the bounded advertised matrix;
- packaged DepotManager clean install, update, repair, rollback safety, manager self-update, uninstall scopes, diagnostics and artifact validation;
- real disposable-runner Windows uninstall registration, Start menu/desktop shortcuts, repair and idempotent cleanup;
- source-controlled Track A and repository-governance evidence contracts.

Production signing, real deployment restore and manual desktop accessibility cannot be replaced by ordinary automated repository tests; they remain Category 5.

### Category 3 — documentation/baseline defects

The earlier lifecycle wording defect was repaired by the technical reconciliation package. AP-08 closes the subsequent H1 status drift created when repository ruleset `23590604` was activated after that audit.

The canonical acceptance documents now distinguish:

- **repository packaged lifecycle acceptance — implemented**;
- **H1 repository governance — PASS with live active ruleset evidence**; and
- **exact production-signed Stable RC lifecycle acceptance — still required under H3**.

No persisted schema, product behavior or advertised feature scope changes are introduced by this reconciliation.

### Category 4 — repository administration

**Closed.**

GitHub repository ruleset `23590604`, `Depot master governance`, is active and targets exactly `refs/heads/master`. The live ruleset requires pull-request delivery, the five aggregate required checks, deletion/non-fast-forward protection, zero required approvals and no bypass actor.

The source-controlled contract remains `.github/rulesets/MasterGovernance.json`, and `scripts/operations/Test-RepositoryGovernance.ps1 -RequireActiveRuleset` remains the fail-closed live verification path. If the live ruleset is later removed or weakened, H1 reopens.

### Category 5 — production-RC, deployment, manual and legal acceptance

The following remain real 1.0 release blockers but are not generic code defects:

- H3 production Authenticode publisher/timestamp acceptance on an exact Stable release candidate;
- production signing credential ownership, expiry/rotation and recovery acceptance;
- exact Stable RC lifecycle acceptance using the production-signed artifacts;
- H4 ACTIVE deployment disaster-recovery profile, backup infrastructure and isolated restore evidence within accepted RPO/RTO;
- H5 keyboard-only, focus, Narrator, Accessibility Insights and 100/125/150/200% DPI acceptance on the exact RC;
- deployment accounting, posting, valuation, reporting, reconciliation, segregation-of-duties and retention procedures;
- customer-specific concurrency/network/volume sizing beyond the generic provider guard;
- deployment-specific GDPR/DSGVO, GoBD, CRA, tax, localization and other qualified legal/organizational review;
- final release notes, known limitations, hashes, SBOM and support information for the actual release.

These gates require real people, credentials, infrastructure, deployment decisions or the final release artifact. Repository engineering must preserve their fail-closed state until that evidence exists.

### Category 6 — demand-driven extensions

The following are outside the current bounded 1.0 promise unless separately marketed and implemented/accepted:

- additional EN 16931 special-tax combinations beyond the currently advertised matrix;
- recipient/channel routing such as organization-specific delivery or Peppol requirements;
- additional Factur-X profiles or arbitrary existing-PDF conversion;
- additional country/statutory localization packs requiring executable workflows;
- direct bank connectivity/payment initiation;
- additional costing methods or provider versions outside the certified database matrix;
- other enterprise identity or integration features not currently advertised.

Their absence is not a 1.0 defect while they remain outside the product promise.

## Packaged lifecycle boundary

Repository lifecycle acceptance is implemented through the real Release / `win-x64` / self-contained / single-file `Depot.exe` and `DepotManager.exe` artifacts.

The packaged acceptance covers clean installation, administrator provisioning, normal update, repair, artifact and manifest validation, historical schema migration, rollback safety, manager self-update and startup-failure restoration, keep/delete-data uninstall scopes, Windows uninstall registration, Start menu/desktop shortcuts, Windows-integration repair, application-binary removal and idempotent cleanup.

This is sufficient to close the generic repository lifecycle evidence gap. It is **not** a substitute for executing the final Stable candidate with the real production signing identity. That final RC run remains part of H3/release acceptance.

## H3 Stable RC entry condition

H1 and H2 are now closed. The next Track A acceptance step is the production-signed Stable RC.

From clean current `master`:

```powershell
.\scripts\release.ps1 -Channel Stable -AcceptanceOnly
```

The request uses `channel=Stable` and `publish_release=false`. Production signing configuration is validated before expensive acceptance work, and a successful run signs/timestamps both executables, validates the exact publisher identity, runs the production-signed packaged RC acceptance and retains immutable evidence without publishing a GitHub Release.

As of the AP-08 readiness check on 2026-09-17, the GitHub Actions API exposes no `workflow_dispatch` run. Therefore H3 remains `PRODUCTION_RC_REQUIRED`; no production signing credential or publisher identity is inferred from source code alone.

## Final Track A closure

A Stable release remains blocked until the remaining production/manual evidence is complete and the Track A controlled evidence file passes:

```powershell
.\scripts\operations\Test-TrackAAcceptance.ps1 `
    -Path <track-a-acceptance.json> `
    -RequirePass `
    -EvidencePath <track-a-validation.json>
```

H1 live governance can be revalidated at any time with:

```powershell
.\scripts\operations\Test-RepositoryGovernance.ps1 `
    -Repository DaveBeusing/Depot `
    -RequireActiveRuleset `
    -EvidencePath <h1-governance-evidence.json>
```

## Engineering decision after this audit

Do not open another generic feature or hardening package solely because Depot is still below version 1.0.

The next work should follow actual evidence:

- execute H3 production-signed Stable RC acceptance;
- if that run exposes a reproducible repository defect, open a focused repair package;
- otherwise continue to H4 deployment DR and H5 manual exact-RC accessibility acceptance;
- keep demand-driven extensions outside 1.0 until their product promise is explicitly changed.

`docs/Release1.0.md` remains the release checklist, `docs/TrackAAcceptanceClosure.md` remains the H1–H5 closure contract, and this document is the authoritative result of the 1.0 technical gap reconciliation.
