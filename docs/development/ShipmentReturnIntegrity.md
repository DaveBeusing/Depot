# Shipment reversal and customer-return integrity

Shipment reversal and customer-return posting share the source shipment as their concurrency authority. Both operations can credit stock, so optimistic versions on the individual return drafts do not protect the shipped quantity by themselves.

## Invariants

- A Customer Return may post only against its own unreversed, posted shipment.
- Each return line must reference a line of that shipment, use the original inventory and have a positive quantity.
- The sum of posted returns plus the new return quantity must not exceed the shipped quantity for any shipment line.
- Any posted return, including a partial return, prevents full shipment reversal.
- A draft return does not prevent shipment reversal; a successful reversal makes all remaining return drafts ineligible to post.
- Rejected actions preserve stock movements, reservations, order and document state, and audit evidence.

## Transaction and lock order

`ShipmentRepository.GetByIdForUpdateAsync` performs a no-op write on the source shipment and then reloads it within the caller's existing write transaction. This takes the supported provider's write lock without advancing the shipment version. The code does not depend on the provider's affected-row count for a no-op update; existence comes from the subsequent read.

Customer-return posting resolves the immutable source shipment ID before opening its write transaction. It then acquires the shipment lock before reloading the return header and lines. The authoritative in-transaction read revalidates the source relationship, return version, return status and shipment lifecycle. This source-first order avoids taking locks on competing return documents before acquiring their shared authority.

Posted return totals are read from `CustomerReturnRepository` after the shipment lock is held. All line identities and remaining quantities are validated before the first stock movement is written. Quantities are aggregated using 64-bit arithmetic. Stock movements, traceability evidence, return status/version and audit evidence remain within the existing transaction.

Shipment reversal acquires the same source lock before checking version, status, posted-return evidence and the existing invoice constraints. If a posted return exists, reversal fails before restoring stock or reservations. Return-draft creation also uses this source lock when calculating the remaining quantity.

The lock lasts until commit or rollback. The existing provider transaction retry policy still applies to transient write conflicts. No schema migration, new version column or product-version boundary is required.

## Regression coverage

`SalesWorkflowIntegrationTests` covers preexisting competing return drafts, partial and full returns, reversal before draft posting, quantities on multiple shipment lines, rollback after an audit failure and concurrent return/reversal orderings. Controlled concurrency cases hold the first transaction immediately before commit while starting the competing write. Rejection tests compare persisted stock, reservation, order, shipment, return and audit state.

These source invariants also require the independently provisioned SQLite, SQL Server, MariaDB and MySQL provider acceptance scenarios. A provider-neutral SQL expression alone is not acceptance evidence for a server provider.

Existing restrictions remain: this change does not add a Customer Return reversal workflow, change commercial Credit Note behavior, or repair historical data that was already double-credited before the guard was introduced.
