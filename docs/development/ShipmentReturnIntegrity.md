# Shipment quantities, reversal and customer-return integrity

Shipment reversal and customer-return posting share the source shipment as their concurrency authority. Both operations can credit stock, so optimistic versions on the individual return drafts do not protect the shipped quantity by themselves.

Shipment posting and reversal also share the Sales Order as the authority for cumulative fulfillment quantities and order status. A single order line can appear on several shipment lines because its reservations are split across inventories.

## Invariants

- Posting aggregates every shipment contribution by `SalesOrderLineId`; reversal subtracts the same complete contribution. A 4 + 6 split produces 10 shipped and zero reserved units, and reversal restores all 10 reserved units.
- Each affected order line receives one version-checked quantity update per posting or reversal. Quantities belonging to other shipments and the current invoiced quantity are preserved.
- Posting revalidates the current active reservation identity and remaining quantity for every shipment line. Draft shipment creation does not consume or independently reserve those quantities.
- Reversal restores quantities per order line and inventory, merging into an existing active reservation where present. This preserves the active reservation uniqueness invariant and the identity used by a pending partial-shipment draft.
- A Customer Return may post only against its own unreversed, posted shipment.
- Each return line must reference a line of that shipment, use the original inventory and have a positive quantity.
- The sum of posted returns plus the new return quantity must not exceed the shipped quantity for any shipment line.
- Any posted return, including a partial return, prevents full shipment reversal.
- A draft return does not prevent shipment reversal; a successful reversal makes all remaining return drafts ineligible to post.
- Rejected actions preserve stock movements, reservations, order and document state, and audit evidence.

## Transaction and lock order

`ShipmentRepository.GetByIdForUpdateAsync` performs a no-op write on the source shipment and then reloads it within the caller's existing write transaction. This takes the supported provider's write lock without advancing the shipment version. The code does not depend on the provider's affected-row count for a no-op update; existence comes from the subsequent read.

Shipment posting and reversal then use `SalesOrderRepository.GetByIdForUpdateAsync` to lock and reload the current order and its lines. The order lock also leaves the header version unchanged. Their write order is shipment, Sales Order, then inventories in ascending ID order through the existing provider lock abstraction. The inventory lock is shared with sales reservation changes and production component issues. Provider transaction retries continue to handle transient lock conflicts.

All shipment quantities are aggregated with 64-bit arithmetic and checked against the current reserved, shipped and ordered quantities before stock is written. Posting validates active reservations after acquiring the inventory locks. It persists each stock movement, traceability/accounting contribution and reservation consumption as before, then writes each affected order line once using its expected version. Order status comes from the resulting complete line state. Reversal follows the same grouped counter update and merges restored reservations with a version check. Stock, reservations, line counters, document status and audit entries commit or roll back together.

Customer-return posting resolves the immutable source shipment ID before opening its write transaction. It then acquires the shipment lock before reloading the return header and lines. The authoritative in-transaction read revalidates the source relationship, return version, return status and shipment lifecycle. This source-first order avoids taking locks on competing return documents before acquiring their shared authority.

Posted return totals are read from `CustomerReturnRepository` after the shipment lock is held. All line identities and remaining quantities are validated before the first stock movement is written. Quantities are aggregated using 64-bit arithmetic. Stock movements, traceability evidence, return status/version and audit evidence remain within the existing transaction.

Shipment reversal acquires the same source lock before checking version, status, posted-return evidence and the existing invoice constraints. If a posted return exists, reversal fails before restoring stock or reservations. Return-draft creation also uses this source lock when calculating the remaining quantity.

The lock lasts until commit or rollback. The existing provider transaction retry policy still applies to transient write conflicts. No schema migration, new version column or product-version boundary is required.

## Regression coverage

`SalesWorkflowIntegrationTests` covers preexisting competing return drafts, partial and full returns, reversal before draft posting, quantities on multiple shipment lines, rollback after an audit failure and concurrent return/reversal orderings. Controlled concurrency cases hold the first transaction immediately before commit while starting the competing write. Rejection tests compare persisted stock, reservation, order, shipment, return and audit state.

The same suite covers 4 + 6 reservation splits across bins, mixed order lines sharing one bin, partial reversal with an active reservation remainder, preexisting shipment drafts, audit failures during split posting/reversal, two concurrent split postings/reversals and both orderings of posting one shipment while reversing another. It verifies physical stock, complete order counters, line versions, status and active reservation quantities.

`SalesSplitShipmentProviderAcceptanceTests` repeats the split, draft, rollback and overlap scenarios on SQLite, SQL Server, MariaDB and MySQL. Its fault injection runs after the complete domain operation but before transaction commit. The concurrency helper observes a competing write-transaction attempt while the first transaction is held before commit; it does not instrument arrival at an individual row lock. These scenarios use untracked stock and do not establish positive coverage of the optional traceability or inventory-accounting integrations.

These source invariants also require the independently provisioned SQLite, SQL Server, MariaDB and MySQL provider acceptance scenarios. A provider-neutral SQL expression alone is not acceptance evidence for a server provider.

Existing restrictions remain: this change does not add a Customer Return reversal workflow, change commercial Credit Note behavior, or repair historical data that was already double-credited before the guard was introduced.

Previously inconsistent shipment/order counters are likewise not silently repaired; current quantity checks reject inconsistent changes so the retained movement and document evidence can be reconciled explicitly.
