# Sales Pricing

Customer pricing is managed in **Sales > Overview > Pricing** inside the Commercial Hub.

## Price lists

Create a price list with a unique code, display name, scope, currency and optional validity window. Add item prices and optional discount percentages to the selected list. A list can be intentionally incomplete.

The available scopes are:

- **Global** — the fallback for every customer.
- **Region** — the default for customers assigned to the selected active Sales Region.
- **Customer** — optional special pricing assigned through the existing customer selector. Create it inactive, assign at least one customer, and then activate it.

There can be only one active Global default and one active Regional default per region. Depot validates these rules when the price list is saved, including concurrent changes by multiple users.

## Price resolution

For each item, Depot resolves Customer → Region → Global. If the customer list does not contain that item, resolution continues with the Regional list. If the Regional list does not contain it, resolution continues with Global. Inactive, expired, not-yet-valid, wrong-currency and otherwise inapplicable lists are skipped in the same way.

A customer-specific list and a Sales Region are both optional. Without a customer list Depot uses Region → Global; without a region it uses Customer → Global. If no valid item price exists at any scope, the editor keeps the manually entered price and discount.

Removing the final customer assignment automatically deactivates that Customer list. This prevents an active Customer-scoped list without the required binding while allowing the customer to return to automatic pricing.

The resolved result displays and stores the source price-list name and scope. This makes a Regional or Global price distinguishable from special Customer pricing.

## Sales documents

Adding an item to a quote or Sales Order uses the central resolver. **Resolve price** applies the same logic again. Automatically sourced draft lines are refreshed when a draft is saved, including after relevant customer, region, quantity, currency or document-date changes. Manual prices are not overwritten.

Accepted quotes and submitted or later Sales Orders retain their stored price and source snapshots. Later price-list, assignment or customer-region changes do not rewrite historical documents.

## Permissions

`SalesPricing.View` allows users to inspect price lists. `SalesPricing.Manage` allows price-list creation, item-price maintenance and customer assignment.

Pricing changes affect new and automatically sourced draft lines. Finalized quote, order and invoice pricing remains snapshot-based.
