# Depot Help Center

Updated: 2026-08-28

Depot ships an integrated offline Help Center using embedded Markdown and native WPF `FlowDocument` rendering. Help opens as a regular workspace tab; F1 resolves the current application context without replacing other open workspaces.

## Content structure

Help content is versioned with the application under `src/Depot/Help`. The current manifest version is **1.10** and contains Getting Started, Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, Finance, Administration, and Troubleshooting topics.

`manifest.json` defines stable topic IDs, titles, categories, Markdown files, ordering, search keywords, optional required permissions, and related topics. IDs are application contracts and should not be renamed after use.

## Finance Help after F1

Manifest 1.10 contains two Finance topics:

- `finance.foundation` — Finance Foundation, guarded by `Finance.View`;
- `finance.general-ledger` — General Ledger and Posting, guarded by `FinanceGeneralLedger.View`.

The General Ledger article documents the implemented F1 behavior:

- double-entry balance requirements;
- transaction/reporting currency and FX snapshots;
- open-period/legal-entity/date validation;
- account/chart/direct-posting validation;
- required dimensions;
- operation and source-document idempotency;
- posting profiles;
- number allocation inside the transaction;
- atomic Audit Log persistence;
- manual-journal authorization separation;
- explicit linked reversal and immutable originals;
- the boundary to F2 Accounts Receivable and later packages.

The `0.15.2-preview` documentation refresh updates Finance article wording only. Topic IDs, required permissions, related-topic contracts, and Markdown file mappings are unchanged, so manifest version remains **1.10**.

## Current user guidance

Help must remain synchronized with current application behavior, especially security/compliance changes that affect normal operation:

- a new database has no shared default administrator password; Depot requires first-run administrator creation;
- the login flow opens the tabless Welcome page after authentication;
- all workspace tabs are closeable; closing the final tab restores Welcome;
- `Ctrl+P` Quick Open, `Ctrl+Shift+P` Command Palette, tab/history shortcuts, and F1 context Help are documented;
- remote SQL Server/MySQL/MariaDB configuration uses supported encrypted-transport defaults;
- backup guidance documents validation, restore safety, automatic retention, and environment-specific recovery acceptance;
- Audit Log documentation includes filtered CSV export and structured business-record evidence export;
- Privacy Data documents person-related discovery/export without implying automatic legal erasure decisions;
- Item Help documents identification, lifecycle, trade/compliance, logistics, serial/lot capture, traceability and current lifecycle/type workflow restrictions;
- Sales invoice Help distinguishes current operational invoice/credit-note behavior from EN 16931/XRechnung technical conformance;
- Finance Help distinguishes implemented F0/F1 accounting controls from future AR/AP/inventory-accounting/banking/reporting/localization work;
- Finance Help must not imply a default jurisdiction, currency, tax rate, chart of accounts, accounting standard, or statutory certification;
- Help must never document removed default credentials or imply legal certification from technical controls.

## Supported Markdown

The native renderer supports headings, paragraphs, ordered/unordered lists, bold, italic, inline code, notes/warnings, images, `topic:` internal links, and simple pipe tables. HTML and arbitrary external links are not supported.

## Updating Help

1. Verify the current UI, ViewModels, services, permissions, and navigation routes before changing documentation.
2. Create/update the Markdown topic in the appropriate category.
3. Keep stable topic IDs and deterministic ordering in `manifest.json`.
4. Add keywords for current terminology and shortcuts.
5. Add only valid `topic:` links and existing permission codes.
6. Increment the manifest version when the Help content contract changes materially, such as a topic ID, permission, mapping, or newly added/removed topic.
7. A wording-only article synchronization may retain the manifest version when the content contract is unchanged.
8. Run the Help Center regression tests; validation covers duplicate IDs, missing files, unknown permissions, and broken links.
9. Verify first-run/security/privacy/finance wording whenever authentication, data retention, audit, backup, database configuration, or financial behavior changes.

Do not document planned functionality as available.

## Context Help

Shell items and contextual pages carry a `HelpTopicId`. F1 resolves the current page and opens that topic. Missing/unavailable context falls back to `getting-started.first-login`.

## Permissions

`requiredPermission` uses the central permission catalog. `HelpService` filters topic lists, search, direct access, and related topics against effective permissions. Public Getting Started and Troubleshooting topics can omit a permission. Help visibility never grants business access.

For Finance, `Finance.View` does not imply General Ledger access. The General Ledger topic requires `FinanceGeneralLedger.View`, matching the service-layer authorization boundary.

## Search and diagnostics

Search is local and weighted across title, manifest keywords, headings, and body text. Related topics are permission-filtered.

Selected operation-error panels can expose **Open Help** and **Copy diagnostics**. Diagnostic text passes through `DiagnosticsSanitizer` to mask credentials, connection strings, hashes, salts, secrets, tokens, encryption keys, protected configuration, and sensitive SQL parameter values.
