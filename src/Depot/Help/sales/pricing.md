# Sales Pricing

Customer pricing is managed in **Sales > Pricing**.

## Price lists and scopes

Create a price list with a unique code, display name, scope, currency and optional validity window. A list can intentionally contain only some items. Removing an item-price row makes that item fall back to the next valid scope.

Scopes are **Global**, **Region** and **Customer**. Depot resolves each item Customer → Region → Global. Missing or invalid higher-scope item prices do not block fallback. Customer-specific pricing and Sales Regions are optional.

There can be only one active Global default and one active Regional default per region. Active Customer lists require at least one customer assignment.

## Bulk price generation

The **Bulk price generation** card calculates prices from Item Cost Build-up. Choose the item filter, enter a **Markup %**, select an Apply Mode and choose **Calculate preview**. Nothing is written during Preview.

Available filters are All Active Items, Category, Manufacturer and Selected Items.

The first pricing rule is Percentage Markup:

```text
New price = Calculated Cost × (1 + Markup % / 100)
```

Markup is not Gross Margin. A cost of 100 with 25% Markup becomes 125. Do not interpret the Markup field as a target margin.

Preview shows calculated cost, current target-list price, new price, change and one of `Create`, `Update`, `Skip` or `Error`. Select a preview row to inspect the Cost Components behind its calculated cost.

Errors are fail-closed. A missing preferred supplier cost, missing cost currency or cost/PriceList currency mismatch is shown as an error; Depot never substitutes zero or assumes a 1:1 FX rate.

## Apply Modes

- **Replace calculated prices** creates missing target entries and updates existing entries.
- **Only increase prices** creates missing entries and updates only when the calculated price is greater than the current target price.
- **Only create missing prices** leaves every existing target entry unchanged.

**Apply preview** is available only for a successful Preview. Apply is atomic. If another user changes the target PriceList, a target entry or the cost evidence after Preview, Depot rejects Apply and requires a fresh Preview.

A new PriceList can be staged in the normal PriceList editor and used by Bulk generation without introducing a separate pricing model. Global, Region and Customer scope semantics remain unchanged.

## Item Cost Build-up

The calculated cost originates in **Inventory > Items > Cost build-up**. The Base Cost is currently the active preferred supplier purchase price. Because legacy supplier prices do not store a currency, you must set the item's three-letter ISO cost currency explicitly.

Cost Components may be Absolute or Percentage. Percentage components choose either **BaseCost** or **RunningTotal**. Sequence controls calculation order; equal sequences remain deterministic through the persisted component identity. Optional validity dates and the Active flag control whether a component participates on the calculation date.

## Sales documents

Adding an item to a quote or Sales Order uses the central scoped resolver. Automatically sourced draft lines may refresh while the document is still a draft. Manual prices are not overwritten. Accepted quotes and submitted/finalized documents retain their stored price and source snapshots; Bulk pricing never rewrites historical transactions.

## Permissions

Viewing Item Costs uses existing Item view permission. Managing Cost Components requires Item edit/manage permission. Bulk Preview requires Pricing view plus Item-cost visibility; applying a Preview requires Pricing manage permission. These checks are enforced in services, not only by disabled controls.
