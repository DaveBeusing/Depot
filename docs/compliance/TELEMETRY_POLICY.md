# Depot Telemetry and External Service Policy

## Default

Depot must not introduce product telemetry, analytics, crash upload, remote support upload, cloud synchronization, or other background external transmission by default without a documented privacy/security review.

## Gate for future integrations

Before enabling an external service, record:

- provider and endpoints;
- exact fields/events transmitted;
- purpose and necessity;
- default state and user/admin control;
- authentication/secrets model;
- transport security;
- retention and deletion behavior;
- processor/subprocessor implications;
- geographic/data-transfer considerations;
- update to the data inventory and threat model.

Diagnostics should remain local unless an operator explicitly exports or transmits them. Secrets, password hashes, protected configuration and full connection strings must never be included in telemetry payloads.
