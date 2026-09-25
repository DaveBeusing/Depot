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

## Reversal

Production corrections preserve original movements and evidence. Reversal is represented by compensating Stock Movements linked as production evidence; historical issue/receipt records are not deleted. Reversal fails closed when current stock or serial/lot state makes the compensating movement unsafe.
