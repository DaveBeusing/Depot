# Finance Budgeting and Variance Analysis

## Purpose

Finance Budgeting provides controlled planning by legal entity, accounting book, fiscal year, account, accounting period and optional accounting dimensions while keeping planning evidence separate from accounting truth. Actual values always come from `FinanceFinancialReportingService` / General Ledger.

## Architecture

```text
FinanceBudgetingView
  → FinanceBudgetingViewModel
    → FinanceBudgetingService
      → FinanceBudgetingRepository
        → DatabaseAccess
```

Finance schema **11** adds `FinanceBudgetVersions` and `FinanceBudgetLines` with optimistic concurrency, lifecycle/source/approval evidence and bounded indexes.

## Lifecycle

Supported states are `Draft`, `PendingApproval`, `Approved`, `Locked`, `Superseded` and `Archived`. Only Draft is editable. Approved/Locked history is immutable; amendments create a new Draft version.

## Planning operations

- version/prior-year copy with source evidence;
- deterministic equal-period annual spread with final-period remainder;
- CSV preview, validation, atomic apply and bounded export;
- account/period plus optional accounting-dimension lines.

## Approval and permissions

Approval uses `ApprovalSubjectKind.FinanceBudget` with amount, currency, legal entity and book. The Finance role has View/Manage/Lock but not Approve; Approver has View/Approve but not Manage.

## Actual vs Budget

Budget aggregates are joined to actuals from Financial Reporting/GL. `Variance = Actual - Budget`; zero Budget yields no percentage variance. P&L and general account/dimension analysis are supported.

## Workspace and My Work

Finance > Budgeting provides versions, lines, import/export, approval, locking, KPIs and variance. My Work includes owned drafts, waiting submissions and policy-eligible approvals.

## Validation

Coverage includes schema migration, spread/remainder math, permission segregation, immutable-state contracts, UI/service layering and live-provider budget persistence/decimal aggregation.

See [Finance Architecture](FinanceArchitecture.md), [Finance Reporting](FinanceReporting.md), [Finance Compliance](FinanceCompliance.md), [Approval Policies](ApprovalPolicies.md), [Role Personas](RolePersonas.md), [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) and [Versioning](Versioning.md).
