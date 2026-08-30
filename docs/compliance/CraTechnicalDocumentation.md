# Depot CRA Technical Documentation Index

## Purpose

Provide a stable technical-documentation index for CRA readiness and evidence collection. This index is engineering input for the final conformity documentation; it is not an EU declaration of conformity.

## Product identification

- Product: Depot
- Product type: Windows desktop business/inventory operations software
- Version source: `Directory.Build.props`
- Source repository/revision: captured by CI/release evidence
- Database schema version: `src/Depot/Data/DatabaseVersion.cs`

## Intended purpose and architecture

- `README.md`
- `docs/compliance/CraClassification.md`
- `docs/compliance/ThreatModel.md`
- `docs/compliance/SecureConfiguration.md`
- architecture/source under `src/Depot`

## Cybersecurity risk assessment

- `docs/compliance/CraRiskAssessment.md`
- `docs/compliance/ThreatModel.md`
- `docs/compliance/AsvsMapping.md`
- Phase-specific status/evidence documents

## Essential-security implementation evidence

- authentication/authorization controls and tests,
- secret protection and TLS configuration,
- audit and business-record integrity controls,
- backup/recovery controls,
- privacy/data-minimization baseline,
- dependency/SBOM controls,
- release integrity and signing pipeline,
- secure-by-default review,
- vulnerability/risk-acceptance release gates.

## Vulnerability handling and support

- root `SECURITY.md`
- `docs/compliance/VulnerabilityManagement.md`
- `docs/compliance/CraIncidentReporting.md`
- `docs/compliance/SecurityUpdateLifecycle.md`
- `docs/compliance/SupportPolicy.md`
- `security/security-risk-acceptances.json`

## Supply-chain evidence

The Security supply chain workflow generates/retains:

- CycloneDX SBOM,
- direct/transitive dependency inventory,
- deprecated dependency inventory,
- vulnerability-audit result,
- dependency-lock verification,
- CRA technical-evidence package with SHA-256 manifest.

## Release evidence

The Release integrity workflow records:

- exact source SHA/ref,
- successful locked/audited restores,
- release build,
- risk-acceptance validation,
- SHA-256 artifact manifest,
- production signing/timestamp verification for tagged releases when signing secrets are configured,
- release evidence artifact.

## User/product information still requiring release-specific completion

Before commercial placing on the EU market, the final product package/documentation must supply the legally required manufacturer/economic-operator identification, product/version identification, support-period end date, user security/update instructions, conformity information and other Annex II information applicable to the final product/release.

## Conformity items outside generic repository automation

Final classification, applicable harmonised standards/common specifications, conformity-assessment route, EU declaration of conformity, CE marking, manufacturer/importer obligations and notified-body involvement (if classification requires it) must be completed by the responsible organization for the marketed product.
