# Finance Banking and Payments

Updated: 2026-09-08

## Scope

Banking and Payments provides banking evidence and payment orchestration within the existing Finance architecture. It does not create a parallel ledger or a parallel AP/AR settlement model.

Implemented components include `FinanceBankingService`, provider-neutral banking persistence, CSV and ISO 20022 `camt.053` statement normalization, and the **Finance > Banking** workspace. Banking persistence, including retained SEPA payment-export evidence, is part of the current Finance schema **12**.

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

## Provider production acceptance

Finance schema 9 Banking DDL and service behavior are now exercised in the real provider matrix for the certified SQLite, SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11 baselines.

The live acceptance creates a real Finance bank account, imports and idempotently replays a CSV statement, reconciles a statement line to a General Ledger entry and reverses that reconciliation. The same provider jobs also exercise restart/re-entry and provider-native backup/restore for remote databases.

This proves the Depot database/runtime boundary; it does **not** certify direct bank connectivity, EBICS, PSD2/open-banking APIs, payment initiation, sanctions/AML/KYC decisioning, bank-specific `camt.053` profiles or jurisdiction-specific payment procedures. Those remain separate integration and organizational responsibilities.

See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) for the exact supported database baselines and recovery boundary.


## SEPA SCT payment export

Approved payment runs can be converted into a retained SEPA Credit Transfer customer-to-PSP artifact using ISO 20022 `pain.001.001.09`. The implementation pins the supported profile to **EPC SCT 2025 rulebook v1.1 / Customer-to-PSP implementation guidelines 2025 v1.0** rather than silently following later scheme revisions.

Generation is deterministic for one payment-run/export sequence. Depot stores the exact UTF-8 XML bytes, SHA-256, message/profile identifiers, transaction count, control sum, selected bank account, generation evidence and lifecycle state. Re-download returns those retained bytes; it never regenerates historical files from changed supplier, debtor or bank master data.

A payment file requires an approved, unexecuted EUR payment run, a valid debtor bank IBAN, an explicit structured debtor payment profile and an active structured creditor payment profile for every supplier. Depot deliberately emits structured postal-address elements and does not emit unstructured-only `AdrLine` addresses.

Debtor and creditor IBAN country codes must also belong to the 41-country EPC SEPA geographical scope verified on 2026-09-23. This keeps non-SEPA credit transfers outside the supported artifact profile even if an IBAN is otherwise checksum-valid.

The EPC announced on 9 September 2026 that the previously planned 15 November 2026 end-date for unstructured addresses would be postponed and that a new date would follow. Depot keeps its stricter structured-address-only export rule as a product interoperability constraint.

External submission is intentionally outside Depot's database transaction. V1 records manual evidence for `SubmittedExternally`, `Accepted`, `Rejected` and `Cancelled`; it does not claim that bank submission is ACID with payment-file creation.

See [SEPA Payment Export](SepaPaymentExport.md) for the exact profile, lifecycle, RBAC and compliance boundary.
