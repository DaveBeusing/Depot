# Security Event Export and Delivery

Updated: 2026-09-16

## Scope

F5A defines the provider-neutral source contract for exporting Depot Security Events. F5B adds durable target configuration, delivery state, retry/suspension semantics and the first concrete HTTPS JSON sink.

The implementation continues to read the existing `SecurityEvents` evidence. Delivery state is stored separately and never mutates source Security Event rows.

## Export contract

`SecurityEventExportBatch` format version `1` contains a bounded, ascending sequence of immutable event-time `SecurityEventExportRecord` values. Security Center review metadata and the mutable database row version are excluded from exported event meaning.

The filter contains minimum severity plus an optional Security Event type allow-list. It is normalized and bound to a canonical SHA-256 fingerprint. `SecurityEventExportCheckpoint` combines the last consumed source ID with that fingerprint, so a cursor cannot silently cross filter semantics.

Reads are ordered by monotonic Security Event ID and capture a source snapshot upper bound. F5B can also request a previously persisted fixed snapshot upper bound, allowing an interrupted delivery to reproduce the same batch even if newer Security Events arrive before the retry.

## Durable delivery state

Security Events feature schema `3` adds two provider-neutral tables:

- `SecurityEventExportTargets` for stable target code, sink code, HTTPS endpoint, normalized filter, batch size and enabled state;
- `SecurityEventExportDeliveryState` for durable checkpoint, in-flight snapshot, retry/suspension evidence, last attempt/success/failure state and a bounded worker lease.

Target configuration and source evidence are deliberately separate. No delivery metadata is written into `SecurityEvents`.

A filter change resets that target's checkpoint to the beginning under the new filter fingerprint. Depot chooses replay over silently skipping historical source evidence under changed selection semantics. Endpoint changes do not implicitly reinterpret the source cursor.

## Delivery guarantee

F5B provides an at-least-once delivery boundary:

1. acquire a short database-backed worker lease;
2. read or reconstruct the bounded batch;
3. persist the batch snapshot upper bound before invoking the sink;
4. invoke the sink;
5. advance the durable checkpoint only after the sink reports success;
6. clear the in-flight snapshot only together with successful checkpoint advancement.

If the process stops after the receiver accepted a request but before Depot commits the checkpoint, the same batch can be delivered again. This intentional duplicate possibility avoids event loss. Receivers should deduplicate using `X-Depot-Delivery-Id`, which is deterministic for target, filter, start checkpoint, next checkpoint and snapshot.

An empty filtered batch has no external side effect and may advance its source checkpoint directly to the exhausted snapshot.

## Retry and suspension

Transient failures keep the in-flight snapshot and schedule deterministic bounded backoff:

- first failure: 30 seconds;
- second: 2 minutes;
- third: 10 minutes;
- fourth: 30 minutes;
- later failures: 2 hours.

HTTP 408, 429, 5xx, transport failures and request timeout are transient. Other non-success 4xx responses are permanent configuration/protocol failures and suspend automatic delivery until an administrator resumes the target after correcting configuration.

Failure state stores only bounded operational diagnostics: classification, controlled code and truncated message. HTTP response bodies, credentials, tokens and secrets are not persisted.

## HTTPS JSON sink

F5B introduces sink code `http-json-v1`.

The endpoint must be an absolute HTTPS URI and must not contain embedded user information. The request body contains export format version, target code, snapshot/checkpoints and the bounded event array. `X-Depot-Delivery-Id` supplies the deterministic retry identity and `X-Depot-Export-Format` identifies the payload contract version.

This package does not persist API keys, bearer tokens, client certificates or other sink credentials. Deployment-specific authenticated transports require a separately controlled credential/integration boundary rather than secrets in Depot's delivery tables.

## Concurrency and retention

A database-backed lease prevents normal concurrent Depot workers from processing the same target at the same time. Expired leases are recoverable after process failure.

Security-event retention observes the lowest checkpoint of enabled export targets. Events that an active target has not consumed are therefore not removed by the normal Security Event retention worker. Disabling a target removes it from this retention floor; operators should treat disabling as suspending the delivery guarantee for source data that later ages out under the configured retention policy.

## Administration and Audit

Viewing configured export status reuses `SecurityEventsView`. Creating/updating targets and resuming a suspended target require `SecurityEventsManage`.

Target create/update operations are recorded through the existing Audit boundary in the same transaction as the configuration mutation. Target codes are stable after creation. Changing filter semantics resets the durable cursor deliberately and transactionally.

## Privacy boundary

Export includes only fields already present in Depot Security Events. It does not add source IP, geolocation, device fingerprint, typed input, external-window activity, authentication tokens, identity-provider tokens or sink secrets.

## Acceptance boundary

F5B repository acceptance requires evidence for:

- schema `2` to `3` migration and idempotence;
- target/state persistence on SQLite, SQL Server, MariaDB and MySQL;
- checkpoint advancement only after sink success;
- exact fixed-snapshot retry after a transient failure while newer events arrive;
- permanent-failure suspension;
- transient/permanent HTTP classification;
- bounded batch and retry behavior;
- retention protection for unconsumed events;
- permission, optimistic-concurrency and Audit boundaries for administration.

F5B provides the durable delivery mechanism and a generic HTTPS JSON adapter. It does not certify any third-party SIEM product or deployment-specific authentication configuration.
