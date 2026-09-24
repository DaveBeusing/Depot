# Sales Pricing

Updated: 2026-09-16

## Scope

Depot pricing resolves a price per item through an explicit scope chain rather than selecting one monolithic list for the whole document:

```text
Customer-specific PriceList
        ↓ fallback per item
Regional PriceList
        ↓ fallback per item
Global PriceList
```

A Customer or Region list therefore only needs to contain exceptions. Missing entries continue through the fallback chain; an explicit zero price remains an explicit price.

## Scope model

PriceLists support `Global`, `Region` and `Customer`. Customer-specific assignment is optional. A customer can also be assigned to a Sales Region. Existing Global lists remain valid and remain the final fallback.

The resolver is centralized in `SalesPricingService`; Views, ViewModels and document services do not implement their own fallback algorithms.

## Historical document evidence

Quotes and Sales Orders retain the resolved price source at the point where the commercial decision becomes durable. Later PriceList changes do not silently rewrite existing document economics.

Finalized invoices continue to retain their own document-line monetary evidence and issuer/buyer finalization snapshots.

## Item cost and advanced bulk price generation

Sales schema 12 extends the existing Item Cost Build-up without creating a parallel pricing engine. The central flow is:

```text
Explicit Base Cost Source
        ↓
ItemCostCalculationService
        ↓
ordered Cost Components
        ↓
Calculated Cost + Cost Currency
        ↓
direct effective-dated FX when required
        ↓
Converted Cost in PriceList Currency
        ↓
Percentage Markup OR Target Gross Margin
        ↓
Commercial Rounding
        ↓
mandatory Preview with full intermediates
        ↓
atomic Apply to one scoped PriceList
```

Bulk pricing never bypasses `SalesPricingService` for runtime resolution and never rewrites historical Sales documents.

### Explicit Base Cost strategies

- Preferred Supplier Purchase Price;
- Last Purchase backed by an actual Goods Receipt on or before the effective date;
- Manual Standard;
- Inventory Cost Reference.

No strategy silently falls back to another strategy.

### Pricing methods

Percentage Markup:

```text
SalesPriceRaw = ConvertedCost × (1 + MarkupPercent / 100)
```

Target Gross Margin:

```text
SalesPriceRaw = ConvertedCost / (1 - GrossMarginPercent / 100)
```

Gross Margin must remain below 100%. It is not treated as another label for Markup.

### Controlled FX

`PricingExchangeRates` are directional and effective-dated. Resolution requires an exact Source Currency → Target Currency pair and chooses the newest record effective on or before the pricing date.

Evidence includes rate source, effective date, rate, source/target currencies and row version. Depot does not invert, triangulate or assume missing rates. Missing FX data is a Preview error.

### Commercial rounding

The bulk request selects one deterministic strategy:

- normal target-currency precision;
- nearest 0.01;
- nearest 0.05;
- nearest 0.10;
- nearest 0.50;
- `.99` ending for two-decimal currencies.

Preview exposes both raw and rounded prices plus the rounding adjustment.

## Schema and migration

The **current Sales feature schema is 15**.

Sales schema **10** introduced Item Cost profiles/components and deterministic cost-component ordering.

Sales schema **11** reconciled the cross-provider active inventory-reservation uniqueness invariant.

Sales schema **12**:

- expands `ItemCostProfiles.BaseCostSource` to four explicit strategies;
- adds `ManualStandardCost` and `InventoryCostReference` values;
- adds versioned `PricingExchangeRates` with directional/effective uniqueness and lookup indexing.

Sales schema **13** adds the electronic-invoice finalization/evidence persistence used by the bounded XRechnung 3.0 CII production path. It does not change the Advanced Pricing formulas or runtime resolution contract described here.

Sales schema **14** adds immutable ZUGFeRD/Factur-X hybrid PDF artifact persistence linked to the finalized XRechnung payload. It likewise does not change Advanced Pricing formulas or price resolution.

Sales schema **15** adds CRM lead, opportunity, stage and activity persistence. It does not change Advanced Pricing formulas, price-list scope resolution or historical price evidence.

Core database schema remains **30**.

## Provider production acceptance

Sales pricing and the order-to-cash path remain part of the production provider matrix for the supported SQLite, SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11 baselines.

The schema-12 pricing migration and runtime SQL are provider-neutral at the service/repository boundary and have provider-specific DDL only in the schema layer. Subsequent Sales migrations continue through the same feature-version path to the current schema.

MariaDB and MySQL are executed as separate jobs with separate connection settings and provider traits. They are never inferred from one another.

See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) for the exact certified database versions and recovery/performance boundary.

## Concurrency, Audit and authorization

PriceList mutations and bulk generation use optimistic concurrency and service-layer authorization. Bulk Apply executes as one provider write transaction and recalculates/revalidates:

- PriceList version;
- target entry version;
- Cost evidence;
- FX rate/version evidence;
- formula result;
- rounding result;
- Apply Mode decision.

Any drift requires a new Preview. The batch Audit record retains the pricing method, percentage, rounding strategy, effective date and actual FX evidence used.

UI visibility is not an authorization boundary.

## Known boundaries

Advanced pricing deliberately does not introduce automated market-rate ingestion, implicit FX inversion/triangulation, a separate live inventory-valuation source, or retroactive repricing of historical Sales documents.

Database-provider certification does not imply market/jurisdiction-specific pricing, tax or invoicing compliance.
