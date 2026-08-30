# Depot Secure-by-Default Review

## Status

Technical baseline reviewed for Phase 5. Deployment-specific OS/database hardening and production signing remain release acceptance activities.

## Current secure defaults

| Area | Secure-default baseline | Evidence |
| --- | --- | --- |
| Administrator credentials | no production default password; first-run administrator creation required | bootstrap/authentication implementation and tests |
| Password storage | versioned PBKDF2-HMAC-SHA256 with hardened work factor | password security tests |
| Brute-force resistance | repeated failures are throttled | authentication tests |
| Authorization | service/business boundaries enforce permissions | authorization/RBAC tests |
| Local secret storage | persisted connection secrets protected with Windows DPAPI CurrentUser | secure configuration implementation/docs |
| Remote database transport | supported SQL Server/MySQL/MariaDB settings require encrypted transport | configuration validation/tests |
| Least privilege | application roles and database least-privilege deployment guidance | RBAC and `SecureConfiguration.md` |
| Audit | sensitive fields sanitized; normal UI has no audit modification/deletion path | audit tests/viewer |
| Telemetry/external transmission | no background telemetry/analytics/cloud upload enabled by default; future transmission is review-gated | `TelemetryPolicy.md` |
| Finalized business records | correction/reversal workflows replace destructive edits | Phase 4 controls/tests |
| Release integrity | source-bound builds, hashes and signing support | release-integrity workflow |
| Vulnerability exceptions | Critical prohibited; other exceptions structured, approved and expiring | risk-acceptance validator |

## Defaults that must not be weakened silently

Changes that make any of the following easier or less restrictive require explicit security review:

- default/embedded credentials,
- disabling TLS for remote databases,
- plaintext secret persistence,
- broad administrator/role grants,
- telemetry or remote transmission enabled without review,
- destructive mutation of finalized records,
- bypassing vulnerability/security release gates,
- unsigned production distribution once signing is an established production control.

## Deployment acceptance

Production deployment still requires OS ACLs, backup storage protection, real database TLS/least-privilege configuration, supported provider versions, recovery drills, production certificate/signing validation and organization-specific operational controls.
