# Customers and Contacts

Customers hold the commercial master data used by quotes, sales orders, shipments and invoices.

## Customer workspace

Open **Sales > Customers** to search, create and maintain customers. Customer numbers are assigned by Depot when a record is created. Payment terms, currency and activation remain part of the customer master record.

## Multiple addresses

A customer can have multiple address records. Each address is classified as **Billing**, **Shipping** or **Other** and can have a descriptive name such as Headquarters, Bonn Warehouse or Toronto Office.

Use **Default** to identify the preferred address for a type. Quotes and Sales Orders use those defaults when a customer is selected, but document snapshots remain independent from later master-data changes.

## Contacts

The **Contacts** tab stores multiple people per customer. Contacts can be classified as General, Commercial, Purchasing, Logistics, Accounting or Technical and can hold department, email, phone and mobile information.

Mark the most commonly used person as **Primary contact**. Quotes offer the available customer contacts and use the selected contact as a document snapshot and email recipient.

## Historical documents

Sales Orders store selected billing and shipping addresses as snapshots. Shipments inherit the order's shipping snapshot and invoices inherit its billing snapshot, so changing customer master data later does not change historical operational documents.

Unsaved customer edits remain protected by Depot's global unsaved-changes guard.
