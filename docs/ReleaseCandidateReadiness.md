# Depot 1.0 Release Candidate Readiness

Updated: 2026-09-17

## Purpose

This document records the read-only-first Depot 1.0 technical gap reconciliation against the current repository implementation, automated tests, packaged acceptance, release evidence and canonical documentation.

It deliberately separates repository engineering defects from administrative, production-RC, deployment, manual, legal and demand-driven work. A missing external acceptance item must not be converted into substitute application code merely to make a checklist appear complete.

## Audit result

**Repository technical reconciliation: COMPLETE**

The audit found no currently known generic Depot 1.0 implementation defect and no currently known missing automated-test/evidence defect inside the advertised repository product boundary.

The repository remains **not ready for a Stable 1.0 release** because required external evidence is still outstanding. In particular, H1 repository administration, H3 production signing, H4 deployment disaster recovery and H5 exact-RC manual accessibility acceptance remain explicit gates.

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

One remaining wording defect was identified during this audit: `Release1.0.md` and `Roadmap.md` still represented installer/package upgrade/rollback/uninstall acceptance as wholly open after the packaged lifecycle and Windows-integration evidence had been implemented.

This package corrects that boundary by splitting it into:

- **repository packaged lifecycle acceptance — implemented**; and
- **exact production-signed Stable RC lifecycle acceptance — still required**.

The distinction matters because the repository has already exercised the production lifecycle code paths, while a final Stable candidate still needs release-specific acceptance with the real publisher identity.

No persisted schema, product behavior or advertised feature scope changes are introduced by this reconciliation.

### Category 4 — repository administration

H1 remains `ADMIN_REQUIRED` until GitHub contains an active ruleset equivalent to `.github/rulesets/MasterGovernance.json`.

The repository-side contract is complete. `scripts/operations/Test-RepositoryGovernance.ps1` validates the versioned policy and can query the live GitHub rulesets API with `-RequireActiveRuleset`. A source-controlled template alone is not H1 production evidence.

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

## Release-candidate entry condition

Depot may move from generic repository engineering into controlled 1.0 release-candidate acceptance when the ordinary merge gates for the current source are green. Entering RC acceptance does not mean the release is approved.

A Stable release remains blocked until the applicable administrative/production/manual evidence above is complete and the Track A controlled evidence file passes:

```powershell
.\scripts\operations\Test-TrackAAcceptance.ps1 `
    -Path <track-a-acceptance.json> `
    -RequirePass `
    -EvidencePath <track-a-validation.json>
```

H1 live governance evidence is obtained separately with:

```powershell
.\scripts\operations\Test-RepositoryGovernance.ps1 `
    -Repository DaveBeusing/Depot `
    -RequireActiveRuleset `
    -EvidencePath <h1-governance-evidence.json>
```

## Engineering decision after this audit

Do not open another generic feature or hardening package solely because Depot is still below version 1.0.

The next work should follow actual evidence:

- if an ordinary gate or RC acceptance run exposes a reproducible repository defect, open a focused repair package;
- otherwise complete H1/H3/H4/H5 and the applicable deployment/legal acceptance outside the generic product-code backlog;
- keep demand-driven extensions outside 1.0 until their product promise is explicitly changed.

`docs/Release1.0.md` remains the release checklist, `docs/TrackAAcceptanceClosure.md` remains the H1–H5 closure contract, and this document is the authoritative result of the 1.0 technical gap reconciliation.
