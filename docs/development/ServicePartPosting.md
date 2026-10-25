# Service parts posting

## Authority and transaction boundary

`ServiceManagementService` owns the service order and consumption/return relationship. `MovementService` remains the authority for physical stock, item policy, reason codes, serial/lot allocation and movement audit. The service calls its transaction-aware helpers; no service operation writes an inventory balance directly.

Each new parts operation acquires the service-order write lock before reading lifecycle, operation evidence or remaining return quantity. It then validates the request and commits the stock movement, tracking, service-part evidence, durable operation identity, audit entries and next service-order version in one write transaction. The inventory lock is acquired through the existing provider abstraction. Failure or cancellation before commit rolls back the entire operation.

Completion, cancellation and other order mutations use the same order lock. Every successful parts posting advances the order version, including an already in-progress order. A competing completion that holds an older version fails and must reload; a completed or cancelled order rejects a new parts operation.

## Durable retries

Callers may supply an operation `Guid`; a non-empty identity is required when supplied. `ServicePartOperations` stores that identity uniquely together with a canonical SHA-256 request hash and the created parts-evidence ID. The hash covers the order, kind, quantity, source inventory or consumption, reason, commercial input and normalized, sorted tracking allocations.

Replaying the same identity and request returns the original evidence without another movement, version increment or audit event. Reusing an identity with different content is rejected. The matching replay is resolved before lifecycle validation so an ambiguous success can still be recovered after the order was completed. Authorization is checked on every call.

The workspace retains the operation identity after an unconfirmed result and reuses it for an unchanged request. Tracking-code case, surrounding whitespace and allocation order are normalized for that comparison; quantities and expiry dates remain part of the identity. A confirmed success clears the quantity before refreshing evidence. A refresh failure is reported as a refresh problem after a successful posting. A new request receives a new identity; the application does not infer duplicate business intent from matching quantities alone.

## Return contract

`ReturnPartAsync` takes a concrete original consumption ID. The source must belong to the same order, be consumption evidence, and still identify an unreversed withdrawal with matching inventory and quantity. A return uses the source inventory, item and retained commercial snapshot. It does not accept an arbitrary destination inventory or replacement price.

The service sums previously linked returns under the order lock using 64-bit quantities. The current return cannot exceed the original quantity minus that sum. For tracked items, the same bound also applies independently to each original serial/lot identity. Codes are normalized consistently with the stock authority; supplied expiry dates must match the source, and the posting retains its original expiry value. A different serial/lot or repeated return of an already returned identity is rejected even when another identity still has remaining quantity.

Production- and service-owned withdrawals cannot be reversed through the standalone movement command. Service parts use their bounded return workflow; the production authority provides its own component correction and whole-assembly reversal.

## Schema and existing evidence

Service Management schema 2 adds the operation table with foreign keys to parts evidence and its original consumption. Schema 1 evidence remains unchanged and reads with absent operation/link metadata. The migration does not fabricate retry identities, rewrite stock or attach an older return to a guessed consumption.

A historical consumption with valid unreversed stock evidence can be returned when its service order and location have no unlinked historical returns. If such returns exist, further returns for that order/location fail closed because the individual remaining balances cannot be established reliably. Historical inconsistencies require explicit reconciliation; this correction does not repair them automatically.

The migration also corrects SQL Server's nullable invoice uniqueness contract: several service orders may have no invoice, while a real invoice ID remains unique. The shared schema registry, logical-backup discovery and Manager compatibility evidence pick up Service Management version 2 through their existing authoritative paths. Core, Sales, Finance and Production versions remain unchanged.

## Validation

The ordinary regression suite injects failures at movement audit, service evidence, operation evidence, service audit, order update and final order audit; compares complete relevant persisted state; exercises cancellation before commit and an intentionally lost success response; and verifies matching retries after completion. It covers unmatched/excess returns, both completion/cancellation race orders, competing returns, specific serial/lot identities and a schema 1 upgrade with preserved historical evidence.

Dedicated acceptance cases exercise the transaction, retry, return and lifecycle contract on SQLite, SQL Server, MariaDB and MySQL through the provider workflow. Overlap fixtures observe a real competing write-transaction attempt while the winner is held before commit; they do not claim instrumentation of a particular database row-lock wait. Successful executions must be recorded against the exact validated source in the pull request.

Netting returned quantities in generated service invoices is a separate commercial invariant tracked in issue #132. The posting and return corrections do not claim to resolve that remaining billing issue.
