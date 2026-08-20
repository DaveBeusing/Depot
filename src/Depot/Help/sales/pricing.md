# Sales Pricing

Customer pricing is managed in **Sales > Overview > Pricing** inside the Commercial Hub.

## Price lists

Create a price list with a unique code, display name, currency and optional validity window. Add item prices and optional discount percentages to the selected list.

A customer can be assigned one active price list. When an item is added to a quote, or when **Apply customer price** is used in a Sales Order, Depot resolves the assigned customer price for the document date.

If no customer-specific price exists, the editor keeps the manually entered price and discount.

## Permissions

`SalesPricing.View` allows users to inspect price lists. `SalesPricing.Manage` allows price-list creation, item-price maintenance and customer assignment.

Pricing changes affect new document lines only. Existing quote, order and invoice lines retain their snapshot values.
