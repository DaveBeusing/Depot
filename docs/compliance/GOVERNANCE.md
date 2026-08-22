# Depot Compliance Governance

## Purpose

This document assigns ownership, review cadence, review triggers, and approval expectations for Depot security and compliance documentation.

## Roles

During preview development one person may hold multiple roles. The roles describe responsibilities rather than requiring separate individuals.

- **Product Owner** — accountable for product scope, support commitments, release risk acceptance, and escalation to qualified legal/compliance advisers.
- **Engineering Owner** — accountable for application security architecture, implementation controls, tests, dependencies, cryptography, and technical evidence.
- **Compliance/Legal Reviewer** — qualified internal or external reviewer used when legal interpretation, regulatory applicability, privacy, licensing, tax, or formal conformity decisions are required.

## Document ownership

| Document | Primary owner | Minimum review interval | Additional review trigger | Approval |
| --- | --- | --- | --- | --- |
| `SECURITY.md` | Engineering Owner | 6 months | Material security architecture change or incident | Engineering Owner |
| `THREAT_MODEL.md` | Engineering Owner | 6 months | New trust boundary, authentication model, external integration, database/deployment model, update mechanism, import/export or backup change | Engineering Owner |
| `DATA_PROTECTION.md` | Product Owner | 6 months | New personal-data category, telemetry, cloud service, external identity, analytics or integration | Product Owner; Compliance/Legal review when required |
| `VULNERABILITY_MANAGEMENT.md` | Engineering Owner | 6 months | Significant vulnerability, incident, disclosure-process change or applicable regulatory change | Engineering Owner + Product Owner for risk acceptance |
| `SUPPORT_POLICY.md` | Product Owner | Before every major production release and at least annually | Runtime/platform lifecycle or support commitment change | Product Owner |
| `COMPLIANCE_MATRIX.md` | Product Owner | 6 months | New market/distribution model, regulatory change or new regulated feature | Product Owner; Compliance/Legal review where applicability is uncertain |
| `DEPENDENCY_POLICY.md` | Engineering Owner | 6 months | Dependency/license policy change or material supply-chain incident | Engineering Owner |
| `ASVS_MAPPING.md` | Engineering Owner | 6 months | Major architecture/security-control change | Engineering Owner |
| `CRYPTOGRAPHY.md` | Engineering Owner | Annually | Algorithm/platform recommendation change or cryptographic incident | Engineering Owner |
| `SECURITY_LOGGING.md` | Engineering Owner | Annually | Audit/logging architecture or data-classification change | Engineering Owner |
| `SECURITY_REVIEW.md` | Engineering Owner | 6 months | Development/review workflow change | Engineering Owner |
| `LICENSE_REVIEW.md` | Engineering Owner | Each dependency change and before production release | New/changed/unknown/restrictive license | Engineering Owner; Compliance/Legal review for non-allowlisted licenses |
| `../SECURITY_ROADMAP.md` | Product Owner | Before each minor/major production release | Material regulatory/security strategy change | Product Owner + Engineering Owner |

## Review record

Each formal review should be recorded in the relevant pull request or release evidence with:

- document/version or commit reviewed,
- review date,
- reviewer role,
- material changes/findings,
- unresolved risks/actions,
- approval or escalation decision.

The repository history remains the authoritative change history; dates should not be edited merely to simulate a review.

## Risk acceptance

Critical/high security or compliance exceptions require an explicit record containing scope, rationale, compensating controls, owner, expiration/review date, and remediation plan. Engineering may propose an exception; the Product Owner accepts product/release risk. Legal or regulatory uncertainty must be escalated to a qualified reviewer rather than resolved by assumption.

## Phase 1 baseline review

Phase 1 documentation and technical controls were established on 2026-08-22. The next routine governance review is due no later than 2027-02-22, with earlier review triggered by the conditions above.
