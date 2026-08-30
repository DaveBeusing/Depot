# Depot Security Baseline

## Purpose

This document defines the baseline security expectations for Depot development and production releases. It is an engineering policy and does not constitute a certification.

## Security objectives

Depot should protect confidentiality, integrity, availability, authenticity, and accountability for application data and business workflows.

## Core requirements

### Authentication

- Production deployments must not rely on a publicly known default password.
- Passwords must be stored using an approved password hashing scheme with reviewed parameters.
- Authentication failures must not disclose whether sensitive account details exist beyond what is operationally necessary.
- Authentication controls must be tested before production releases.

### Authorization

- Authorization must be enforced in business/service boundaries and not rely solely on UI visibility.
- Least privilege is the default.
- Role and permission changes are security-relevant events.
- Creator/approver separation and administrator override rules must remain explicit and testable.

### Data protection

- Credentials and secrets must not be logged.
- Sensitive configuration must be protected at rest where feasible.
- Remote database connections should use encrypted transport.
- Exports, PDFs, logs, and backups must be considered part of the data-protection boundary.

### Business integrity

- Finalized transactions must not be silently rewritten when historical integrity is required.
- Corrections should be explicit and traceable.
- Security-sensitive and compliance-relevant operations should produce audit evidence.

### Logging

Security-relevant events should include, as applicable:

- authentication success/failure,
- permission/role changes,
- administrator overrides,
- security configuration changes,
- backup/restore operations,
- critical business reversals/corrections,
- integrity failures.

Logs must avoid unnecessary personal data and secrets.

### Dependencies

- Production dependencies must be inventoried.
- Release builds should generate an SBOM.
- Known vulnerabilities must be triaged before release.
- Unsupported dependencies require replacement, isolation, or explicit risk acceptance.

### Release security

Production releases should be signed, traceable to source, tested, and accompanied by dependency/security evidence.

## Secure development

Security is part of normal design and review. Changes affecting authentication, authorization, cryptography, secrets, audit, database access, import/export, updates, or business-record integrity require explicit security consideration.

## Related documents

- `../SecurityRoadmap.md`
- `ThreatModel.md`
- `DataProtection.md`
- `VulnerabilityManagement.md`
- `SupportPolicy.md`
- `ComplianceMatrix.md`
