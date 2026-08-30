# Item Traceability

## Purpose

Depot supports serial-number and lot/batch traceability as part of the physical inventory ledger. Traceability is not a second stock system: the current quantity and location of a serial number or lot are derived from posted `StockMovements` and their immutable tracking allocations.

## Item master data

Physical stock items can use one of three tracking modes:

- `None` — no serial/lot allocation is required.
- `SerialNumber` — every moved unit must be assigned exactly one unique serial number with quantity 1.
- `LotNumber` — every moved quantity must be fully allocated to one or more lot numbers.

Only `StockItem` items may participate in physical inventory movements. Lifecycle data is evaluated by the operational services: `Discontinued` and `Obsolete` items are blocked for new purchase and sales orders, `LastBuyDate` blocks purchase orders after the configured date, and replacement items are included in validation messages. End-of-life/end-of-support states remain visible operational warnings where continued stock handling is still valid.

## Persistence model

`ItemTrackingUnits` stores the identity and mutable control state of a serial/lot unit: item, tracking mode, normalized code, optional expiry date, block state/reason and optimistic-concurrency version.

`StockMovementTracking` links a posted stock movement to one or more tracking units with a signed quantity. The allocation sign follows the movement sign. Current tracked stock is therefore the sum of allocations, grouped by tracking unit and inventory.

This design deliberately avoids storing a separate current-location/current-quantity column for tracking units.

## Capture format

Workflow grids expose a transient **Serial / lot** column. The capture is not persisted with a draft; it is validated and written only inside the posting transaction.

Use one entry per line:

- Serial without expiry: `SN-001`
- Serial with expiry: `SN-001|2027-12-31`
- Lot without expiry: `LOT-4711|25`
- Lot with expiry: `LOT-4711|25|2027-12-31`

Multiple serials/lots are entered on separate lines. The presentation parser accepts the generic format, while the posting service validates the authoritative item tracking mode and movement quantity. A serial therefore always resolves to quantity 1 even though a lot can carry a larger quantity.

The same syntax is available in **Inventory > Movements > New movement** for manual purchases, withdrawals and corrections.

## Integrated workflows

Tracked allocations participate in the same transaction as these physical stock workflows:

- goods receipts
- stock transfers
- inventory-count corrections
- material issues
- material returns
- supplier returns
- sales shipments
- customer returns
- manual purchase/withdrawal/correction movements
- reversals of the resulting immutable stock movements

Excel opening-balance import intentionally remains fail-closed for tracked items because the legacy import format has no serial/lot allocation columns. Tracked opening balances must be posted through a traceable movement path rather than fabricating tracking identity.

## Posting rules

For tracked items, the sum of tracking allocations must equal the absolute movement quantity. Serial allocations always have quantity 1. Codes are trimmed, normalized to uppercase and unique per item/tracking mode.

Inbound posting may create a tracking unit. A serial number can only be inbound while its global on-hand balance is zero. Existing lots can be received again. An expiry date can be populated when previously missing but cannot silently conflict with an existing date.

Outbound posting requires the selected serial/lot to exist at the exact source inventory with sufficient quantity. Blocked and expired units cannot be issued.

When an existing workflow still invokes its compatibility posting overload, the traceability layer resolves the transient UI capture only for the movement type, source/destination inventory and absolute movement quantity. Ambiguous matches are rejected instead of guessing a serial/lot assignment. Explicit service allocations always take precedence.

## Inventory traceability browser

**Inventory > Overview > Serial / lot traceability** provides a searchable balance browser. Search covers part number, serial/lot code, warehouse and storage location. Selecting a balance loads the complete movement history for the tracking unit, including direction, quantity, warehouse/location and document reference.

Users with `Items.Manage` may block a serial/lot unit with a mandatory reason and later unblock it. Blocking changes no quantity; it only prevents outbound use. The block transition uses optimistic concurrency and is audited with before/after state.

## Reversals

Reversal movements copy the exact tracking allocations of the original movement with inverted signs. A reversal that would require a serial/lot which is no longer available at the original inventory fails before posting. This keeps document reversal and traceability atomic.

## Concurrency and transactions

Tracking validation, movement creation, allocation creation and business-document state changes run inside the same database transaction. Inventory rows and document versions continue to use the existing locking/optimistic-concurrency conventions. A failure rolls back both the stock movement and its tracking allocations.

## Permissions and audit

Viewing traceability uses `Items.View`. Blocking or unblocking a tracking unit uses `Items.Manage`. Block-state changes are audited with before/after data. Business-document posting and reversal continue to use their existing RBAC permissions and audit records.

## Provider neutrality

The schema and repository layer are implemented through Depot's provider-neutral database access and are supported for SQLite, SQL Server and MySQL/MariaDB. Provider-specific behavior remains inside the database abstraction and initialization layer.

## Operational guidance

When receiving serial-tracked stock, enter one serial number per physical unit. For lot-tracked stock, allocate the complete received quantity across the supplier lots and record expiry dates when applicable. Before outbound posting, select only units physically present at the source inventory. Do not work around a blocked or expired unit with a manual stock adjustment; resolve the block/expiry decision explicitly so the audit trail remains intact.

## Invariants

1. A tracked physical movement is never partially allocated.
2. A serial allocation always represents exactly one unit.
3. A serial number cannot have a global on-hand balance greater than one.
4. Outbound tracked quantity cannot exceed the tracked balance at the source inventory.
5. Blocked or expired tracking units cannot be posted outbound.
6. Reversals preserve the original tracking identity.
7. Current tracked stock is derived from movements, never maintained independently.
8. Tracking writes are transactional with their business document and stock movement.
9. Ambiguous transient UI captures fail closed rather than being assigned heuristically.
10. Non-stock item types cannot enter physical stock workflows.
