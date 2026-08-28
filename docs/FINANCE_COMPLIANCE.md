# Finance compliance boundary

Updated: 2026-08-28

Finance F0/F1 is an engineering foundation and accounting-control implementation, not an accounting, tax, audit, statutory, or software certification. It provides technical controls that later jurisdiction-specific implementations can use and that operators can include in their own control environment.

## Implemented technical controls

F0 provides:

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

F1 additionally provides:

- balanced double-entry enforcement in transaction and reporting currency;
- immutable posted journal entries and lines;
- explicit linked reversal instead of destructive correction;
- open-period/date/legal-entity enforcement before posting;
- deterministic operation and source-document idempotency;
- atomic number allocation, journal persistence, reversal linking and Audit Log persistence;
- transaction/reporting currency and exchange-rate snapshots on historical journal entries;
- active-account/chart/direct-posting checks;
- required-dimension validation;
- optimistic concurrency for posting-profile maintenance;
- service-layer RBAC for GL view/post/reversal and profile maintenance;
- separate sensitive permission for free manual journals;
- provider-neutral Finance feature schema version 2.

`FinanceJournalEntry` is classified as an accounting-relevant retained business record. The original entry is not edited when corrected.

## Compliance relevance

These controls are relevant building blocks for accounting-record integrity, traceability, segregation of duties, change evidence, and repeatable processing. They support future jurisdiction-specific GoBD/HGB, IFRS, GAAP, VAT/GST/sales-tax, audit-export, SAF-T, DATEV, XBRL, and filing work, but they do not by themselves establish conformity with any of those regimes.

In particular, GoBD-relevant engineering characteristics such as traceability, immutability/correction history, authorization, audit evidence and reproducible processing are technical controls only. German tax authorities do not provide a binding general software certification merely because such controls exist, and third-party certificates cannot replace deployment-specific procedures and operator responsibility.

## What F1 does not claim

F1 does not demonstrate conformity with HGB, GoBD, IFRS, US GAAP, VAT/GST/sales-tax law, SAF-T, DATEV, XBRL, statutory retention periods, statutory account plans, tax filing regimes, consolidation rules, or any jurisdiction-specific audit standard.

Existing XRechnung/EN 16931 functionality remains a separate electronic-invoicing capability. F1 does not treat it as a generic accounting-compliance or tax-determination engine.

ISO-style country and currency syntax validation is a structural guard only. Production reference-data governance must define which codes, schemes, currencies, exchange-rate sources, charts, accounts, tax registrations, accounting standards and effective dates are valid for a specific deployment.

## Remaining assurance work

Before Finance can be represented as production accounting evidence for a particular organization/jurisdiction, acceptance must additionally cover:

- live SQL Server/MySQL/MariaDB migration, locking, deadlock/retry, recovery and load tests;
- role design and segregation-of-duties approval for the deployment;
- chart of accounts and posting-profile approval;
- period-close/reopen procedures and privileged access controls;
- exchange-rate source/effective-date governance;
- backup, retention, restore and export procedures for accounting records;
- source-workflow integrations and reconciliation between subledgers and GL;
- statutory/localization rules, reports, exports and filing interfaces;
- documented operator procedures and legal/accounting review.

The next Finance package, F2 Accounts Receivable, must preserve the same immutable/idempotent posting boundary while adding receivable open-item truth and Sales Invoice/Credit Note integration.
