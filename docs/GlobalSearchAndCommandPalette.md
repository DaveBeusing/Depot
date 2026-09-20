# Global Search and Command Palette

Updated: 2026-09-20

## Purpose

Depot provides one keyboard-first entry point for fast ERP navigation and bounded business-record lookup without moving query logic into the WPF shell.

The canonical shortcut is:

```text
Ctrl+K
```

The palette combines immediate local results with permission-aware asynchronous record search. Existing `Ctrl+P` Quick Open and `Ctrl+Shift+P` command shortcuts remain available for compatibility.

## Architecture

```text
ShellPaletteWindow
  -> GlobalSearchService
      -> IGlobalSearchProvider
          -> GlobalSearchReadRepository
              -> DatabaseAccess
```

The palette never queries repositories or SQL directly. `GlobalSearchService` filters providers by their current authorization eligibility before any provider task is started. Built-in providers use the dedicated, read-only `GlobalSearchReadRepository`; it exposes only bounded search projections and does not provide mutation APIs. Opening a selected record still goes through the existing application/service authorization boundary.

Navigation, shell commands and already-known recent entries are resolved locally and displayed before any record query starts. A database-backed search failure therefore does not prevent workspace navigation or command execution.

## Search domains

The first production provider set covers:

- Items
- Customers
- Suppliers
- Sales Orders
- Purchase Orders
- Sales Invoices
- Finance Journal Entries

Help topics are searched through the existing `IHelpService`, which preserves help-topic permission filtering.

Each business result uses a stable semantic result identifier such as:

```text
item:42
customer:17
supplier:8
sales-order:104
purchase-order:91
invoice:55
journal-entry:7001
```

These identifiers are UI navigation keys only. They do not bypass the service authorization required to load the target record.

## Security

Search providers are fail-closed for permissions:

- Items require `ItemsView`.
- Suppliers require `SuppliersView`.
- Purchase orders require `PurchaseOrdersView`.
- Customers require `CustomersView`.
- Sales orders require `SalesOrdersView`.
- Sales invoices require `SalesInvoicesView`.
- Journal entries require both `FinanceGeneralLedgerView` and `FinanceFinancialReportingView` because the current shell opens journal results through the Financial Reporting workspace.

Unauthorized providers return no entries and do not expose counts, identifiers, titles, or other record metadata.

The underlying services continue to enforce their own permissions when the palette opens a selected record.

## Hot-path behavior

The command palette is designed to remain responsive under ordinary ERP data volumes:

- navigation and command matches are synchronous and database-independent;
- record lookup starts only after two non-whitespace characters;
- the UI uses a cancellable 120 ms debounce;
- a new query cancels the previous query;
- only permission-eligible providers are started;
- eligible providers execute concurrently;
- each provider receives a bounded result budget;
- built-in record providers use count-free Top-N reads;
- the aggregate result set is capped;
- duplicate stable identifiers are removed deterministically;
- exact matches rank before prefixes, word prefixes and general contains matches.

There is no unbounded result set and no external full-text engine. One- and two-character record queries use exact/prefix paths only; from three characters, contains remains available as a fallback.

Finance journal lookup is filtered server-side with the same bounded Top-N contract. A positive numeric query can also match a journal-entry ID directly; the previous broad recent-window load plus in-memory filtering is no longer used.

## UX

`Ctrl+K` opens one searchable surface for:

1. recent records;
2. workspaces and workspace sections;
3. registered shell commands;
4. permission-filtered Help topics;
5. permission-filtered business records.

Arrow keys change selection, `Enter` opens the selected result, and `Esc` closes the palette. Static results remain visible while asynchronous business search is running.

The shell uses one canonical group order across Command Palette, Quick Open and Global Search:

1. **Suggested** — commands that are actually bound to the active workspace plus permission-filtered Help matches;
2. **Commands** — other currently available shell and workflow commands;
3. **Workspaces** — permitted workspaces and workspace sections;
4. **Records** — permission-filtered business-record results;
5. **Recent** — session-local recently opened record results.

A workflow command becomes **Suggested** only when its registered context route matches the active workspace/page. Registration itself remains permission-safe, and execution still uses the existing command/service boundary.

## Persistence and schema

Global search introduces no persisted data and does not change any database schema.

```text
Core database schema: 30
UserPreferences feature schema: 2
```

Global Search itself introduces no persisted data. UserPreferences schema 2 is the current product baseline because later workspace-productivity persistence added favorites, recents and default-landing preferences.

## Tests

`GlobalSearchServiceTests` covers:

- minimum-query behavior;
- deterministic ranking;
- stable-ID deduplication;
- result bounding;
- cancellation of in-flight provider searches;
- duplicate provider-ID rejection;
- permission-ineligible providers are not invoked;
- count-free bounded Global Search reads;
- Finance Journal server-side filtering;
- short-query contains suppression;
- rapid-input debounce/cancellation/stale-result safety.

Search performance contracts additionally cover the 10k/100k benchmark probe, identifier-heavy repository coverage, DataGrid virtualization/recycling and CollectionSynchronizer refresh behavior. Existing authorization and shell-navigation regression suites remain part of the required CI and quality gates.
