# Light Assembly and Production

## Boundary

Depot Production is a deliberately bounded light-assembly capability for businesses that combine stocked components into stocked finished goods. It is not an MRP, routing, work-center, capacity-planning, labor-costing or shop-floor execution subsystem.

V1 uses single-level BOM explosion only. A component may have its own BOM, but that BOM is not recursively expanded by a parent order. Direct and graph cycles are rejected when BOMs are maintained so the model can evolve safely without ambiguous product structures.

## Authorities

Production follows the standard dependency direction:

`View -> ViewModel -> ProductionService -> ProductionRepository -> DatabaseAccess`

Physical stock remains authoritative in Inventory and Stock Movements. Production never updates inventory quantities directly. Serial/lot validation remains owned by Item Traceability. Component cost references remain owned by Item Costing. Finance Inventory Accounting and General Ledger remain authoritative for accounting consequences; Production stores operational assembly cost evidence and does not create a second costing ledger.

Shortages are projections. They do not create purchases automatically. An explicit user action may ask the existing Replenishment authority to recalculate the affected item/warehouse policy and may then use the established Purchase Requisition flow.

## Persisted contract

Production feature schema **1** owns:

- revisioned BOM headers and component lines;
- assembly order lifecycle and immutable released requirement snapshots;
- links from assembly operations to authoritative Stock Movement records;
- deterministic component-cost evidence retained at completion.

Released requirement snapshots retain component identity, descriptions, UOM labels and quantities so later BOM edits or revisions do not rewrite order history.

## Quantity semantics

BOM quantities are decimal so product definitions can retain explicit quantities. The current Inventory movement authority posts integral base-unit quantities. Release therefore fails closed when `BOM quantity × planned order quantity` is not a positive whole base-unit quantity. Unit conversions are not guessed.

Component issue uses the free quantity of the selected inventory: current on-hand minus all active Sales reservations. The check runs after acquiring the same provider inventory lock used by Sales reservation and stock posting. The availability display is a projection; posting always reloads the authoritative quantities. Both stock and reservation totals use 64-bit aggregation so large totals cannot overflow an individual movement's quantity type.

## Reversal

Production corrections preserve original movements and evidence. Reversal is represented by compensating Stock Movements linked as production evidence; historical issue/receipt records are not deleted. Reversal fails closed when current stock or serial/lot state makes the compensating movement unsafe.

The generic Stock Movements reversal rejects withdrawals linked to Production or Service evidence. These movements must be corrected by their owning workflow, where stock and source state share one transaction. The shared transaction-level reversal helper is internal to the application services.

Before completion, **Correction → Reverse selected component issue** reverses one complete original issue. The production operation restores its original tracking identities, appends `ComponentIssueReversal` evidence, reduces the requirement's issued quantity and records the order change atomically. Completion is blocked until replacement components have been issued. A repeated correction operation with the same key, original movement and reason returns the existing result; changing the original or reason for that key is rejected.

After completion, correction reverses the complete assembly. It compensates the finished-goods receipt and component issues that have not already been corrected through Production. Previously corrected originals remain in history and are not credited again. The finished-goods receipt anchors whole-order reversal replay even when the earliest component issue has been corrected. Historical standalone reversals without matching Production correction evidence are not treated as valid prior corrections.

Issue, component correction, completion and complete reversal acquire the Production order header lock before inventory locks. State, operation replay and requirement counters are reloaded under that lock. Successful component changes advance the order version. These rules use the existing Production schema 1 contracts; no new persisted Production columns or movement kinds are required.


## Workspace and My Work

The **Production** workspace exposes BOM revision maintenance and assembly-order execution. Order detail separates material requirements, availability/shortages, controlled issue, completion/cost evidence and correction.

Owned production orders participate in **My Work**:
- drafts remain visible as drafts;
- current shortages are surfaced as exceptions;
- released/in-progress orders without shortages are surfaced for material issue;
- fully issued orders are surfaced as ready for completion.

The Warehouse Operator system role receives the bounded Production permissions. Stock posting/reversal permissions and Replenishment authorization remain independently enforced by the reused services.

## Permissions

- `Production.View` — view BOMs, production orders, availability and evidence.
- `BillsOfMaterial.Manage` — create draft BOM revisions and activate them.
- `ProductionOrders.Manage` — create/release production orders and explicitly hand shortages to Replenishment.
- `ProductionOrders.Issue` — issue components; existing Stock Movement posting authorization is also required.
- `ProductionOrders.Complete` — receive the finished quantity; existing Stock Movement posting authorization is also required.
- `ProductionOrders.Reverse` — correct an issued component before completion or request a complete assembly reversal; existing Stock Movement reversal authorization is also required.

## Provider and regression validation

Production schema 1 is provisioned through the normal database initialization path for SQLite, SQL Server and the shared MariaDB/MySQL provider infrastructure. Provider acceptance verifies the feature version and all Production tables. Focused regression tests cover BOM revision stability, cycle rejection, issue/completion idempotency, retained cost evidence, source-owned component correction, complete reversal after reissue, reservation-aware issue and rollback after a final audit failure. Lot-tracking regression verifies that correction restores the issued tracking identity. Real-provider cases also exercise the reservation/issue ordering under the shared inventory lock; SQLite alone does not establish server locking behavior.
