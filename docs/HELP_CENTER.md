# Depot Help Center

Depot ships an integrated offline Help Center using embedded Markdown and native WPF `FlowDocument` rendering. Help opens as a regular workspace tab; F1 resolves the current application context without replacing other open workspaces.

## Content structure

Help content is versioned with the application under `src/Depot/Help`. The current manifest version is **1.5** and contains Getting Started, Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, Administration, and Troubleshooting topics.

`manifest.json` defines stable topic IDs, titles, categories, Markdown files, ordering, search keywords, optional required permissions, and related topics. IDs are application contracts and should not be renamed after use.

## Current shell guidance

Getting Started documentation must remain synchronized with the actual shell behavior:

- sign-in opens a tabless Welcome page rather than selecting a module
- the Welcome page uses the signed-in display name and local time-of-day greeting
- all workspace tabs are closeable; closing the final tab restores Welcome
- activity-bar navigation and contextual module navigation
- `Ctrl+P` Quick Open and keyed document tabs for supported records
- `Ctrl+Shift+P` Command Palette
- `Ctrl+W`, `Ctrl+Tab`, and `Ctrl+Shift+Tab`
- `Alt+Left` / `Alt+Right` navigation history
- F1 context Help
- database status detail on status-indicator hover
- clickable application version opening About
- unsaved-change protection

The Dashboard topic documents only metrics already supplied by `DashboardService`/`DashboardRepository`. Administrator behavior currently includes Inventory, Purchasing, Warehouse, Sales, Approvals, Administration, and Reports overview access; Help must not invent additional dashboard data.

## Supported Markdown

The native renderer supports headings, paragraphs, ordered/unordered lists, bold, italic, inline code, notes/warnings, images, `topic:` internal links, and simple pipe tables. HTML and arbitrary external links are not supported.

## Updating Help

1. Verify the current UI, ViewModels, services, permissions, and navigation routes before changing documentation.
2. Create/update the Markdown topic in the appropriate category.
3. Keep stable topic IDs and deterministic ordering in `manifest.json`.
4. Add keywords for current terminology and shortcuts.
5. Add only valid `topic:` links and existing permission codes.
6. Increment the manifest version when the content contract changes materially.
7. Run `HelpCenterTests`; validation covers duplicate IDs, missing files, unknown permissions, and broken links.

Do not document planned functionality as available.

## Context Help

Shell items and contextual pages carry a `HelpTopicId`. F1 resolves the current page and opens that topic. Missing/unavailable context falls back to `getting-started.first-login`.

## Permissions

`requiredPermission` uses the central permission catalog. `HelpService` filters topic lists, search, direct access, and related topics against effective permissions. Public Getting Started and Troubleshooting topics can omit a permission. Help visibility never grants business access.

## Search and diagnostics

Search is local and weighted across title, manifest keywords, headings, and body text. Related topics are permission-filtered.

Selected operation-error panels can expose **Open Help** and **Copy diagnostics**. Diagnostic text passes through `DiagnosticsSanitizer` to mask credentials, connection strings, hashes, salts, secrets, tokens, encryption keys, protected configuration, and sensitive SQL parameter values.
