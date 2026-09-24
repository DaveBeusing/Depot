# Project Accounting

## Purpose and authority

Project Accounting is a bounded operational and analysis layer over Depot's existing accounting authorities. It does not maintain a second ledger and it does not recalculate posted accounting truth.

```text
Project / Project Phase
  -> operational-document attribution
  -> existing GL / AP / AR / Inventory evidence
  -> project actuals, commitments and budget comparison
```

`FinanceGeneralLedgerService`, the Finance subledgers, Financial Reporting and Finance Budgeting remain authoritative. Project Accounting stores project lifecycle data, explicit source attribution and links to existing Finance budget lines.

## Architecture decision

Project Accounting owns an independent feature schema named `ProjectAccounting`. This preserves project lifecycle, ownership and phase semantics without forcing projects into an untyped accounting dimension or coupling their persistence lifetime to the Finance schema.

The feature schema contains:

- `Projects` and shallow `ProjectPhases`;
- `ProjectAttributions` for controlled links to Purchase Orders, supplier documents, Sales Orders, Sales Invoices and manual General Ledger journals;
- `ProjectBudgetLineLinks`, which links existing `FinanceBudgetLines` instead of storing a duplicate budget amount.

Core and Finance schema versions are unchanged by this feature schema.

## Financial semantics

Actual revenue and cost are read-only projections over persisted General Ledger lines associated with explicitly attributed source documents. Revenue uses reporting credit less reporting debit; cost uses reporting debit less reporting credit for expense accounts. Reversal journals therefore flow through the same immutable evidence instead of being special-cased into a second calculation store.

Open purchase commitments are operational evidence derived from the remaining quantity of Ordered or Partially Received Purchase Order lines. Commitments are deliberately shown separately from posted actuals. The current Purchase Order contract does not persist an explicit document currency or legal entity, so project commitment amounts must not be silently combined with currency-qualified General Ledger totals.

Budget comparison uses `FinanceBudgetLines` and their existing budget-version authority. A project link contributes attribution metadata only; amount, account and accounting period remain owned by Finance Budgeting.

## Lifecycle and integrity

Project codes are unique within a legal entity. Projects support Draft, Active, OnHold, Closed and Cancelled states. Project phases are deliberately shallow, retain optional planned dates and start in Planned. Explicit phase transitions allow Planned -> Active/Cancelled and Active -> Completed/Cancelled; terminal phases cannot be edited. A project's legal entity is immutable after creation so existing attribution, budget links and Finance reconciliation cannot be detached from their accounting scope.

New operational attribution is blocked when a project is Closed or Cancelled. Attribution becomes immutable once the authoritative source is posted or otherwise enters an immutable operational state. Manual journal attribution is limited to existing manual General Ledger entries.

All mutable project records use optimistic concurrency. Lifecycle, attribution and budget-link changes are auditable through Depot's existing Audit boundary.

## Provider support

The `ProjectAccounting` feature schema is provider-neutral and provisioned for SQLite, SQL Server, MariaDB and MySQL through the existing database abstraction. Provider acceptance must cover schema creation and a project persistence round trip before the feature is considered complete.


## Workspace and My Work

The Projects workspace uses Depot standard custom controls and exposes project master/detail, shallow phases, business attachments and read-only financial views. Project attachments use the shared versioned Business Attachments boundary and the existing project view/manage permissions. Owned Draft projects appear in My Work drafts; active owned projects remain visible, On Hold or overdue owned projects surface as exceptions, and recently closed owned projects can appear as completed work.

Contextual Help topic `projects.accounting` explains the accounting authority boundary and project permissions.

## Validation

Focused regression coverage verifies lifecycle state behavior, independent SQLite feature migration, service/repository layering, authoritative Finance evidence usage, workspace integration and My Work integration.

The shared production-provider acceptance suite provisions the feature through `DatabaseProvisioningService` and asserts the `ProjectAccounting` feature version and Projects table on SQLite, SQL Server, MariaDB and MySQL. MariaDB and MySQL remain independently accepted despite sharing the MySQL connector family.
