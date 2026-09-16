# Item Costing and Advanced Bulk Pricing

## Purpose

Depot keeps operational item-cost calculation separate from runtime sales price resolution while allowing controlled commercial derivation of PriceList entries.

The advanced flow is:

```text
Explicit Item Cost Base Source
→ ItemCostCalculationService
→ ordered ItemCostComponents
→ Calculated Cost in Cost Currency
→ direct effective-dated FX evidence when required
→ Converted Cost in PriceList Currency
→ Percentage Markup OR Target Gross Margin
→ deterministic Commercial Rounding
→ Preview with intermediate evidence
→ atomic Apply
→ existing Global / Region / Customer PriceList
```

No step silently substitutes a different source, rate, formula or rounding rule.

## Item Cost Base Sources

`ItemCostProfile.BaseCostSource` is explicit and persisted.

Supported sources:

1. `PreferredSupplierPurchasePrice`
   - requires exactly one active preferred supplier price;
   - zero matches fail closed;
   - multiple matches fail closed.
2. `LastPurchase`
   - uses the most recent received purchase on or before the effective calculation date;
   - purchase-order drafts or unreceived lines do not qualify;
   - no received purchase fails closed.
3. `ManualStandard`
   - uses the persisted non-negative `ManualStandardCost` value;
   - the selected strategy requires that value to be present.
4. `InventoryCostReference`
   - uses the persisted non-negative `InventoryCostReference` value;
   - this is an explicit reference value, not an implicit substitution of live FIFO/accounting valuation.

The profile keeps an explicit ISO three-letter Cost Currency. Existing supplier/procurement prices do not carry a currency field, so Depot does not infer one.

## Cost Components

Cost Components retain the established deterministic sequence model:

- Absolute;
- Percentage of Base Cost;
- Percentage of Running Total;
- active/inactive;
- optional valid-from/valid-until;
- persisted identity and version evidence.

`ItemCostCalculationService` remains the single cost formula.

## Pricing methods

### Percentage Markup

```text
SalesPriceRaw = ConvertedCost × (1 + MarkupPercent / 100)
```

### Target Gross Margin

```text
SalesPriceRaw = ConvertedCost / (1 - GrossMarginPercent / 100)
```

Gross Margin is validated in the range `0 <= margin < 100`. Markup and Gross Margin are separate enums and request values; neither is inferred from the other.

Example for a cost of 100 and 25%:

```text
25% Markup       = 125.00
25% Gross Margin = 133.33 before commercial rounding
```

## Controlled FX conversion

FX is directional and effective-dated.

Each `PricingExchangeRates` row persists:

- Source Currency;
- Target Currency;
- Effective Date;
- Rate Source;
- Rate;
- Version.

For a calculation date, Depot resolves the newest direct row where:

```text
SourceCurrency = CostCurrency
TargetCurrency = PriceListCurrency
EffectiveDate <= CalculationDate
```

There is no automatic inversion, triangulation, previous-pair substitution or 1:1 fallback. A missing rate makes the affected Preview row an error.

When source and target currencies are equal, conversion is an explicit in-memory identity (`1.0`) and no FX row is required.

The converted cost is rounded to normal target-currency precision before applying the selected pricing formula.

## Commercial rounding

Supported strategies:

- `CurrencyPrecision`;
- `Nearest0_01`;
- `Nearest0_05`;
- `Nearest0_10`;
- `Nearest0_50`;
- `Ending99`.

Increment strategies use midpoint-away-from-zero and then target-currency precision. `Ending99` moves a positive raw price upward to the next `.99` endpoint and is restricted to two-decimal target currencies.

Preview stores both the unrounded raw price and the final price, so the rounding adjustment is visible and reproducible.

## Preview evidence

Each successful Preview row exposes:

- Base Cost Source;
- Base Cost;
- Calculated Cost;
- Cost Currency;
- Cost Evidence Version;
- FX rate ID/version, source, effective date, rate and currencies;
- Converted Cost;
- Pricing Method;
- selected pricing percentage;
- Unrounded Price;
- Rounding Strategy;
- Rounding Adjustment;
- Calculated New Price;
- existing target price and resulting action;
- Cost Component details.

Errors are row-local and fail closed. Apply is blocked while Preview contains errors.

## Apply and concurrency

Preview performs no writes.

Apply runs atomically through the existing transaction boundary. It re-reads/recalculates:

- target PriceList version;
- target entry version;
- Item Cost evidence;
- selected FX rate and its version;
- pricing formula;
- rounding result;
- Apply Mode result.

A change to any calculation evidence after Preview results in a concurrency conflict and requires a fresh Preview. Preview and Apply therefore cannot drift onto separate formulas or rates.

The batch Audit record persists the selected pricing method/percentage, rounding strategy, effective date and the actual non-identity FX evidence used by the applied rows.

## Historical sales documents

Advanced generation writes only PriceList entries. It does not revisit accepted quotes, submitted orders, invoices, credit notes or finalized monetary snapshots. Existing historical document evidence therefore remains unchanged.

## RBAC

- Item Cost read: existing Item view permission.
- Item Cost profile/component maintenance: existing Item edit/manage permission.
- Bulk Preview: Sales Pricing view plus Item-cost visibility.
- Bulk Apply: Sales Pricing manage plus Item-cost visibility.
- FX-rate maintenance: Sales Pricing manage.

Authorization is enforced in services. UI command state is not the security boundary.

## Schema and version impact

Advanced Pricing introduces Sales feature schema **12**.

Schema 12:

- expands `ItemCostProfiles.BaseCostSource` to four explicit strategies;
- adds nullable `ManualStandardCost` and `InventoryCostReference` reference values;
- adds versioned `PricingExchangeRates` with directional/effective uniqueness and lookup indexing.

Core database schema remains unchanged.

## Provider contract

The migration is implemented for:

- SQLite;
- SQL Server;
- MySQL / MariaDB.

Provider-specific DDL remains behind the data/schema layer. Pricing services and repositories use provider-neutral SQL/query behavior for runtime calculations.
