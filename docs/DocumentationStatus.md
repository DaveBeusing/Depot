# Documentation status

Updated: 2026-09-01

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application: `0.15.93-preview`
- Help manifest: `1.20`
- Core database schema: `30`
- Sales feature schema: `10`
- Finance feature schema: `9`
- User Sessions feature schema: `2`
- Finance foundation, General Ledger, Receivables, Payables, Inventory Accounting, Banking, Financial Reporting and Localization are implemented.
- User Sessions include persistent login sessions, heartbeat-derived presence, configurable idle/max-age policy, active/history administration, administrative termination, bulk user-session termination and revocation on account deactivation.

## Session documentation invariants

Documentation must state that:

- online presence is derived from `EndedUtc IS NULL` plus heartbeat freshness; no persisted `IsOnline` is authoritative;
- the default heartbeat interval is 30 seconds and the default presence timeout is 90 seconds;
- the central session policy defaults to 30 minutes idle timeout and 12 hours maximum session age;
- supported session-policy ranges are 5–480 idle minutes and 1–168 maximum-age hours;
- Depot records only the latest timestamp of keyboard/mouse/touch activity inside the main window; typed text, key values, mouse coordinates and external OS/window activity are not collected;
- activity is persisted with the normal heartbeat before policy evaluation rather than through a database write per input event;
- policy expiration uses `Expired`, returns the affected client to sign-in and applies maximum session age even while activity continues;
- saving a stricter policy evaluates already-open sessions immediately;
- session-policy changes require `Settings.Manage` and use optimistic Version checks;
- multiple concurrent sessions per user are supported;
- `Users.View` protects session visibility and `UserSessions.Terminate` separately protects destructive session actions;
- terminating one session or all open sessions for a user uses `AdministrativeLogout` and the affected client returns to sign-in after heartbeat detection;
- deactivating a user atomically revokes all still-open sessions with `Revoked`;
- heartbeats and raw activity events are not Audit events, while administrative termination and policy changes are audit-relevant actions;
- the History view shows the 200 most recently ended sessions and is operational lifecycle history rather than a replacement for the Audit log;
- User Sessions schema version 2 adds the central `UserSessionPolicy` singleton through a provider-neutral migration;
- session data collection remains minimal and does not include MAC addresses, hardware fingerprinting, IP/geolocation, key content or OS/window activity tracking;
- password-change policy, concurrent-session policy, session-history retention, MFA/external identity and security-event monitoring remain future work.

## Finance documentation invariants

Documentation must state that:

- the General Ledger is the authoritative immutable accounting ledger;
- Financial Reporting does not maintain a parallel ledger;
- Localization does not post journals;
- `LegalEntity.CountryCode` does not automatically activate localization;
- effective localization requires an explicit effective-dated assignment;
- the built-in Germany reference hierarchy resolves `GENERIC → EU → DE`;
- country packs are validated against Legal Entity country and active assignment ranges cannot overlap;
- built-in pack and registry definitions are immutable;
- custom regional/country packs can be added without another schema change when metadata/configuration is sufficient;
- support levels distinguish software capability, required configuration, external procedure and reference-only information;
- support levels are not legal/compliance status flags;
- Depot does not invent tax rates, statutory charts, filing classifications or accounting-policy choices;
- localization assignments and registry entries are retained Audit evidence;
- provider-neutral schema/code is not live-provider certification.

## Sales pricing documentation invariants

Documentation must state that:

- Sales pricing fallback is Customer → Region → Global and is evaluated independently for every item;
- Customer price-list assignments and Sales Regions are optional;
- automatically sourced draft pricing and finalized document snapshots remain distinct;
- a higher-scope list never suppresses fallback for an item absent at that scope.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply an unconfigured jurisdiction, currency, tax rate, chart/account, accounting standard or reporting classification;
- claim weighted-average, standard cost, LIFO, impairment/NRV or manufacturing costing as implemented;
- claim configurable reports are automatically jurisdiction-specific statutory filings;
- claim assigning a country pack makes a deployment legally/tax/statutorily compliant;
- claim all possible country packs are implemented merely because the framework can host them;
- describe remote session revocation, session history, bulk session termination, idle timeout or maximum session age as future-only features now that they are implemented;
- imply that stale open sessions caused by crash/network loss are equivalent to explicitly ended historical sessions;
- imply that Depot records input content merely because it tracks a last-activity timestamp;
- hide repository failures by attributing them to unrelated Finance changes.

Help manifest **1.20** includes the `administration.user-sessions` topic with active/history guidance, session termination, bulk termination and session-policy behavior, plus cross-links from Users, Dashboard and Audit Log. The topic is visible with `Users.View`; destructive session actions remain independently guarded by `UserSessions.Terminate` and policy edits by `Settings.Manage` in application services.
