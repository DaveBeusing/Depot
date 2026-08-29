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

`SalesPriceList.Scope` uses `Global`, `Region`, or `Customer`:

- **Global** — `RegionId` is null and the list is not assigned through `CustomerPriceLists`.
- **Region** — `RegionId` references an active `SalesRegion` and the list is not assigned through `CustomerPriceLists`.
- **Customer** — `RegionId` is null. An active list requires at least one binding through the existing `CustomerPriceLists` relation. An inactive list may be staged before assignment.

`Customer.SalesRegionId` is optional. A customer without a region skips the regional step. A customer price-list assignment is also optional; the UI presents the absence of an assignment as automatic Region → Global or Global pricing rather than as a missing price list.

`SalesPriceResult` retains the unit price, discount, source price-list identity and name, scope, currency, and optional region identity. Sales-order and quote lines persist the source identity, name, scope, and currency beside the established price and discount snapshots.

## Validity rules

The resolver retains the existing Sales pricing semantics:

- customer and item must be active;
- price list must be active;
- the effective document date must be within `ValidFrom` / `ValidTo` when set;
- list currency must equal the document currency;
- a regional list requires an active region matching the customer's region;
- a customer list must be the list assigned to that customer.

Price-list entries currently have one unit price and discount per item. They do not have separate activity dates or quantity tiers in the current Depot model. Quantity is part of the central resolver contract so future tier rules can be added at the same business boundary without introducing another resolver.

If no valid result exists, editors retain an explicitly entered manual value. Automatic-source metadata is cleared so the value is not presented as a resolved price.

## Defaults, concurrency, and transactions

At most one active Global default may exist. At most one active Region default may exist for a given region. `SalesPricingService` checks these rules inside the existing provider write transaction. SQLite uses an immediate write transaction; SQL Server and MySQL/MariaDB use serializable write transactions. Existing transient-conflict retry handling remains in force, so concurrent default activations cannot commit two contradictory defaults.

Price-list, price-list-item, region, and customer-assignment mutations use the established optimistic `Version` contract where applicable. Adding, changing, and removing an item price records structured Audit evidence. The business mutation and its Audit entry commit or roll back together. `SalesPricing.View` and `SalesPricing.Manage` remain the authoritative RBAC permissions.

## Schema and migration

Core schema 30 and Sales feature schema 9 add:

- `SalesRegions`;
- `SalesPriceLists.Scope` and optional `RegionId`;
- optional `Customers.SalesRegionId`;
- scope/region, customer-region, and reverse assignment indexes;
- price-source snapshot columns on `SalesOrderLines` and `SalesQuoteLines`;
- equivalent foreign keys and scope checks for SQLite, SQL Server, and MySQL/MariaDB.

The Sales 8 → 9 migration classifies every existing price list as `Customer`. Existing price-list items and `CustomerPriceLists` rows are retained unchanged, so established customer-specific pricing continues to resolve after upgrade. Previously active lists without any customer binding are deactivated because they had no resolving customer and would violate the active Customer-scope invariant; their items are retained. New databases receive the same schema through the ordered Sales migration chain.

## Sales document behavior

Quotes and Sales Orders call the central resolver when an item is added or a price is explicitly resolved. Saving a draft refreshes only lines carrying automatic source metadata, which covers changes to customer, region, assigned list, quantity, currency, or effective document date. Manually entered lines are not overwritten.

Accepted quotes retain their price snapshots when converted to an order. Submitted and later Sales Orders cannot be edited through the draft-save path, and later price-list or customer-region changes do not rewrite their stored line prices.

## Validation

Automated SQLite coverage includes per-item mixed-scope fallback, missing assignments and regions, currency and active-item rules, date/activity fallback, no-price results, cancellation, optimistic concurrency, concurrent default activation, RBAC, transactional Audit rollback, Sales 8 → 9 data preservation, and historical Sales Order snapshots. The existing optional live-provider test configuration runs the scoped fallback contract against SQL Server and MySQL/MariaDB when their test connection strings are present.
