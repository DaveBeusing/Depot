# Light Assembly and Production Orders

Use **Production** for bounded light assembly: define a revisioned Bill of Material (BOM), create and release an assembly order, review material availability, issue real components, complete the assembly into finished stock and retain traceability/cost evidence.

## Bills of Material

A BOM revision belongs to one finished stock item. Draft revisions can be edited. Activating a revision makes it available for new production orders and preserves it as historical product-structure evidence.

Component quantities must be positive. A finished item cannot contain itself, duplicate component lines are rejected, and component graphs that would create a cycle are blocked. V1 uses a single-level BOM explosion; component BOMs are not recursively expanded by the parent order.

Released revisions are immutable. Create a new revision instead of editing historical structure.

## Create and release an assembly order

Choose an active BOM, production warehouse, finished-goods inventory location and planned quantity. Releasing the order snapshots the required component identities, descriptions, UOM labels and quantities. Later BOM changes do not rewrite the released order.

Inventory movements use integral base-unit quantities. If a BOM quantity multiplied by the planned quantity cannot be represented as a positive whole base-unit quantity, release fails rather than guessing a conversion.

## Material availability and shortages

The **Availability** view compares each released requirement with the current authoritative stock/reservation position.

A shortage is informational until you explicitly choose **Send shortages to replenishment**. That action asks the existing Replenishment workflow to recalculate the affected item/warehouse demand. It does not create a Purchase Order or automatically award a supplier.

## Issue components

Select a remaining requirement and an inventory location in the production warehouse, then post the required quantity. The operation uses the normal Stock Movement authority; Production does not edit inventory quantities directly.

For tracked items, enter one lot code for the moved lot quantity or one unique serial code per unit. Existing Item Traceability validation remains authoritative.

Over-issue is rejected. Retrying the same production operation remains idempotent at the service boundary.

## Complete the assembly

Completion is available only after every released component requirement has been issued exactly. The finished quantity is received through the normal Stock Movement authority into the selected finished-goods inventory location.

At completion, Depot resolves authoritative Item Costing evidence for every component, stores the unit/extended cost and evidence version, and shows the resulting total/unit component cost. Missing cost evidence or mixed component currencies fail closed.

## Correct a completed assembly

Use **Correction** to reverse a completed assembly. Depot creates compensating Stock Movements and keeps the original component issues, finished receipt and cost evidence. Reversal is rejected when later stock or traceability state makes a safe compensation impossible.

## My Work

Owned production work can appear as a draft, a material-shortage exception, material-ready work or ready-for-completion work. Opening the item navigates directly to the Production order.

## Permissions

Production uses separate view, BOM-manage, order-manage, issue, complete and reverse permissions. Stock posting/reversal and Replenishment permissions remain independently enforced where those authorities are used.

## Product boundary

Production V1 does not implement MRP/MRP II, routings, work centers, machine scheduling, finite-capacity planning, labor/payroll costing, shop-floor telemetry, quality management, subcontract manufacturing or process manufacturing.
