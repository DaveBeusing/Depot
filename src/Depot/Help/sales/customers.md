# Customers and Contacts

Customers hold the commercial master data used by quotes, sales orders, shipments and invoices.

## Customer workspace

Open **Sales > Customers** to search, create and maintain customers. Customer numbers are assigned by Depot when a record is created. Payment terms, currency and activation remain part of the customer master record.

## Sales region and pricing

A customer can optionally belong to an active Sales Region. The region selects the Regional default price list used between Customer-specific and Global pricing.

The customer-specific price list is optional. **Automatic pricing** means Depot resolves Region → Global, or Global when the customer has no region. Assign a Customer-scoped list only for item prices that differ from those defaults. An incomplete Customer list still falls back per item.

Changing a customer's region or price-list assignment can refresh automatically sourced lines in editable drafts. It does not rewrite prices stored on submitted or later business documents.

## Multiple addresses

A customer can have multiple address records. Each address is classified as **Billing**, **Shipping** or **Other** and can have a descriptive name such as Headquarters, Bonn Warehouse or Toronto Office.

Use **Default** to identify the preferred address for a type. Quotes and Sales Orders use those defaults when a customer is selected, but document snapshots remain independent from later master-data changes.

## Contacts

The **Contacts** tab stores multiple people per customer. Contacts can be classified as General, Commercial, Purchasing, Logistics, Accounting or Technical and can hold department, email, phone and mobile information.

Mark the most commonly used person as **Primary contact**. Quotes offer the available customer contacts and use the selected contact as a document snapshot and email recipient.

## Historical documents

Sales Orders store selected billing and shipping addresses as snapshots. Shipments inherit the order's shipping snapshot and invoices inherit its billing snapshot, so changing customer master data later does not change historical operational documents.

Unsaved customer edits remain protected by Depot's global unsaved-changes guard.
