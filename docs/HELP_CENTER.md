# Depot Help Center

Updated: 2026-08-28

Depot ships an integrated offline Help Center using embedded Markdown and native WPF `FlowDocument` rendering. Help opens as a regular workspace tab and resolves the current application context without replacing other open workspaces.

## Content structure

Help content is versioned with the application under `src/Depot/Help`. The current F2 manifest version is **1.11** and contains Getting Started, Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, Finance, Administration, and Troubleshooting topics.

`manifest.json` defines stable topic IDs, titles, categories, Markdown files, ordering, search keywords, optional required permissions, and related topics. IDs are application contracts and should not be renamed after use.

## Finance Help after F2

Manifest 1.11 contains three Finance topics:

- `finance.foundation` — Finance Foundation, guarded by `Finance.View`;
- `finance.general-ledger` — General Ledger and Posting, guarded by `FinanceGeneralLedger.View`;
- `finance.receivables` — Accounts Receivable, guarded by `FinanceReceivables.View`.

The General Ledger article documents the F1 posting invariants and now explains how F2 consumes that boundary transactionally.

The Accounts Receivable article documents:

- AR configuration and its Sales-schema dependency;
- Sales Invoice/Credit Note → AR → General Ledger atomic integration;
- receivable debit/credit open items;
- partial/full payment allocation and unapplied overpayments;
- later credit allocation;
- payment reversal including allocations made after the original payment;
- controlled write-offs and reversals;
- aging and customer statements;
- dunning policies/runs;
- F2 RBAC and sensitive write-off separation;
- currency/period/idempotency/audit behavior inherited from F1;
- the boundary to F3 Accounts Payable and later packages.

The Sales Invoice article links directly to Accounts Receivable and explains that configured AR/GL integration participates in the same invoice/credit-note posting transaction.

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
- Finance Help distinguishes implemented F0/F1/F2 accounting controls from future AP/inventory-accounting/banking/reporting/localization work;
- Finance Help must not imply a default jurisdiction, currency, tax rate, chart of accounts, accounting standard, bank account, write-off account, statutory dunning rule, or statutory certification;
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

Shell items and contextual pages carry a `HelpTopicId`. F2's Finance > Receivables page uses `finance.receivables`. Missing/unavailable context falls back to `getting-started.first-login`.

## Permissions

`requiredPermission` uses the central permission catalog. `HelpService` filters topic lists, search, direct access, and related topics against effective permissions. Public Getting Started and Troubleshooting topics can omit a permission. Help visibility never grants business access.

For Finance, `Finance.View` does not imply General Ledger or Receivables access. Their Help topics require `FinanceGeneralLedger.View` and `FinanceReceivables.View`, matching the service-layer authorization boundaries.

## Search and diagnostics

Search is local and weighted across title, manifest keywords, headings, and body text. Related topics are permission-filtered.

Selected operation-error panels can expose **Open Help** and **Copy diagnostics**. Diagnostic text passes through `DiagnosticsSanitizer` to mask credentials, connection strings, hashes, salts, secrets, tokens, encryption keys, protected configuration, and sensitive SQL parameter values.
