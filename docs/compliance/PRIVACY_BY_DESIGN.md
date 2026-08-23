# Depot Privacy by Design

## Design rules

Privacy review is required for features that introduce new personal-data fields, external transmission, telemetry, cloud services, analytics, identity providers, email providers, remote support, exports, or new long-lived logs.

Every such change should document:

1. purpose and data categories;
2. minimum fields required;
3. users/roles allowed to access the data;
4. storage and derived copies;
5. retention/lifecycle behavior;
6. audit requirements;
7. external recipients/processors;
8. security controls and encryption;
9. data-subject discovery/export impact;
10. migration/deletion consequences.

## Current decisions

- Authentication is local and no authentication telemetry is sent externally.
- Database credentials are DPAPI-protected and excluded from privacy exports.
- Remote database traffic requires encryption.
- Audit and business evidence is append/correction oriented rather than destructively rewritten.
- The application currently has no general-purpose product telemetry pipeline.
- External email/document transmission is explicit user workflow, not background telemetry.
- Data-subject exports must be generated only by an authorized administrator and must exclude secrets/authentication hashes.

## Pull-request requirement

The existing security/compliance review should treat a change as privacy-impacting when any rule above is triggered and update `DATA_INVENTORY.md`, `RETENTION_POLICY.md`, and the data-subject search/export implementation when necessary.
