# Item Cost Build-up and Bulk Pricing

Updated: 2026-08-29

## Purpose

Depot separates purchase/base cost, calculated landed cost, and sales price. Cost build-up is a central business capability, not a UI-only percentage operation.

```text
Preferred supplier purchase price
        ↓
ItemCostCalculationService
        ↓
ordered ItemCostComponents
        ↓
Calculated Item Cost
        ↓
PriceListGenerationService
        ↓
Percentage Markup
        ↓
Preview
        ↓
atomic Apply
        ↓
existing Global / Region / Customer PriceList
```

The pricing resolver itself remains unchanged: Customer → Region → Global is still evaluated per item.

## Base Cost

For the first version, **Base Cost** is the active preferred `SupplierItem.PurchasePrice` for the item. The source is explicit through `ItemCostProfile.BaseCostSource` and is intentionally extensible for later cost sources.

Legacy supplier/purchase-order prices in Depot do not carry a currency. Depot therefore requires an explicit three-letter ISO currency on the item's cost profile. This is an assertion about the preferred supplier purchase price, not an implicit EUR default.

If no active preferred supplier price exists, if more than one preferred source is present, or if the cost currency differs from the target PriceList currency, calculation fails closed. Depot does not assume a 1:1 FX rate. A future FX service may extend the cost-source strategy without changing the cost-component formula.

Inventory/FIFO valuation remains an accounting valuation source and is not silently substituted for purchasing Base Cost in this version.

## Cost Components

An `ItemCostComponent` belongs to one item and contains a name, calculation type, value, calculation base, sequence, active flag, optional validity window and optimistic `Version`.

Supported calculation types:

- **Absolute Cost** adds the configured monetary value.
- **Percentage Cost** calculates a percentage against an explicit base.

Supported percentage bases:

- **BaseCost** — percentage applies only to the original Base Cost.
- **RunningTotal** — percentage applies to the accumulated cost immediately before this component.

Example:

```text
Base Cost                     1,000.00
10 Freight Absolute             +50.00
20 Customs 4% BaseCost          +40.00
30 Handling Absolute            +15.00
40 Overhead 3% RunningTotal     +33.15
--------------------------------------
Calculated Cost              1,138.15
```

Active components valid on the effective date are ordered by `Sequence`, then by persisted component `Id`. This stable secondary key makes equal sequences deterministic without depending on database or UI ordering. Inactive, not-yet-valid and expired components are ignored.

## Rounding and currency

Amounts are calculated with `decimal`. Each applied monetary component and running total uses deterministic currency precision with `MidpointRounding.ToEven`: known zero-decimal and three-decimal ISO currencies use their standard precision; other currencies use two decimals.

Absolute components are denominated in the Item Cost Profile currency. Mixed currencies are never added. Bulk generation requires the calculated cost currency to match the target PriceList currency; otherwise the Preview row is an error.

## Markup versus Gross Margin

Depot's first bulk pricing rule is **Percentage Markup**:

```text
Sales Price = Calculated Cost × (1 + Markup / 100)
```

For Cost 100 and Markup 25%, Sales Price is 125.

**Gross Margin is not Markup.** A 25% Gross Margin on Cost 100 would require `100 / (1 - 0.25) = 133.33`. The current UI and domain use the word Markup only. Target Gross Margin is a future pricing rule and must use a distinct formula and label.

## Bulk filtering

Bulk generation supports existing Depot master data only:

- All Active Items;
- Category;
- Manufacturer;
- Selected Items.

Candidates must be active and have active lifecycle status. No new master-data classifications are created solely for bulk pricing.

## Preview and Apply

`PriceListGenerationService` always calculates a Preview before Apply. Preview includes item, calculated cost, current target-list price, markup, new price, absolute/percentage change, action and any error. Selecting a row exposes the component evidence used to derive its cost.

Missing or ambiguous Base Cost and currency mismatches produce `Error`, never a zero cost.

Apply modes:

- **Replace calculated prices** — Create missing entries and Update existing entries.
- **Only increase prices** — Create missing entries; Update only when the new price is greater; otherwise Skip.
- **Only create missing prices** — Create missing entries; existing entries are Skip.

Apply executes inside one existing provider write transaction. PriceList generation is therefore all-or-nothing. Audit failure or any business/concurrency failure rolls the transaction back.

## Concurrency and evidence

Preview captures target PriceList version, existing PriceListEntry versions and a cost evidence token containing the Cost Profile, preferred SupplierItem and effective Cost Component versions. Apply reloads those records within the write transaction and recalculates using `ItemCostCalculationService`.

If the PriceList, an entry or any cost evidence changed after Preview, Apply fails with an optimistic concurrency conflict. The user must calculate a fresh Preview. Preview and Apply never use separate pricing formulas.

Historical Sales Quotes, Sales Orders and finalized documents retain their existing immutable price-source snapshots. Bulk generation changes the target PriceList only; it does not retroactively rewrite historical business documents.

## RBAC and Audit

No duplicate permission model is introduced:

- Item cost viewing uses `ItemsView`.
- Item cost maintenance requires `ItemsEdit` or `ItemsManage`.
- Bulk Preview requires `SalesPricingView` plus `ItemsView`.
- Bulk Apply requires `SalesPricingManage` plus item-cost visibility.

Authorization is enforced in the service layer. Cost Profile and Component changes use the existing structured Audit infrastructure. A completed bulk Apply records one batch-level `PriceListGenerationAuditRecord` with target list, pricing method, Markup, Apply Mode and Created/Updated/Skipped/Failed counts rather than generating redundant batch audit rows per item.

## Persistence and provider neutrality

Sales feature schema **10** adds:

- `ItemCostProfiles`;
- `ItemCostComponents`;
- `(ItemId, Sequence, Id)` lookup/order index.

The migration has provider-specific DDL through the established database abstractions for SQLite, SQL Server and MySQL/MariaDB. Repository operations continue through `DatabaseAccess` and existing transaction sessions; no provider-specific SQL is placed in ViewModels or Services.

## Extension points

The architecture intentionally leaves room for additional Base Cost sources, controlled FX conversion, target Gross Margin and commercial rounding rules such as 0.05/0.10/0.50 or .99 endings. These are not simulated by the current implementation; unsupported currency conversion fails closed.
