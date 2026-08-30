# Scoped Sales Pricing

Updated: 2026-08-29

## Purpose

Depot resolves sales prices through one central `SalesPricingService` boundary. Price lists may contain only the items that differ from a lower-level default; no customer or regional list has to duplicate a complete catalog.

Resolution is performed independently for every item:

```text
Customer price-list item
        ↓ no valid item price
Regional default item
        ↓ no valid item price
Global default item
        ↓ no valid item price
No price available
```

The existence of a higher-scope price list never suppresses fallback for an item that is absent or invalid at that scope.

## Domain model

`SalesPriceList.Scope` uses `Global`, `Region`, or `Customer`. Global has no region or customer binding; Region references an active `SalesRegion`; Customer uses the existing optional `CustomerPriceLists` assignment. An active Customer list requires at least one assignment.

`Customer.SalesRegionId` is optional. A customer without a region skips the regional step. A customer price-list assignment is also optional.

`SalesPriceResult` retains the unit price, discount, source price-list identity and name, scope, currency, and optional region identity. Sales-order and quote lines persist price-source snapshots so later price-list changes do not rewrite historical documents.

## Validity rules

The resolver requires active customer/item/list state, an effective date inside the list validity window, matching currency and valid scope bindings. If no valid result exists, editors retain an explicitly entered manual value and automatic-source metadata is cleared.

## Item cost build-up

Item Cost Build-up is a separate upstream calculation concern and is implemented centrally by `ItemCostCalculationService`. The first Base Cost source is the active preferred `SupplierItem.PurchasePrice`, combined with an explicit Item Cost Profile currency because legacy supplier prices do not contain currency metadata.

Cost Components support `Absolute` and `Percentage`. Percentage components explicitly choose `BaseCost` or `RunningTotal`. Active components valid on the effective date are evaluated by `Sequence`, then persisted component `Id` as a stable secondary sort key.

Missing/ambiguous Base Cost and currency mismatches fail closed. Depot does not use zero as a substitute and does not assume 1:1 FX conversion. See `docs/ITEM_COSTING_AND_BULK_PRICING.md` for the full calculation contract.

## Bulk price generation

`PriceListGenerationService` consumes `ItemCostCalculationService`; it does not contain a second cost formula. The first sales rule is **Percentage Markup**:

```text
SalesPrice = CalculatedCost × (1 + MarkupPercentage / 100)
```

Markup is deliberately distinct from Gross Margin. For Cost 100, 25% Markup produces 125. A 25% Gross Margin would produce 133.33 and is not represented by the current Markup UI.

Bulk selection supports All Active Items, Category, Manufacturer and Selected Items. The target may be an existing PriceList or a newly staged Global, Region or Customer PriceList using the same existing scoped model.

A Preview is mandatory before Apply. It shows Calculated Cost, current target-list price, rule, new price, absolute/percentage change and an action of `Create`, `Update`, `Skip` or `Error`. Selecting a row exposes the Cost Component evidence used by that calculation.

Apply modes are Replace calculated prices, Only increase prices and Only create missing prices. Preview applies the selected mode before any write.

## Concurrency, transactions and Audit

Bulk Apply is all-or-nothing inside one existing provider write transaction. The Preview captures the target PriceList version, relevant PriceListEntry versions and cost evidence versions. Apply reloads and recalculates through the same cost service. A changed list, entry, supplier cost, Cost Profile or Cost Component causes an optimistic concurrency conflict and requires a fresh Preview.

Cost Profile and Cost Component mutations use the existing Audit infrastructure. Successful bulk Apply writes one batch-level audit record containing the target list, Pricing Method, Markup, Apply Mode and Created/Updated/Skipped/Failed counts.

Existing RBAC is reused: item costs use `ItemsView` plus `ItemsEdit`/`ItemsManage`; bulk Preview uses `SalesPricingView` and item-cost visibility; bulk Apply requires `SalesPricingManage`. Authorization is enforced in services.

## Defaults and normal pricing concurrency

At most one active Global default may exist. At most one active Region default may exist for a given region. `SalesPricingService` checks these rules inside the established provider write transaction. Price-list, entry, region and customer-assignment mutations continue to use optimistic `Version` checks.

## Schema and migration

Core schema remains on its existing core migration line. Sales feature schema **10** contains the scoped pricing structures from Sales 9 plus `ItemCostProfiles` and `ItemCostComponents`, including the deterministic `(ItemId, Sequence, Id)` index. The Sales 9 → 10 migration is implemented for SQLite, SQL Server and MySQL/MariaDB through the existing provider abstraction.

The Sales 8 → 9 migration still classifies legacy price lists as Customer scope without changing established item prices. The Sales 9 → 10 migration only adds item-cost structures and therefore does not rewrite existing PriceLists or historical document snapshots.

## Sales document behavior

Quotes and Sales Orders call the central resolver when an item is added or a price is explicitly resolved. Saving a draft refreshes only lines carrying automatic source metadata. Manually entered lines are not overwritten. Accepted quotes and submitted or later Sales Orders retain their stored price/source snapshots.

## Validation

SQLite regression coverage includes deterministic mixed component calculations, component validity/activity, rounding/currency guards, cancellation, Base Cost failure, markup generation, Category/Manufacturer/Selected filters, Apply Modes, missing costs, new scoped PriceList creation and Preview→Apply concurrency. Existing optional provider fixtures verify the Sales 10 item-cost migration on SQL Server and MySQL/MariaDB when their test connection strings are configured. Scoped Customer → Region → Global resolver regression tests remain unchanged.
