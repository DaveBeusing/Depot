# Finance Banking and Payments

Updated: 2026-08-28

## Scope

Banking and Payments provides banking evidence and payment orchestration within the existing Finance architecture. It does not create a parallel ledger or a parallel AP/AR settlement model.

Implemented components include `FinanceBankingService`, provider-neutral banking persistence, CSV and ISO 20022 `camt.053` statement normalization, and the **Finance > Banking** workspace. Banking persistence is part of Finance schema **9**.

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

General Ledger truth remains in `FinanceGeneralLedgerService`. Customer settlement truth remains in Accounts Receivable and supplier settlement truth remains in Accounts Payable.

## Bank accounts and statements

Bank accounts bind a legal entity, accounting book, active direct-posting GL account and explicit ISO currency. Optional bank name, IBAN, BIC and local account identifiers are retained; no hidden country, bank, currency, clearing-account or chart defaults are inferred.

Imported statements are immutable accounting evidence. Each import stores operation ID, SHA-256 content hash, selected bank account, source format/filename, external statement reference, statement period/balances and normalized signed statement lines. Operation IDs and hashes protect retry/idempotency semantics.

CSV supports comma/semicolon delimiters and quoted fields. ISO 20022 import supports `camt.053` account statements using namespace-neutral XML element matching. This is statement normalization, not certification of a bank-specific profile.

## Reconciliation

A statement line can reconcile to one active Accounts Receivable payment, Accounts Payable payment, or General Ledger entry containing the configured bank account. Currency, accounting book and signed amount must match exactly. Corrections use explicit reconciliation reversal while retaining statement, payment/journal and original match evidence.

## Payment runs

Payment proposals contain supplier invoice open items and proposed settlement amounts. Cross-currency/cross-book or excessive allocations are rejected. Creator/approver separation is enforced and execution uses the existing Accounts Payable payment service with deterministic operation IDs.

External banking systems cannot participate in Depot's database transaction. Bank connectivity must therefore map external submission/status/retry evidence onto Depot's idempotent execution identity rather than assuming distributed ACID semantics.

## Cash position

Cash Position compares the latest imported statement closing balance with the configured bank GL balance and shows unreconciled statement-line counts. Differences are reconciliation signals; Depot does not automatically create adjusting journals.

## RBAC

- `FinanceBanking.View`
- `FinanceBanking.Manage`
- `FinanceBankStatements.Create`
- `FinanceBankReconciliation.Manage`
- `FinancePaymentProposals.Create`
- `FinancePaymentProposals.Approve`
- `FinancePaymentRuns.Post`
- `FinanceCashPosition.View`

The standard Finance role receives operational Banking rights but not Payment Proposal approval; the Approver role receives the approval permission.

## Production boundary

Provider-neutral DDL exists for SQLite, SQL Server and MySQL/MariaDB. Production acceptance still requires live migration, locking, retry, backup/recovery and load testing. Direct bank connectivity, EBICS, PSD2/open-banking conformance, payment-initiation certification, sanctions/AML/KYC decisioning and country/bank-specific payment profiles are separate integration and organizational responsibilities.
