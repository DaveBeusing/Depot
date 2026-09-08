# Sales Pricing

Updated: 2026-09-08

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

PriceLists support:

- `Global`
- `Region`
- `Customer`

Customer-specific assignment is optional. A customer can also be assigned to a Sales Region. Existing Global lists remain valid and remain the final fallback.

The resolver is centralized in `SalesPricingService`; Views, ViewModels and document services do not implement their own fallback algorithms.

## Historical document evidence

Quotes and Sales Orders retain the resolved price source at the point where the commercial decision becomes durable. Later PriceList changes do not silently rewrite existing document economics.

Finalized invoices continue to retain their own document-line monetary evidence and issuer/buyer finalization snapshots.

## Item cost and bulk price generation

Item Cost Build-up was introduced in Sales schema 10 and remains the calculation source for controlled bulk price generation. The central flow is:

```text
Preferred supplier PurchasePrice
        ↓
ItemCostCalculationService
        ↓
ordered cost components
        ↓
Calculated Cost
        ↓
PriceListGenerationService
        ↓
Markup + mandatory Preview
        ↓
atomic Apply to one scoped PriceList
```

Bulk pricing never bypasses `SalesPricingService` for runtime resolution and never rewrites historical Sales documents.

## Schema and migration

The **current Sales feature schema is 11**.

Sales schema **10** introduced:

- scoped pricing structures from the previous Sales schema;
- `ItemCostProfiles`;
- `ItemCostComponents`;
- deterministic `(ItemId, Sequence, Id)` cost-component lookup/order indexing.

Sales schema **11** adds/reconciles the cross-provider active inventory-reservation uniqueness invariant. It does not change the pricing formula or rewrite PriceLists, Item Cost evidence, Quotes, Sales Orders, Shipments or Invoices.

The reservation invariant is provider-equivalent rather than syntax-identical:

- SQLite: partial unique index;
- SQL Server: filtered unique index;
- MariaDB/MySQL: generated `ActiveInventoryId` plus unique `(SalesOrderLineId, ActiveInventoryId)` index.

Core database schema remains **30**.

## Provider production acceptance

Sales pricing and the order-to-cash path are included in the real production provider matrix for the supported SQLite, SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11 baselines.

Provider acceptance verifies:

- scoped PriceList fallback behavior;
- Sales feature 10→11 migration and reservation uniqueness;
- Sales order approval/reservation/release;
- shipment posting and stock impact;
- invoice creation/posting and order completion;
- provider transaction/constraint behavior.

MariaDB and MySQL are executed as separate jobs with separate connection settings and provider traits. They are never inferred from one another.

See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) for the exact certified database versions and recovery/performance boundary.

## Concurrency and authorization

PriceList mutations and bulk generation continue to use optimistic concurrency and service-layer authorization. Bulk Apply executes as one provider write transaction and revalidates preview evidence before changing target entries.

UI visibility is not an authorization boundary.

## Known boundaries

Current bulk generation supports Percentage Markup, not Target Gross Margin, and fails closed when cost/list currencies differ. Controlled FX conversion and additional pricing methods remain separate extensions.

Database-provider certification does not imply market/jurisdiction-specific pricing, tax or invoicing compliance.
