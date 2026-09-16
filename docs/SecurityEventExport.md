# Security Event Export Foundation

Updated: 2026-09-16

## Scope

F5A defines the provider-neutral source contract for exporting Depot Security Events to external security tooling. It deliberately does not implement background delivery, retry scheduling, durable sink configuration or persistent delivery checkpoints; those concerns belong to F5B.

The export foundation reads the existing `SecurityEvents` evidence. It does not create a parallel event log and does not mutate source rows while exporting them.

## Export contract

`SecurityEventExportBatch` format version `1` contains a bounded, ascending sequence of `SecurityEventExportRecord` values. The exported event projection contains only event-time evidence that is stable after creation:

- source Security Event ID;
- UTC timestamp;
- event type and severity;
- optional local user, account, session, client-instance and machine correlation identifiers;
- summary and controlled details.

Security Center review metadata (`ReviewedUtc`, `ReviewedByUserId`) and the mutable database row `Version` are intentionally excluded from the export record. Reviewing an event therefore does not rewrite the externally exported security-event meaning.

## Filters

F5A supports a bounded source filter consisting of:

- minimum severity;
- an optional allow-list of Security Event types.

The filter is normalized before use: event types are deduplicated and ordered. A canonical SHA-256 fingerprint is calculated over the normalized filter contract.

## Checkpoints

`SecurityEventExportCheckpoint` contains:

- the last consumed Security Event ID;
- the SHA-256 fingerprint of the filter that produced the cursor.

A checkpoint cannot be reused with another filter. Filter mismatch fails closed instead of silently skipping or replaying events under different selection semantics.

Security Event IDs, rather than timestamps, are the ordering/cursor boundary. Each read captures the current maximum source ID before querying. A batch reads only IDs greater than the incoming checkpoint and less than or equal to that snapshot boundary.

If more matching events exist than the requested batch size, the next checkpoint advances to the last returned event. If the snapshot is exhausted, the next checkpoint advances to the captured source maximum even when trailing source events were excluded by the filter. This prevents repeated rescanning of permanently excluded rows while ensuring events inserted after the snapshot are left for the next read.

## Batching

The source batch size is bounded to 500 events. F5A never returns an unbounded Security Event export query.

The source repository uses ascending source ID order. This deterministic order is independent of UI ordering, review state and local-time presentation.

## Sink boundary

`ISecurityEventExportSink` defines the delivery adapter boundary without implementing delivery policy. A sink receives an already bounded `SecurityEventExportBatch`; F5A does not advance a durable checkpoint simply because a sink was invoked.

F5B will own delivery orchestration, retry classification, durable checkpoint advancement and concrete sink implementations. A durable checkpoint may advance only after the corresponding batch has satisfied the F5B delivery contract.

## Persistence and schema

F5A adds no persisted tables or columns and does not change Security Events feature schema `2`.

Durable export configuration/checkpoint state is intentionally deferred to F5B so the persisted schema is introduced together with the exact delivery and retry invariants it must protect.

## Privacy and authorization boundary

The export contract contains only fields already present in Depot Security Events. It does not add source IP, geolocation, device fingerprint, typed input, external-window activity, authentication tokens or external identity-provider secrets.

The F5A source service is an internal infrastructure boundary. User-facing export administration and background-delivery authorization are not introduced by this package.

## Acceptance boundary

F5A acceptance requires repository tests proving:

- deterministic ascending batches;
- bounded reads;
- snapshot-safe cursor advancement;
- canonical filter fingerprints;
- fail-closed filter/checkpoint mismatch;
- stable exported event content across later Security Center review changes;
- no Security Event schema change.

F5A alone is not a reliable-delivery or SIEM-integration claim. That requires F5B.
