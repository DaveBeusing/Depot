# Depot Roadmap

This roadmap reflects the implementation on the current `master` branch. “Implemented” means the workflow exists in code; it does not by itself mean production certification.

## Fully implemented

### Architecture and platform

- [x] .NET 10 WPF application with strict MVVM layering
- [x] Composition root in `App.xaml.cs`
- [x] Shared provider-neutral `DatabaseAccess`
- [x] SQLite initialization and migrations
- [x] SQL Server connection factory, initialization, and migrations
- [x] MySQL/MariaDB connection factory, initialization, and migrations
- [x] Explicit safe database errors and credential-safe connection logging
- [x] Encrypted persistent database settings
- [x] Shared busy/loading/empty/error ViewModel state
- [x] Cancellation support for asynchronous database workflows
- [x] Server-side paging and slice-query infrastructure
- [x] Debounced server-side search on primary list screens

### Security and multi-user foundations

- [x] Email/password authentication
- [x] PBKDF2-SHA256 password hashing with per-user salts
- [x] Administrator and standard-user roles
- [x] Audit-entry persistence
- [x] Optimistic concurrency with version columns
- [x] Transactional stock movements
- [x] Provider-specific inventory and purchase-order locking
- [x] Concurrent withdrawal and concurrent goods-receipt protection

### Inventory and master data

- [x] Items and inventories
- [x] Purposes
- [x] Warehouses and storage locations
- [x] Reason codes and standard seed values
- [x] Manufacturers
- [x] Item categories
- [x] Units of measure
- [x] Packaging
- [x] Supplier categories
- [x] Suppliers
- [x] Supplier items and preferred-supplier rules
- [x] Activation/deactivation and reference checks for master data

### Operations

- [x] Opening balances, purchases, withdrawals, and corrections
- [x] Current stock and weighted average cost calculations
- [x] Purchase orders and purchase-order lines
- [x] Draft, Ordered, PartiallyReceived, Received, and Cancelled statuses
- [x] Delivery-note-based goods receipts separated from supplier invoices
- [x] Partial receipts and automatic purchase-order status updates
- [x] Atomic receipt, stock-movement, received-quantity, status, and audit writes
- [x] Immutable technical reason-code keys and protected workflow system codes

### Administration and output

- [x] Administration shell
- [x] User management
- [x] Excel import preview, validation, and execution
- [x] Inventory and grouped reports
- [x] Excel export
- [x] About/version information
- [x] Database overview and connection test
- [x] Backup creation and validation
- [x] Restore with safety backup
- [x] Persistent automatic backup scheduling
- [x] Integrity checks
- [x] SQLite compaction

## Partially implemented

- Fully asynchronous data access: primary UI paths are asynchronous, but legacy synchronous `GetAll()` paths remain.
- Large-data readiness: paging/search/streaming infrastructure exists, but some reports and compatibility services still materialize full tables.
- User-facing paging: server-side paging is used, but not every screen exposes complete page navigation.
- Audit tooling: audit records are written, but no audit viewer, filter, retention, or export UI exists.
- Transfer workflow: the audited Draft/Cancelled domain and persistence foundation exists; atomic TransferOut/TransferIn posting, verification, and UI remain open.
- General settings: database and backup settings are implemented; the general Settings page remains a placeholder.
- Provider verification: SQL Server and MySQL/MariaDB implementations exist, but live-server integration coverage is incomplete.

## Not started

- [ ] Barcode scanning and generation
- [ ] Label templates and printing
- [ ] Dedicated audit-log administration UI
- [ ] General application-preferences implementation

## Must be verified before version 1.0

- [ ] Clean installation and supported-schema migration on supported Windows versions
- [ ] Live SQL Server and MySQL/MariaDB schema-migration matrix
- [ ] Live provider backup, validation, restore, and recovery drills
- [ ] Multi-client concurrency tests against SQL Server and MySQL/MariaDB
- [ ] Long-running import, report, and procurement tests
- [ ] 100,000+ record performance and memory acceptance tests
- [ ] Full keyboard, focus, accessibility, scaling, and localization pass
- [ ] Separate SupplierInvoice domain and invoice-verification workflow
- [ ] Security review of default credentials, encrypted settings, logs, backups, and retained legacy invoice data
- [ ] Release packaging, signing, upgrade, rollback, and uninstall verification
- [ ] Complete manual regression test and release notes
