# Inventory Replenishment

Use **Purchasing > Replenishment** to review material shortages and create controlled purchase demand.

## What Depot calculates

For each active Item/Warehouse policy, Depot shows on-hand stock, reservations, sales backorders, eligible inbound Purchase Order quantity, projected available quantity, policy thresholds, required quantity, MOQ-rounded suggested quantity and supplier planning evidence.

Select a suggestion to read the complete explanation.

## Blocked suggestions

A Blocked suggestion means Depot has demand but cannot produce a safe actionable result. Common causes are missing supplier-item evidence, multiple matching supplier-item records, or warehouse ambiguity where item-level backorders/open supply cannot be allocated safely.

Correct the policy or master data and recalculate. Depot does not guess.

## Convert to Purchase Requisition

Select one or more Open suggestions and use the conversion action. Depot creates demand in the existing Purchase Requisition workflow. Normal sourcing and approval steps still apply.

Replenishment never awards a supplier and never creates or places a Purchase Order automatically.

## Policy maintenance

Users with `ReplenishmentPolicies.Manage` can maintain per-item/per-warehouse reorder point, safety stock, target stock and optional preferred planning supplier. Policy updates use optimistic concurrency.
