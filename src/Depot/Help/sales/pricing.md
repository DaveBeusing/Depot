# Sales Pricing

Customer pricing is managed in **Sales > Pricing**.

## Price lists and scopes

Create a price list with a unique code, display name, scope, currency and optional validity window. A list can intentionally contain only some items. Removing an item-price row makes that item fall back to the next valid scope.

Scopes are **Global**, **Region** and **Customer**. Depot resolves each item Customer → Region → Global. Missing or invalid higher-scope item prices do not block fallback. Customer-specific pricing and Sales Regions are optional.

There can be only one active Global default and one active Regional default per region. Active Customer lists require at least one customer assignment.

## Advanced bulk price generation

The **Advanced bulk price generation** card calculates target prices from Item Cost Build-up. Choose the item filter, select the pricing method, enter its percentage, choose the rounding strategy and Apply Mode, then choose **Calculate preview**. Nothing is written during Preview.

Available filters are All Active Items, Category, Manufacturer and Selected Items.

### Percentage Markup

```text
Sales Price = Converted Cost × (1 + Markup % / 100)
```

A cost of 100 with 25% Markup becomes 125.

### Target Gross Margin

```text
Sales Price = Converted Cost / (1 - Gross Margin % / 100)
```

A cost of 100 with a 25% target Gross Margin becomes 133.33 before any additional commercial rounding. Gross Margin must be below 100%. Markup and Gross Margin are intentionally separate methods.

## FX conversion

When the item-cost currency differs from the target PriceList currency, Depot requires a direct effective-dated FX record for that exact direction.

Each FX record stores:

- source currency;
- target currency;
- effective date;
- rate source;
- rate;
- version.

Depot selects the most recent rate whose effective date is on or before the pricing date. It does not silently invert a reverse rate, triangulate through another currency or assume 1:1. Missing FX evidence produces a Preview error.

Manage these records in **Pricing exchange rates**. Updating a rate increments its version. If a rate changes after Preview, Apply is rejected and a fresh Preview is required.

## Commercial rounding

Supported strategies are:

- Currency Precision — normal target-currency precision;
- Nearest 0.01;
- Nearest 0.05;
- Nearest 0.10;
- Nearest 0.50;
- Ending .99.

Increment rounding uses deterministic midpoint-away-from-zero rounding and then target-currency precision. **Ending .99** rounds a positive raw price upward to the next price ending in `.99` and is available only for currencies with two decimal places.

Preview shows Base Cost Source, Base Cost, Calculated Cost, source currency, FX source/date/rate, Converted Cost, pricing method and percentage, raw price, rounding strategy, rounding adjustment, final new price, current price, change and one of `Create`, `Update`, `Skip` or `Error`. Select a preview row to inspect the Cost Components behind its calculated cost.

## Apply Modes

- **Replace calculated prices** creates missing target entries and updates existing entries.
- **Only increase prices** creates missing entries and updates only when the calculated price is greater than the current target price.
- **Only create missing prices** leaves every existing target entry unchanged.

**Apply preview** is available only for a successful Preview. Apply is atomic. Before writing, Depot recalculates item cost and FX evidence in the transaction. If the target PriceList, an entry, item-cost evidence or an FX-rate version changed after Preview, Depot rejects Apply and requires a fresh Preview.

A new PriceList can be staged in the normal PriceList editor and used by Bulk generation without introducing a separate pricing model. Global, Region and Customer scope semantics remain unchanged.

## Item Cost Build-up

The calculated cost originates in **Inventory > Items > Cost build-up**. Select the Base Cost Source explicitly:

- **Preferred Supplier Purchase Price** — the single active preferred supplier price;
- **Last Purchase** — the most recent received purchase on or before the calculation date;
- **Manual Standard** — the persisted manual-standard value;
- **Inventory Cost Reference** — the persisted inventory-reference value.

There is no implicit fallback between these strategies. If the selected source cannot produce a value, calculation fails closed. Because existing procurement prices do not carry a currency, the item-cost profile continues to declare the three-letter ISO source currency explicitly.

Cost Components may be Absolute or Percentage. Percentage components choose either **BaseCost** or **RunningTotal**. Sequence controls calculation order; equal sequences remain deterministic through the persisted component identity. Optional validity dates and the Active flag control whether a component participates on the calculation date.

## Sales documents

Adding an item to a quote or Sales Order uses the central scoped resolver. Automatically sourced draft lines may refresh while the document is still a draft. Manual prices are not overwritten. Accepted quotes and submitted/finalized documents retain their stored price and source snapshots; Bulk pricing never rewrites historical transactions.

## Permissions

Viewing Item Costs uses existing Item view permission. Managing Cost Profiles and Components requires Item edit/manage permission. Bulk Preview requires Pricing view plus Item-cost visibility. Applying a Preview and maintaining Pricing exchange rates require Pricing manage permission. These checks are enforced in services, not only by disabled controls.
