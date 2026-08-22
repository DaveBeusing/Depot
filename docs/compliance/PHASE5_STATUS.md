# Phase 5 Technical Status — Cyber Resilience Act Readiness

Date: 2026-08-22

## Status

**TECHNICAL IMPLEMENTATION COMPLETE — 2026-08-22**

This status covers repository/application engineering controls that can be implemented without final legal classification, a marketed production release, named organizational reporting roles, production signing identity, or formal conformity assessment. It does not constitute CRA conformity or legal advice.

## Implemented technical baseline

- [x] CRA product classification has a documented technical preliminary assessment in `CRA_CLASSIFICATION.md`, including reassessment triggers and external acceptance gates.
- [x] CRA-oriented cybersecurity risk assessment is maintained in `CRA_RISK_ASSESSMENT.md` and links risks to controls, tests/evidence and residual targets.
- [x] Direct/transitive dependencies are locked, audited and inventoried; CycloneDX SBOM evidence is generated automatically.
- [x] Coordinated vulnerability disclosure policy is published in repository-root `SECURITY.md` with private-reporting guidance and public-disclosure safeguards.
- [x] Vulnerability-management triage/remediation targets are defined by severity and overridden by active exploitation/regulatory escalation.
- [x] Security update creation, validation, distribution, communication and rollback rules are defined in `SECURITY_UPDATE_LIFECYCLE.md`.
- [x] Production support planning uses a CRA-aligned default minimum five-year engineering floor and requires a published release-line end date and lifecycle factors.
- [x] CRA vulnerability/severe-incident reporting runbook records the awareness timestamp and 24h/72h/final-report milestones.
- [x] Secure-by-default controls are reviewed and documented in `SECURE_DEFAULTS_REVIEW.md`.
- [x] Security exceptions are machine-readable and validated in CI/release workflows.
- [x] Critical vulnerabilities cannot use the normal release exception mechanism.
- [x] Actively exploited vulnerabilities cannot use the normal release exception mechanism at any severity.
- [x] High exceptions require explicit security review; all exceptions require owner, rationale, compensating controls, affected versions, approval and future expiry.
- [x] Release restore runs NuGet audit with warnings-as-errors in addition to the separate security supply-chain audit.
- [x] CRA technical documentation is indexed in `CRA_TECHNICAL_DOCUMENTATION.md`.
- [x] CI builds a CRA technical-evidence artifact containing relevant documentation, risk register, risk acceptances and CycloneDX SBOM with SHA-256 evidence manifest and source commit/version.
- [x] Release integrity captures source identity, risk-acceptance state, hashes and signing support.

## Automated evidence

The `Security supply chain` workflow provides:

- NuGet vulnerability audit,
- locked dependency verification,
- security/privacy/business-integrity tests,
- risk-acceptance validation,
- CycloneDX SBOM,
- dependency/deprecation inventory,
- CRA technical evidence artifact and SHA-256 manifest.

The `Release integrity` workflow provides:

- exact source identity verification,
- security exception validation,
- audited locked restores,
- production artifact build,
- conditional production Authenticode signing/timestamp verification for tagged releases,
- SHA-256 release manifest,
- source/risk-acceptance release evidence.

## External / production acceptance gates

The following are intentionally **not** marked as generic code-complete compliance claims and must be completed for the actual marketed product/release:

1. qualified legal/compliance confirmation of CRA scope, manufacturer/economic-operator role and final product classification;
2. final conformity-assessment route against the then-current product, harmonised standards/common specifications and delegated acts;
3. EU declaration of conformity and CE-marking process where applicable;
4. production manufacturer/contact/user information required by the CRA and Annex II;
5. release/product-specific support end date (month/year) and documented determination factors;
6. verification/enabling of a dependable private vulnerability-reporting channel for production operation;
7. named organizational vulnerability/incident owner, legal reporting authority and customer-communications responsibility;
8. competent CSIRT/main-establishment determination and operational access to the ENISA CRA Single Reporting Platform;
9. tabletop exercise proving the 24h/72h reporting process can be executed operationally;
10. production Authenticode certificate/timestamp acceptance already tracked in Phase 2;
11. provider/OS deployment recovery and permission-boundary acceptance gates already tracked in Phase 2;
12. review of new Commission guidance, harmonised standards and delegated acts before commercial distribution.

## Current regulatory timing reference

Engineering planning currently assumes CRA reporting obligations apply from **11 September 2026** and the principal remaining CRA obligations from **11 December 2027**. Release/legal review must verify the current regulation and Commission guidance at the time of distribution/incident.

## Evidence files

- `SECURITY.md`
- `docs/compliance/CRA_CLASSIFICATION.md`
- `docs/compliance/CRA_RISK_ASSESSMENT.md`
- `docs/compliance/CRA_INCIDENT_REPORTING.md`
- `docs/compliance/CRA_TECHNICAL_DOCUMENTATION.md`
- `docs/compliance/SECURITY_UPDATE_LIFECYCLE.md`
- `docs/compliance/SECURE_DEFAULTS_REVIEW.md`
- `docs/compliance/VULNERABILITY_MANAGEMENT.md`
- `docs/compliance/SUPPORT_POLICY.md`
- `security/security-risk-acceptances.json`
- `scripts/security/validate-risk-acceptances.ps1`
- `scripts/security/build-cra-evidence.ps1`
- `.github/workflows/security-supply-chain.yml`
- `.github/workflows/release-integrity.yml`
