# Finance compliance boundary

Updated: 2026-08-27

Finance F0 is an engineering foundation, not an accounting, tax, audit, or statutory certification. It provides technical structures that later jurisdiction-specific implementations can use safely.

## Implemented technical controls

- explicit legal-entity boundary;
- explicit functional/reporting currencies with no EUR default;
- exchange rates with source identity and effective timestamp;
- explicit fiscal calendars and accounting periods;
- configurable accounting-standard code rather than a hard-coded national/standards assumption;
- structured tax registrations separated by country and scheme;
- localization and tax-determination interfaces with no implicit rate fallback;
- dedicated Finance RBAC permissions;
- independent Finance feature schema versioning across SQLite, SQL Server, and MySQL/MariaDB;
- no seeded jurisdiction, chart of accounts, tax rate, or statutory reporting configuration.

## What F0 does not claim

F0 does not demonstrate conformity with HGB, GoBD, IFRS, US GAAP, VAT/GST/sales-tax law, SAF-T, DATEV, XBRL, tax filing regimes, statutory retention requirements, or any jurisdiction-specific audit standard. Existing XRechnung/EN 16931 functionality remains a separate electronic-invoicing capability and is not treated as a generic accounting-compliance engine.

ISO-style country and currency syntax validation is a structural guard only. Production reference-data governance must define which codes, schemes, currencies, and tax registrations are valid for a specific deployment and effective date.

## Controls required in F1 and later

Before General Ledger can be represented as accounting evidence, the posting engine must guarantee balanced double entry, immutable posted entries, explicit reversals, period locks, deterministic source-document links, idempotency, transaction atomicity, audit evidence, authorization, and provider/concurrency acceptance.

Later localization packages must define applicable tax determination, numbering, invoice/accounting requirements, statutory reports, retention/export obligations, filing interfaces, and deployment-specific legal acceptance. Those requirements must remain outside the jurisdiction-neutral Finance foundation.
