# User-facing hardening changes

Updated: 2026-08-28

- New databases no longer use shared default administrator credentials. Depot requires creation of the initial administrator during first-run setup.
- Password policy and login throttling are enforced.
- Remote SQL Server/MySQL/MariaDB configurations require encrypted transport through supported settings.
- Administration includes Audit Log evidence export and Privacy Data discovery/export workflows.
- Administration > Company is the authoritative legal seller/document identity for the current database.
- Inventory > Items provides enriched product master data including GTIN, model/revision/product family, lifecycle dates, origin/customs/ECCN, RoHS/REACH, dangerous-goods/battery data, explicit kg/mm logistics measurements, intended tracking mode and an active replacement-item selector.
- GTIN checksum and uniqueness are validated; item replacement, physical values, dangerous-goods classification and lifecycle dates have consistency checks before saving.
- Tracking mode, item type and lifecycle status participate in physical/purchasing/sales workflow controls; serial/lot capture is enforced where tracked physical movements are posted.
- Posted/finalized business records use correction/reversal/credit workflows instead of destructive edits.
- Posted Sales Invoices freeze seller and Buyer identity and persist the exact generated XRechnung XML with SHA-256 integrity verification in the posting transaction.
- Posted invoices expose **Export XRechnung**, which exports the verified issued XML instead of regenerating it from current Company or Customer master data.
- Invoice posting fails closed when mandatory electronic-invoice identity is incomplete or when a zero-rated, exempt, or reverse-charge scenario cannot yet be represented explicitly by the commercial tax model.

## Finance F0/F1

- Finance F0 adds jurisdiction-neutral legal-entity, currency/exchange-rate, fiscal-calendar/period, chart/account, accounting-book, journal-definition, dimension, tax-registration and number-sequence foundations.
- Finance F1 adds the provider-neutral General Ledger posting engine and raises Finance feature schema from 1 to **2** for SQLite, SQL Server, and MySQL/MariaDB.
- General Ledger entries are immutable once posted and must balance debit and credit in transaction currency and reporting currency.
- Foreign-currency postings keep the used transaction/reporting currencies and exchange-rate snapshot as historical evidence.
- Posting profiles map named business amount keys to configured debit/credit accounts so later source workflows do not hard-code account numbers.
- Posting is blocked when the accounting period is not open for the legal entity/date, when an account is inactive/not directly postable/not in the book's chart, or when a required accounting dimension is missing.
- Retrying an identical operation or source-document event does not create a second accounting entry or consume another General Ledger number.
- General Ledger numbers are allocated inside the accounting transaction and roll back if posting or Audit Log persistence fails.
- Corrections are explicit linked reversal entries; the original journal remains unchanged and cannot be reversed twice.
- Journal creation/reversal and central Audit Log evidence commit atomically.
- Controlled profile-based posting uses the normal General Ledger permission. Free manual journals additionally require the sensitive `FinanceManualJournals.Post` permission.
- The default Finance system role receives controlled General Ledger view/post/reversal and posting-profile permissions, but not the free manual-journal permission automatically.
- F1 currently exposes a service/repository accounting boundary rather than a partial Finance workspace. Sales, Purchasing, and Inventory are not silently wired to GL until their complete Finance integration package exists.
- F2 Accounts Receivable is next and will connect Sales Invoice/Credit Note events to F1 while adding receivable open items, payment allocation, write-offs, dunning, and aging.

## Help and documentation

- Help manifest **1.10** contains Finance Foundation and General Ledger and Posting topics.
- The `0.15.2-preview` documentation synchronization refreshes those Finance articles without changing stable Help topic IDs or permission contracts, so the manifest version remains 1.10.
- README, architecture, current status, release checklist, compliance overview/matrix, and Finance Help now describe F1 consistently.

Accessibility and software-quality gates continue to run in CI.
