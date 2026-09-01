# Documentation status

Updated: 2026-09-01

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application: `0.15.94-preview`
- Help manifest: `1.21`
- Core database schema: `30`
- Sales feature schema: `10`
- Finance feature schema: `9`
- User Sessions feature schema: `2`
- Security Events feature schema: `1`
- Finance foundation, General Ledger, Receivables, Payables, Inventory Accounting, Banking, Financial Reporting and Localization are implemented.
- User Sessions include persistent login sessions, heartbeat-derived presence, configurable idle/max-age policy, active/history administration, administrative termination, bulk termination and revocation on account deactivation.
- Security Events include deterministic suspicious-authentication escalation, lockout events, success-after-failure correlation, administrative session events, High/Critical notifications and a reviewable Security Center.

## Session and security documentation invariants

Documentation must state that:

- online presence is derived from `EndedUtc IS NULL` plus heartbeat freshness; no persisted `IsOnline` is authoritative;
- the default heartbeat interval is 30 seconds and presence timeout is 90 seconds;
- central session policy defaults to 30 minutes idle timeout and 12 hours maximum session age;
- Depot records only the latest timestamp of keyboard/mouse/touch activity inside the main window; typed text, key values, mouse coordinates and external OS/window activity are not collected;
- policy expiration uses `Expired`, and maximum session age applies even while activity continues;
- `Users.View`, `Settings.Manage` and `UserSessions.Terminate` remain separate session permissions;
- suspicious-login monitoring is deterministic and reuses the existing in-process 15-minute authentication throttle window;
- failures 1–2 are informational, failure 3 is Warning, failure 4 is High, failure 5/active lockout is Critical, and successful authentication after recent failures is retained as a separate event;
- suspicious events are triage signals rather than proof of compromise;
- `SecurityEvents.View` protects Security Center visibility and `SecurityEvents.Manage` protects review actions;
- Security Event review changes only review metadata/Version and not the original event contents;
- High/Critical Security Events may generate Notification Center alerts for `SecurityEvents.View` holders;
- Security Events complement rather than replace the business Audit Log;
- Security Events schema version 1 is provider-neutral and independent from User Sessions schema version 2;
- the current implementation does not collect source IP, geolocation, MAC address, hardware fingerprint, typed input, mouse coordinates or external-window activity;
- password-change invalidation, concurrent-session policy, retention/archival, shared throttling for multi-node deployments, MFA/external identity and any future IP/geo/device-trust signals remain future work.

## Finance documentation invariants

Documentation must state that the General Ledger is authoritative, reporting does not maintain a parallel ledger, Localization does not post journals, effective localization requires explicit assignment, provider-neutral code is not certification, and Depot does not invent jurisdiction-specific statutory configuration.

## Sales pricing documentation invariants

Documentation must state that Sales pricing fallback is Customer → Region → Global per item, optional scopes do not suppress fallback, and historical finalized document snapshots remain distinct from mutable current pricing.

## Documentation rules

Documentation must not describe implemented session expiry or Security Center features as future-only work, imply suspicious-login events prove compromise, imply that Security Events replace Audit evidence, or claim that IP/geolocation/device fingerprinting exists in this version.

Help manifest **1.21** includes `administration.user-sessions` and the new `administration.security-center` topic. The Security Center topic requires `SecurityEvents.View`; review remains independently guarded by `SecurityEvents.Manage` in application services.
