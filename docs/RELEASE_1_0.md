# Depot Version 1.0 Release Checklist

## Status

- [ ] Ready for release

Depot is currently `0.9.1-preview` with database schema version **16**. Checked implementation items below mean code exists on `master`; they do not replace the outstanding release verification.

## Implemented and covered by automated tests

### Application and authentication

- [x] First-run SQLite database creation
- [x] Default administrator creation
- [x] Email/password login and logout
- [x] Administrator and standard-user authorization
- [x] PBKDF2-SHA256 password hashing
- [x] Application/version/schema information

### Data and inventory

- [x] Item create, edit, search, and deactivate
- [x] Normalized manufacturer, category, unit-of-measure, and packaging references
- [x] Purpose management
- [x] Warehouse and storage-location hierarchy
- [x] Multiple inventory contexts per item
- [x] Inventory-based stock movements
- [x] Reason-code persistence, immutable technical keys, protected workflow system codes, and inactive-code validation
- [x] Current-stock and average-cost calculations
- [x] Concurrent withdrawals cannot create negative stock
- [x] Stale version updates are rejected
- [x] Stock movement and audit entry commit together

### Suppliers and procurement

- [x] Supplier categories independent from item categories
- [x] Supplier create, edit, search, activate, and deactivate
- [x] SupplierItem many-to-many assignments and preferred supplier
- [x] Purchase-order drafts, lines, generated order numbers, status changes, search, and filters
- [x] Partial and final goods receipts
- [x] Required invoice metadata and invoice-document validation
- [x] Atomic receipt, received quantity, stock movement, order status, and receipt audit writes
- [x] Over-receipt rollback
- [x] Concurrent goods receipts cannot over-receive an order line

### Import, reports, and database administration

- [x] Excel import preview, validation, duplicate detection, and execution
- [x] Inventory and grouped reports
- [x] Excel export
- [x] Backup creation and archive validation
- [x] Restore with automatic safety backup
- [x] Persistent scheduled backups
- [x] SQLite integrity check and compaction
- [x] Provider/schema/connection/size/last-backup overview
- [x] SQLite backup/restore integration tests

## Implemented but only partially verified

### Database providers

- [x] SQLite provider, current schema creation, and migrations
- [x] SQL Server provider, current schema creation, and migrations
- [x] MySQL/MariaDB provider, current schema creation, and migrations
- [x] Provider-specific generated-ID normalization and locking SQL
- [x] Safe connection errors and credential-safe database logging
- [ ] Live SQL Server clean-install test
- [ ] Live SQL Server supported-version migration test
- [ ] Live SQL Server backup/restore and integrity test
- [ ] Live SQL Server multi-client concurrency test
- [ ] Live MySQL clean-install test
- [ ] Live MariaDB clean-install test
- [ ] Live MySQL/MariaDB supported-version migration test
- [ ] Live MySQL/MariaDB backup/restore and integrity test
- [ ] Live MySQL/MariaDB multi-client concurrency test

### Performance and asynchronous behavior

- [x] Asynchronous query/command APIs and cancellation tokens
- [x] Server-side paging infrastructure
- [x] Debounced server-side search on primary list screens
- [x] Streaming API for large result processing
- [ ] Remove or justify remaining synchronous `GetAll()` application paths
- [ ] Add full page navigation where bounded first-page loading is insufficient
- [ ] Verify large reports do not require full in-memory materialization
- [ ] Complete 100,000+ record performance and memory test
- [ ] Verify cancellation during long imports, reports, backup, restore, and receipt posting

## Not implemented

- [ ] Dedicated audit-log viewer, filters, retention, and export
- [ ] General application-preferences page
- [ ] Barcode workflow
- [ ] Label design and printing

## Manual acceptance required before release

### Functional regression

- [ ] Dashboard totals and recent movements verified
- [ ] Item and master-data workflows verified
- [ ] Warehouse/storage-location migration and editing verified
- [ ] Stock receipts, issues, corrections, and opening balances verified
- [ ] Purchase-order edit/cancel/status workflow verified
- [ ] Partial receipts and invoice-document selection verified
- [ ] Import and all reports verified with representative customer data
- [ ] Backup validation, restore, safety backup, and scheduled backup verified
- [ ] Corrupted backup, unavailable path, locked file, and interrupted operation behavior verified

### User interface

- [ ] No broken bindings or runtime XAML exceptions
- [ ] Keyboard focus and Tab order verified, including login and procurement
- [ ] Loading, saving, importing, exporting, backup, and restore feedback verified
- [ ] Empty and error states verified
- [ ] 100%, 125%, 150%, and 200% scaling verified
- [ ] Accessibility names, contrast, and screen-reader basics verified
- [ ] German number, currency, date, and time formatting verified

### Security and operations

- [ ] Initial administrator password handling reviewed
- [ ] `depot.settings` encryption and Windows-user behavior reviewed
- [ ] Logs reviewed for connection strings, passwords, invoice data, and personal data
- [ ] Backup permissions, retention, and recovery procedure documented
- [ ] Invoice-document path lifecycle and access permissions reviewed
- [ ] Database least-privilege requirements documented for server providers

### Release engineering

- [ ] Release build succeeds with zero warnings
- [ ] All automated tests pass on the release commit
- [ ] Supported Windows versions tested
- [ ] Application version finalized in `Directory.Build.props`
- [ ] Database schema remains version 16 or migration notes are updated
- [ ] Installer/package, signing, upgrade, rollback, and uninstall tested
- [ ] Release notes and known limitations published
