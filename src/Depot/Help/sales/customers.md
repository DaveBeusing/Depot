# Customers

Customers hold the commercial master data used by sales orders, shipments and invoices.

## Customer workspace

Open **Sales > Customers** to search, create and maintain customers in the dedicated customer workspace. Customer numbers are assigned by Depot when a record is created. Contact data, payment terms, currency and activation remain part of the customer master record.

## Multiple addresses

A customer can have multiple address records. Each address is classified as **Billing**, **Shipping** or **Other** and can have a descriptive name such as Headquarters, Bonn Warehouse or Toronto Office.

Use **Default** to identify the preferred address for a type. Sales Orders use those defaults when a customer is selected, but another suitable address can be chosen for an individual order.

Legacy billing and shipping fields remain synchronized with the default addresses for compatibility with existing data.

## Historical documents

Customer addresses are master data and can change. Sales Orders therefore store the selected billing and shipping addresses as snapshots. Shipments inherit the order's shipping snapshot and invoices inherit its billing snapshot, so changing a customer later does not change historical operational documents.

Unsaved customer edits are protected by Depot's global unsaved-changes guard when you leave the Sales section or close the workspace tab.
