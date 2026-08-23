# Historical issuer snapshots

Depot stores the company identity used for posted financial documents separately from the mutable Administration > Company master record.

## Scope

Issuer snapshots currently apply to posted sales invoices and posted sales credit notes. Draft documents continue to resolve the current Company master data because they are not yet finalized business records.

When a financial document is posted, Depot projects the current `CompanyProfile` to the publishable `DocumentIssuerProfile` and inserts that projection into `SalesDocumentIssuerSnapshots` inside the same database transaction as the posting status change and audit event. If issuer capture fails, the complete posting transaction fails and is rolled back.

The snapshot contains the legal/trading identity, postal address, register/tax disclosure, management disclosure, ordinary contact data, bank details and structured-invoice endpoint data needed for regeneration. It deliberately excludes workflow-specific or sensitive company registrations such as IOSS and internal customs-account references.

## Immutability

`SalesDocumentIssuerSnapshots` has one primary-keyed record per `(DocumentType, DocumentId)`. Application logic rejects a second capture attempt. No update path exists.

PDF regeneration follows these rules:

- draft invoice or credit note: use current Company master data;
- posted invoice: require the stored sales-invoice issuer snapshot;
- posted credit note: require the stored sales-credit-note issuer snapshot;
- other operational/commercial documents: use current Company master data until their own finalization/snapshot rule is defined.

A missing snapshot for an already posted document fails closed. Depot does not silently substitute today's Company master data because that would change the historical representation of the finalized record.

## Legacy records

Financial documents posted before this schema existed do not contain provable historical issuer data. The migration therefore does not backfill them from current master data. If such legacy records must be regenerated, they require an explicit controlled migration/remediation process using independently verified historical company data.

## Database migration

Sales schema version 7 adds `SalesDocumentIssuerSnapshots` for SQLite, SQL Server and MySQL/MariaDB and ensures Company-profile storage exists before financial posting is attempted. The application build carrying this completed block is version `0.14.113-preview`.

## Evidence

Automated tests verify that:

- changing Company master data after capture does not change the stored issuer;
- a second snapshot for the same finalized document is rejected;
- a missing historical snapshot fails closed instead of falling back to current master data.
