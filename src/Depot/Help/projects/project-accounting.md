# Projects and Cost Accounting

Projects provide a bounded operational cost-object layer over Depot's existing Finance, Purchasing and Sales evidence.

## Project lifecycle

Create a project with a legal entity, owner, optional customer, planned dates and description. Projects move through Draft, Active, On Hold, Closed or Cancelled. Closed and cancelled projects do not accept new operational attribution. Phases provide a deliberately shallow work breakdown.

## Financial views

Project financials are read-only projections and never form a second ledger.

- **Actuals** come from posted General Ledger evidence associated with attributed source documents.
- **Commitments** come from remaining open Purchase Order quantities and stay separate from posted actuals.
- **Budget variance** links existing Finance Budgeting lines to projects/phases; Finance remains authoritative for amount, account and period.
- Revenue, cost and margin follow the reporting-currency and account semantics of authoritative Finance records.

## Attribution

Supported attribution covers Purchase Orders, supplier documents, Sales Orders, Sales Invoices and controlled manual General Ledger journals. Attribution becomes immutable when the authoritative source is posted or reaches an immutable operational state. Attribution never rewrites source accounting evidence.

## Permissions

- `Projects.View` exposes project master data and the Projects workspace.
- `Projects.Manage` controls project, phase and budget-link maintenance.
- `ProjectAttributions.Manage` controls source attribution and still requires applicable source-domain authority.
- `ProjectFinancials.View` exposes actuals, commitments and budget variance.

UI visibility never replaces service-layer authorization.

## My Work

Owned Draft projects appear in **My drafts**. Active owned projects remain visible, while On Hold or overdue owned projects surface as exceptions. Recently closed projects can appear as completed work.

## Accounting boundary

General Ledger, subledgers, Financial Reporting and Finance Budgeting remain authoritative. Project Accounting stores lifecycle and attribution metadata and projects those authoritative records for analysis.
