# Depot CRA Cybersecurity Risk Assessment

## Purpose

This is the CRA-oriented product cybersecurity risk assessment for Depot. It complements `ThreatModel.md` and links product risks to concrete controls, tests, evidence and residual-risk decisions.

## Method

Likelihood and impact use Low / Medium / High. Risk is derived conservatively. A risk is considered treated only when implementation and verification evidence exist. Residual acceptance must be explicit, time-bounded where appropriate, and may not bypass legal obligations.

## Risk register

| ID | Risk scenario | Initial risk | Primary controls / mitigation | Verification evidence | Residual target | Status |
| --- | --- | --- | --- | --- | --- | --- |
| CRA-001 | Unauthorized account takeover through weak/default credentials | Critical | first-run administrator creation, password policy, PBKDF2 versioning, login throttling | authentication/security tests | Low | Mitigated baseline |
| CRA-002 | Privilege escalation or authorization bypass | High | service-boundary RBAC, explicit permissions, creator/approver separation | authorization/RBAC/workflow tests | Low | Mitigated baseline |
| CRA-003 | Secrets exposed in settings, logs, exports or diagnostics | High | DPAPI protection, redaction/sanitization, TLS-required remote DB settings | configuration/audit/privacy tests and review | Low | Mitigated baseline |
| CRA-004 | Vulnerable or compromised third-party dependency enters a release | High | locked restore, NuGet audit, CycloneDX SBOM, dependency policy | Security supply chain workflow artifacts | Low/Medium | Continuous |
| CRA-005 | Release artifact is replaced, modified or cannot be traced to source | High | exact-source checkout, SHA-256 manifest, conditional Authenticode signing | Release integrity workflow | Low | Technical pipeline implemented |
| CRA-006 | Finalized business records are silently modified | High | immutable workflow states, corrective transactions, atomic audit evidence | BusinessRecordIntegrityTests | Low | Mitigated baseline |
| CRA-007 | Audit evidence is lost, altered through normal application paths, or leaks secrets | High | append-oriented audit paths, RBAC, sanitizer, transactional writes, exports | AuditLogTests / integrity tests | Low/Medium | Mitigated baseline |
| CRA-008 | Backup/restore causes corruption, unauthorized substitution or historical inconsistency | High | retention/recovery controls, integrity expectations, provider recovery procedure | automated SQLite baseline; provider drills required | Low/Medium | Partial external validation |
| CRA-009 | Malformed import/export input causes corruption or resource exhaustion | Medium | validation and transactional service boundaries | workflow/import tests; Phase 7 performance/failure expansion | Low | Ongoing |
| CRA-010 | Security vulnerability is known but release proceeds without treatment | High | vulnerability policy, CI dependency gate, release risk-acceptance validation | security workflows and exception validation | Low | Phase 5 control |
| CRA-011 | Vulnerability report is lost or publicly disclosed before coordinated remediation | High | repository security policy and private-reporting instructions | SECURITY.md review | Low | Phase 5 control |
| CRA-012 | Actively exploited vulnerability or severe incident is not escalated in regulatory time | High | incident/reporting runbook with 24h/72h milestones and ownership requirements | tabletop/organizational drill required | Low/Medium | Procedure implemented; operational validation required |
| CRA-013 | Security updates cannot be produced, verified or rolled back safely | High | reproducible locked restore, release integrity pipeline, backup/migration/rollback procedures | CI/release evidence and deployment drill | Low/Medium | Technical baseline |
| CRA-014 | Unsupported runtime/database/dependency leaves product exposed during support period | High | support policy, dependency lifecycle review and documented supported configurations | release review evidence | Low/Medium | Policy baseline |
| CRA-015 | Insecure default installation/configuration exposes the product | High | no default password, least privilege guidance, TLS-required remote DB settings, protected secrets | first-run/configuration tests and secure configuration review | Low | Mitigated baseline |

## Risk-to-evidence rules

Each High/Critical product cybersecurity risk must have:

- an accountable engineering/product owner role,
- one or more implemented mitigations,
- verification evidence,
- a residual-risk statement,
- a review trigger,
- an explicit risk acceptance if the residual risk remains High.

A risk acceptance does not make a known exploitable vulnerability acceptable by itself. Regulatory and product-safety obligations remain overriding gates.

## Review triggers

Review this assessment at least before each production release and whenever there is a material architecture change, new remote service, new authentication/identity capability, new database/provider, update mechanism change, severe incident, actively exploited vulnerability, substantial modification, or relevant CRA guidance/standard change.
