# User-facing hardening changes

Updated: 2026-08-27

- New databases no longer use shared default administrator credentials. Depot requires creation of the initial administrator during first-run setup.
- Password policy and login throttling are enforced.
- Remote SQL Server/MySQL/MariaDB configurations require encrypted transport through supported settings.
- Administration includes Audit Log evidence export and Privacy Data discovery/export workflows.
- Administration > Company is the authoritative legal seller/document identity for the current database.
- Inventory > Items provides enriched product master data including GTIN, model/revision/product family, lifecycle dates, origin/customs/ECCN, RoHS/REACH, dangerous-goods/battery data, explicit kg/mm logistics measurements, intended tracking mode and an active replacement-item selector.
- GTIN checksum and uniqueness are validated; item replacement, physical values, dangerous-goods classification and lifecycle dates have consistency checks before saving.
- Item activation/deactivation audit evidence retains the complete extended master-data snapshot.
- Tracking mode, item type and lifecycle status now participate in the documented physical/purchasing/sales workflow controls; serial/lot capture is enforced where tracked physical movements are posted.
- Automatic backup retention preserves the newest backups and ages older automatic backups according to the configured technical policy.
- Posted/finalized business records use correction/reversal/credit workflows instead of destructive edits.
- Posted Sales Invoices freeze seller and Buyer identity and persist the exact generated XRechnung XML with SHA-256 integrity verification in the posting transaction.
- Customers provide a dedicated E-Invoice Identity area for Buyer Reference, electronic endpoint/scheme, tax identity, and structured billing data required by finalized electronic invoices.
- Posted invoices expose **Export XRechnung**, which exports the verified issued XML instead of regenerating it from current Company or Customer master data.
- Invoice posting fails closed when mandatory electronic-invoice identity is incomplete or when a zero-rated, exempt, or reverse-charge scenario cannot yet be represented explicitly by the commercial tax model.
- Electronic credit-note Buyer/XML finalization and production recipient/channel acceptance remain separate follow-up/release gates.
- Finance F0 adds jurisdiction-neutral legal-entity, currency/exchange-rate, fiscal-calendar/period, chart/account, accounting-book, journal-definition, dimension, tax-registration and number-sequence foundations.
- Finance structures initialize automatically through Finance feature schema version 1 on SQLite, SQL Server, and MySQL/MariaDB and seed no jurisdiction/currency/tax defaults.
- Dedicated Finance permissions separate generic access, exchange rates, periods, accounting books, tax configuration and number sequences; the Finance system role receives these permissions through normal RBAC.
- Help manifest 1.9 adds the Finance Foundation guide.
- F0 does not yet add a General Ledger posting workspace or debit/credit posting; that is F1.
- Accessibility and software-quality gates run in CI.
