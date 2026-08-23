# Depot Threat Model

## Status

Phase 1 baseline assessment completed. Open risks are intentionally carried into later roadmap phases and must be reassessed as the corresponding controls are implemented.

## Scope

Depot is a Windows desktop application with WPF/MVVM architecture and a provider-neutral persistence layer supporting SQLite, SQL Server, MySQL, and MariaDB. It handles inventory, warehouse, procurement, sales, administration, reporting, authentication, authorization, audit data, documents, imports/exports, and backups.

## Risk scale

Likelihood and impact use `Low`, `Medium`, and `High`. Risk is derived conservatively: any High/High scenario is Critical; High combined with Medium is High; Medium/Medium is Medium; remaining scenarios are Low/Medium according to impact. Ratings are engineering prioritization, not a substitute for a formal regulatory risk method.

## Assets

- user identities and credentials,
- roles and permissions,
- customer and supplier data,
- inventory quantities and valuations,
- purchase and sales records,
- invoices and credit notes,
- audit records,
- database credentials,
- application configuration,
- backups,
- generated PDFs and exports,
- release/signing assets.

## Trust boundaries

1. User -> Depot desktop UI.
2. UI/ViewModel -> business services.
3. Business services -> repositories/data access.
4. Depot -> local SQLite database.
5. Depot -> remote SQL Server/MySQL/MariaDB server.
6. Depot -> filesystem for settings, logs, backups, PDFs, and exports.
7. Build system -> package/dependency sources.
8. CI/release process -> signing and published artifacts.

## STRIDE coverage

The baseline considers spoofing, tampering, repudiation, information disclosure, denial of service, and elevation of privilege across the trust boundaries above.

## Risk register

| ID | Scenario | STRIDE | Likelihood | Impact | Risk | Existing controls/evidence | Required mitigation / verification | Residual target | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| THR-001 | Known/default or insecure initial administrator credentials allow account takeover | Spoofing/Elevation | High | High | Critical | Password hashing and authentication tests | Remove production default credentials; require secure first-run administrator creation; password policy and brute-force controls | Low | Engineering | Open - Phase 2 |
| THR-002 | Privileged workflow bypasses authorization through a service/repository path | Elevation | Medium | High | High | `AuthorizationService`, RBAC and service-level authorization tests | Expand negative-path coverage for every privileged workflow and review direct repository mutation paths | Low | Engineering | Mitigating / Phase 2 |
| THR-003 | Finalized business records are altered without a traceable correction | Tampering/Repudiation | Medium | High | High | Approval separation and immutable/reversal patterns in selected workflows | Classify mutable/final records; enforce correction/reversal model across relevant business objects | Low | Engineering/Product | Open - Phase 4 |
| THR-004 | Audit evidence is modified/deleted by normal application behavior or privileged misuse | Tampering/Repudiation | Medium | High | High | Audit service/tests | Protect audit records, define retention/export and administrator override evidence; add tamper/negative tests | Low | Engineering | Open - Phase 2 |
| THR-005 | Database credentials leak from settings, logs, errors, exports or local files | Information disclosure | Medium | High | High | Logging/cryptography policies | Protect credentials at rest; redact secrets; define Windows user/machine protection behavior; verify by tests/manual review | Low | Engineering | Open - Phase 2 |
| THR-006 | Personal/commercially sensitive information leaks through logs, backups, PDFs or exports | Information disclosure | Medium | High | High | Data-protection and logging baselines | Complete data inventory; minimize/redact logs; define backup/export access and retention | Low/Medium | Engineering/Product | Open - Phases 2/3 |
| THR-007 | Unsafe SQL construction/provider-specific paths permit injection or unintended commands | Tampering/Disclosure | Low/Medium | High | High | Repository/data-access abstraction | Review dynamic SQL and provider-specific commands; require parameterization; add hostile-input tests | Low | Engineering | Open - Phase 2 |
| THR-008 | Malformed or hostile Excel/import content causes data corruption, resource exhaustion or unsafe file handling | Tampering/DoS | Medium | Medium | Medium | Import validation/business services | Define size/type limits, hostile/malformed fixtures, path handling and transactional rollback tests | Low | Engineering | Open - Phase 2/7 |
| THR-009 | Unauthorized/substituted/corrupt backup is restored, compromising integrity or confidentiality | Tampering/Disclosure | Medium | High | High | Backup/restore capability | Define access, integrity/encryption approach and negative recovery tests; document recovery procedure | Low | Engineering | Open - Phase 2 |
| THR-010 | Compromised/vulnerable NuGet dependency enters a release | Tampering/Elevation | Medium | High | High | Locked restore, NuGet audit, CycloneDX SBOM, dependency evidence/policy | Continue automated audit; manually review exceptional licenses/support; document risk acceptance | Low/Medium | Engineering | Mitigated baseline / continuous |
| THR-011 | Release executable/package is modified or impersonated after build | Tampering/Spoofing | Medium | High | High | CI traceability baseline | Authenticode signing, timestamping, protected signing credentials, artifact hashes/provenance | Low | Engineering/Product | Open - Phase 2 |

## Risk treatment rules

- Critical risks block a production release unless eliminated or explicitly accepted by the Product Owner with documented rationale and qualified review where required.
- High risks require remediation or documented time-bounded risk acceptance before production release.
- Medium/Low risks remain tracked and are prioritized according to exposure and roadmap.
- A risk is not marked `Mitigated` solely because a policy exists; verification evidence is required.

## Phase 1 conclusion

The principal security threats are identified, rated, assigned, and linked to concrete roadmap work. Phase 1 therefore establishes the threat-model baseline; it does not claim that open Phase 2+ risks have already been remediated.

## Review triggers

Review this model at least every six months and whenever authentication, authorization, database connectivity, remote services, update mechanisms, import/export, backup/restore, identity integration, release distribution, or major business workflows materially change.
