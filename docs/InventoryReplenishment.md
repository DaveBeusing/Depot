# Inventory Replenishment

## Purpose

Inventory Replenishment converts authoritative stock demand and eligible inbound supply into deterministic, explainable purchase suggestions. Suggestions are planning evidence only; Depot never creates a Purchase Order or supplier award automatically.

## Authority and workflow

```text
Inventory + Reservations + Backorders + Open Supply
  -> Replenishment Policy
    -> Requirement Snapshot
      -> Purchase Suggestion
        -> User Review
          -> Purchase Requisition
```

Existing Inventory, Sales, Purchasing and SupplierItem data remain authoritative. Accepted suggestions enter the existing Procurement Sourcing Purchase Requisition workflow.

## Policy and deterministic calculation

Policies are scoped per Item and Warehouse and contain active state, reorder point, safety stock, target/order-up-to stock, optional preferred planning supplier and an optimistic-concurrency version.

```text
ProjectedAvailable = OnHand - Reserved - Backordered + EligibleInbound
Required = ProjectedAvailable <= ReorderPoint
  ? max(0, TargetStock - ProjectedAvailable)
  : 0
```

When replenishment is required, the quantity is rounded up to the active SupplierItem MOQ. The immutable snapshot retains all calculation inputs, supplier evidence, blocking reason and explanation. A SHA-256 snapshot key makes unchanged reruns idempotent.

Current backorders and open Purchase Order supply are item-level. If multiple warehouse policies exist for the same item while item-level demand or inbound supply is non-zero, Depot blocks the suggestion rather than inventing a warehouse allocation.

## Supplier evidence

A policy may name a preferred supplier; otherwise the active preferred SupplierItem relation is used. Exactly one active matching SupplierItem is required when replenishment is needed. Lead time and MOQ are retained as planning evidence. Missing or ambiguous evidence creates a visible Blocked suggestion and never awards a supplier.

## Suggestion lifecycle and conversion

Suggestions use Open, Accepted, Dismissed, Superseded and Blocked states. Changed snapshots explicitly supersede prior Open/Blocked suggestions. Unchanged reruns reuse the existing snapshot/suggestion.

Only Open suggestions can be converted. Conversion is explicit, transactional and idempotent, goes through `ProcurementSourcingService`, retains suggestion/warehouse evidence on requisition lines and records the created Purchase Requisition on accepted suggestions.

No Purchase Order commitment is created by replenishment.

## Permissions and UI

Permissions are separated as `Replenishment.View`, `ReplenishmentSuggestions.Manage` and `ReplenishmentPolicies.Manage`. The Purchasing workspace, Buyer Workbench and My Work expose bounded actionable shortages and explanations. Service-layer authorization remains authoritative.

## Persistence, providers and operations

Inventory Replenishment feature schema **1** is tracked as `InventoryReplenishment` in `DepotFeatureVersions`; Core schema is unchanged. The persistence contract applies to SQLite, SQL Server, MariaDB and MySQL.

Batch recalculation is bounded to 500 policies and conversion to 100 suggestions per action. Requirement discovery uses set-based aggregate reads.

This feature is not full MRP/MRP II, forecasting, manufacturing planning, cross-company planning, warehouse optimization or autonomous purchasing.
