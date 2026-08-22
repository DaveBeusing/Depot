# Depot Personal Data Inventory

## Purpose

This inventory identifies personal-data categories and technical storage/derivative locations in Depot. It is a technical inventory, not a determination of legal basis or statutory retention periods.

## Primary data categories

| Area | Representative fields | Primary storage | Typical purpose |
| --- | --- | --- | --- |
| Application users | email, display name, account state, role assignments, created timestamp | `Users`, `UserRoles`, audit references | authentication, authorization, accountability |
| Customer master data | name, contact name, email, phone, billing/shipping addresses, tax identifiers | `Customers`, `CustomerAddresses`, `CustomerContacts` | sales, delivery, invoicing, customer administration |
| Customer contacts | name, role, department, email, phone, mobile | `CustomerContacts` | commercial/logistics/accounting communication |
| Supplier master/contact data | contact, email, phone, address, IBAN, account name, SEPA mandate, VAT number, notes | `Suppliers` | procurement, payments, returns, supplier administration |
| Business documents | customer/supplier references, addresses, free-text notes, user IDs | purchase/sales/warehouse transaction tables | execution and evidence of business transactions |
| Audit evidence | user email/ID, before/after snapshots that may include personal data | `AuditEntries` | accountability, troubleshooting, compliance evidence |
| Notifications | recipient user IDs, workflow text and timestamps | notification tables | workflow notification |
| Generated documents | invoice/order/quote/credit-note content, addresses and references | PDF/export files selected by operator | business document exchange and archival |
| Spreadsheet/CSV exports | selected report/master/audit data | operator-selected files | reporting and data portability |
| Backups | complete database snapshot plus metadata | `.depotbackup` archives | disaster recovery |
| Settings | Windows identity-scoped connection configuration; DB user names and protected credentials | `depot.settings` in LocalAppData | connection and backup configuration |
| Diagnostics/logs | exception/context information after sanitizer rules | local diagnostic/log outputs | support and troubleshooting |

## Derived copies and propagation

Personal data can leave the primary database through PDF generation, spreadsheet/CSV export, email attachment workflows, backup archives, screenshots/manual copying, and administrator diagnostics. These outputs become separate retention/access-control objects and must not be treated as deleted merely because source master data changes.

## Data minimization rules

- New person-related fields require a documented operational purpose.
- Free-text fields must not be used as a substitute for structured sensitive-data storage.
- Password hashes, connection credentials, secrets and protected settings are excluded from data-subject exports.
- Lists and notifications should expose only the personal data needed to complete the workflow.
- Audit records retain attributable identifiers required for evidence, but presentation must sanitize credentials/secrets.

## Review triggers

Update this inventory whenever a new entity stores person-related information or Depot adds telemetry, cloud APIs, email providers, remote support, analytics, external identity, new exports, or new generated document types.
