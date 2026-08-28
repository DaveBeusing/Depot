# Finance F5 — Banking and Payments

Updated: 2026-08-28

## Scope

F5 adds the banking evidence and payment-orchestration layer to the existing Finance architecture. It does not create a parallel ledger or a parallel AP/AR settlement model.

Implemented components:

- `FinanceBankingService` — authorization, bank-account configuration, statement import, reconciliation, payment-run orchestration and cash position;
- `FinanceBankingRepository` — provider-neutral banking persistence;
- `FinanceBankStatementParser` — CSV and ISO 20022 `camt.053` normalization;
- **Finance > Banking** — WPF workspace for Cash Position, Bank Accounts, Statements/Reconciliation and Payment Runs.

Finance feature schema: **7**.

## Architecture

```text
FinanceBankingView
  ↓
FinanceBankingViewModel
  ↓
FinanceBankingService
  ├── FinanceAccountsPayableService
  └── FinanceBankingRepository
       ↓
DatabaseAccess
```

General Ledger truth remains in F1. Customer settlement truth remains in F2 AR. Supplier settlement truth remains in F3 AP.

## Bank accounts

A bank account is configured against:

- legal entity;
- accounting book;
- active direct-posting General Ledger account;
- explicit ISO currency;
- optional bank name, IBAN, BIC and local account identifier.

The service validates legal-entity/book/account compatibility. No country, bank, IBAN, clearing account, currency or chart default is inferred.

## Statement evidence

Imported statements are retained accounting evidence and are not edited after import. Each import stores:

- operation ID;
- SHA-256 import hash;
- selected bank account;
- source format and optional filename;
- external statement reference;
- statement period and balances;
- signed normalized statement lines.

Operation IDs and content hashes prevent duplicate/retry imports. The same operation ID with different content is rejected.

### CSV

CSV supports comma or semicolon delimiters and quoted fields. Required columns are booking date and amount. Optional fields include value date, currency, external transaction ID, reference/remittance, counterparty and transaction code.

### ISO 20022

F5 parses ISO 20022 `camt.053` account statements using namespace-neutral XML element matching. Credit entries become positive signed amounts and debit entries negative signed amounts. Opening/closing booked balances are consumed when present.

This is a statement-import implementation, not certification of a specific bank's ISO 20022 profile.

## Reconciliation

A statement line may be reconciled to exactly one active target:

1. F2 Accounts Receivable payment;
2. F3 Accounts Payable payment;
3. F1 General Ledger entry containing the configured bank GL account.

Currency, accounting book and signed amount must match exactly. Generic Finance has no hidden matching tolerance. Reversed AR/AP payments cannot be matched.

Reconciliation creates separate immutable evidence. Corrections use an explicit reconciliation reversal; the statement, payment, journal and original match are retained.

## Payment runs

A payment proposal contains supplier invoice open items and proposed positive settlement amounts. The service rejects voided/non-invoice items, cross-currency/cross-book items and amounts above the remaining AP balance.

Payment-run lifecycle:

```text
Draft → Approved → PartiallyExecuted → Executed
```

The creator cannot approve the same run. The default Finance role does not receive `FinancePaymentProposals.Approve`; the Approver role does.

Execution uses the existing `FinanceAccountsPayableService.PostPaymentAsync` and allocates the created supplier payment to the proposed AP open item. A deterministic execution operation ID makes retries idempotent.

An external banking system cannot participate in Depot's database transaction. Therefore F5 deliberately models payment execution as a recoverable idempotent workflow rather than claiming distributed ACID semantics. A deployment that adds an actual bank connector must retain external submission/status evidence and map its retry semantics onto the F5 execution operation ID.

## Cash position

For every active bank account, Cash Position shows:

- most recent imported statement date and closing balance;
- configured bank GL balance at that statement date;
- difference;
- unreconciled statement-line count.

Differences are reconciliation signals. F5 does not auto-create adjusting journals.

## RBAC

F5 permissions:

- `FinanceBanking.View`
- `FinanceBanking.Manage`
- `FinanceBankStatements.Create`
- `FinanceBankReconciliation.Manage`
- `FinancePaymentProposals.Create`
- `FinancePaymentProposals.Approve`
- `FinancePaymentRuns.Post`
- `FinanceCashPosition.View`

The standard Finance role receives operational Banking rights but not Payment Proposal approval. The standard Approver role receives Payment Proposal approval plus Banking view access.

## Provider and production boundary

Schema 7 has provider-specific DDL for SQLite, SQL Server and MySQL/MariaDB. Production acceptance still requires live server migration, locking, retry, backup/recovery and load testing.

F5 does not claim:

- direct bank connectivity;
- EBICS support;
- PSD2/open-banking API conformance;
- payment initiation certification;
- sanctions/AML/KYC decisioning;
- country-specific payment-file formats or statutory approval rules;
- bank-specific ISO 20022 profile certification.

Those are separate integration, localization and organizational acceptance responsibilities.

## Downstream reporting and next package

**F6 — Financial Reporting is implemented** and consumes the single F1 ledger plus F2/F3/F4/F5 evidence for trial balance, GL detail, financial statements, cash flow and related reporting.

The next Finance package is **F7 — Localization Framework**.
