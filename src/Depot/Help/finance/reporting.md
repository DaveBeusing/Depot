# Financial Reporting

Use **Finance > Financial Reporting** to create accounting reports from the existing Finance ledger/subledgers, configure report classifications, export CSV, and retain immutable report snapshots.

## Reports

The workspace supports:

- Trial Balance
- General Ledger detail
- Balance Sheet
- Profit & Loss
- Cash Flow
- Accounts Receivable Aging
- Accounts Payable Aging
- Tax Summary
- Inventory Valuation
- Cost of Goods Sold

Select the accounting book and the required date range or as-of date for the report type. GL-derived reports can optionally be filtered by an accounting dimension and dimension value.

## Currency behavior

General Ledger-derived reports use the journal line values already persisted in the accounting book's **Reporting Currency**. These values preserve the exchange-rate snapshot used when the source journal was posted.

Accounts Receivable and Accounts Payable aging are shown in each open item's transaction currency. Depot does not silently apply a current exchange rate or guess a historical rate for these subledger balances.

Inventory Valuation reconstructs historical F4 valuation evidence at the requested cutoff.

## Account mappings

The **Mappings** area configures reporting meaning for individual GL accounts. A mapping can define:

- financial-statement section;
- cash-flow category;
- tax category;
- whether the account itself is a cash account;
- whether the account belongs to Cost of Goods Sold;
- display sort order and active state.

Mappings are validated against the account type. Cash accounts do not classify themselves as Operating, Investing or Financing counterpart accounts. COGS requires an expense account.

Cash Flow and Tax Summary do **not** infer accounting meaning from an account number or account name. Configure the required mappings explicitly before relying on those reports.

## Dimension filtering

For GL-based reports you may supply a Dimension ID and Dimension Value ID together. Depot filters against the dimensions persisted on journal entry lines. Supplying only one of the two values is rejected.

AR/AP aging does not pretend to support GL-line dimensions because those dimensions are not persisted on open items.

## CSV export

**Export CSV** creates a deterministic culture-invariant CSV from the currently generated result. Export requires `FinanceFinancialReporting.Export`.

CSV is a technical export format. It is not a statutory filing format or jurisdiction-specific financial statement certification.

## Report snapshots

**Create Snapshot** persists an immutable `FinanceReportSnapshot` containing:

- the report kind and parameters;
- accounting book/date/dimension scope;
- a SHA-256 parameter hash;
- a SHA-256 content hash;
- canonical CSV content;
- creator and UTC creation time.

Snapshot creation uses an operation ID. Retrying the same operation with identical report content returns the existing snapshot. Reusing the same operation ID for different content is rejected.

Snapshots are retained AuditEvidence. Create a new snapshot for a later assessment instead of attempting to change old evidence.

## Permissions

- `FinanceFinancialReporting.View` — open the workspace and generate reports
- `FinanceFinancialReporting.Manage` — configure account mappings
- `FinanceFinancialReporting.Export` — export CSV
- `FinanceReportSnapshots.Create` — retain immutable report snapshots

The service permission check is authoritative even if a UI control is visible.

## Important boundaries

Financial Reporting uses existing F1-F5 accounting evidence. It does not create a second ledger, change posted journals, or invent missing historical FX data.

Depot F6 does not claim HGB, IFRS, US-GAAP, tax-return, GoBD or other statutory certification for report layouts. Country-specific statutory presentation and filing behavior belongs to future localization/compliance packages.

Related topics: [General Ledger and Posting](topic:finance.general-ledger), [Accounts Receivable](topic:finance.receivables), [Accounts Payable](topic:finance.payables), [Inventory Accounting](topic:finance.inventory-accounting), [Banking and Payments](topic:finance.banking), [Audit Log](topic:administration.audit-log).
