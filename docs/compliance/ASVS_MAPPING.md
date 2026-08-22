# Depot OWASP ASVS Mapping

## Purpose

This document maps security verification themes from OWASP ASVS to Depot's desktop/business-application architecture. ASVS is used as engineering guidance; not every web-specific requirement applies to a Windows desktop application.

| Security area | Depot component/evidence | Current status | Next verification |
| --- | --- | --- | --- |
| Architecture and threat modeling | `THREAT_MODEL.md`, architecture docs | Partial | Complete risk records for high-priority scenarios |
| Authentication | User/authentication services, password hashing | Implemented / partial verification | First-run credentials, password policy, brute-force behavior |
| Session management | `AuthorizationService` sign-in/session state | Partial | Verify sign-out/session switching clears effective permissions |
| Access control | `AuthorizationService`, service-level permission checks | Implemented with tests | Expand negative-path coverage across all privileged workflows |
| Input validation | ViewModels/services/import workflows | Partial | Review imported files, identifiers, quantities, free text and provider-specific SQL paths |
| Stored cryptography | Password hashing, future protected settings/backups | Partial | Define approved algorithms/parameters and secret-storage design |
| Error handling and logging | Application/database/audit logging | Partial | Verify secret/PII redaction and security-event coverage |
| Data protection | DB, settings, logs, backups, PDFs, exports | Partial | Complete data inventory and retention/access controls |
| Communication security | Remote SQL Server/MySQL/MariaDB | Planned | Define TLS requirements and certificate validation expectations |
| Malicious code / supply chain | NuGet audit, CycloneDX SBOM | Implemented baseline | Review dependency licenses/support and release evidence |
| Business logic | Approval separation, immutable/reversal workflows | Strong baseline | Continue abuse-case tests for workflow bypasses and concurrent state changes |
| Files and resources | Excel import/export, PDF generation, backup/restore | Partial | Add hostile/malformed input and path/permission tests |
| Configuration | `depot.settings`, database configuration | Partial | Secure credential storage and secure defaults |

## Verification rule

A requirement is only treated as verified when there is identifiable evidence such as an automated test, repeatable manual acceptance procedure, build artifact, or reviewed configuration/document.
