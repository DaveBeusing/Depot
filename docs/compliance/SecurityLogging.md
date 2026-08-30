# Depot Security Logging Requirements

## Goals

Security logging should provide enough evidence to investigate privileged access, configuration changes, failed security controls, and material business overrides without unnecessarily collecting secrets or personal data.

## Events to consider security relevant

- authentication success/failure,
- account activation/deactivation,
- role and permission changes,
- administrator overrides and reasons,
- protected workflow approval/rejection/reversal,
- security/database configuration changes,
- backup and restore operations,
- integrity/migration failures,
- security-sensitive import failures,
- future update/signature validation failures.

## Minimum fields

Where applicable record timestamp, actor/user identifier, action/event type, affected entity identifier, outcome, reason/override justification, and correlation/session context that does not expose secrets.

## Prohibited or restricted content

Do not log passwords, password hashes, private keys, access tokens, raw connection-string secrets, or unnecessary full customer/invoice payloads. Exception/diagnostic logging must redact secrets.

## Integrity and retention

Security/audit evidence should not be modifiable through normal end-user workflows. Retention and export requirements must be defined before production release and aligned with privacy and business-record obligations.
