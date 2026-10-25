# Finance Banking and Payments

Updated: 2026-10-25

## Scope

Banking and Payments provides banking evidence and payment orchestration within the existing Finance architecture. It does not create a parallel ledger or a parallel AP/AR settlement model.

Implemented components include `FinanceBankingService`, provider-neutral banking persistence, CSV and ISO 20022 `camt.053` statement normalization, and the **Finance > Banking** workspace. Banking persistence, including retained SEPA payment-export evidence, is part of the current Finance feature schema. See [Versioning](Versioning.md) for its current version.

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

After a statement or payment run references a bank account, its entity, accounting book, GL account, currency, IBAN, BIC and local account number cannot be reassigned. Create a distinct bank account for a different financial identity; descriptive name changes remain available.

Imported statements are immutable accounting evidence. Each import stores operation ID, SHA-256 content hash, selected bank account, source format/filename, external statement reference, statement period/balances and normalized signed statement lines. Operation IDs and hashes protect retry/idempotency semantics.

CSV supports comma/semicolon delimiters and quoted fields. ISO 20022 import supports `camt.053` account statements using namespace-neutral XML element matching. This is statement normalization, not certification of a bank-specific profile.

## Reconciliation

A statement line can reconcile to one active Accounts Receivable payment, Accounts Payable payment, or General Ledger entry containing the configured bank account. Currency, accounting book and the selected GL bank side’s signed amount must match exactly for every target kind. Payment nominal totals do not substitute for account-specific journal evidence: a payment split between two bank accounts is matched against each actual bank-side amount.

An active reconciliation consumes the pair of journal entry and bank GL account. The same evidence cannot be matched again through another payment or journal alias, including concurrent requests. Reversal releases that bank side while retaining the original match. A transfer between two distinct bank GL accounts remains independently reconcilable on both sides. Candidate selection applies the same account and active-use restrictions as the transactional command.

## Payment runs

Payment proposals contain supplier invoice open items and proposed settlement amounts. Cross-currency/cross-book or excessive allocations are rejected. Creator/approver separation is enforced and execution uses the existing Accounts Payable payment service with deterministic operation IDs.

Execution passes the selected bank identity into the payment request and its replay hash. The configured AP profile is used when it posts the exact credit to that bank. Otherwise one unambiguous active SupplierPayment profile in the same entity/book must provide the selected bank side; missing or ambiguous profiles fail before settlement or journaling. Other configured bank accounts cannot receive a nonzero side in that payment profile. Balanced non-bank clearing lines remain intact. Later profile edits cannot redirect an approved run to another bank. Standalone AP payments without an explicit bank continue to use the configured default profile.

An interrupted run from before bank identity was included in payment hashes can resume only when its original request hash matches, its retained run line identifies the same selected bank, and its existing journal credits that bank for the exact payment amount in the same book and currency. A historical payment on another bank is rejected for correction; replay never rewrites its journal or settles the payable again.

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
- `FinanceSepaPaymentProfiles.Manage`
- `FinanceSepaPaymentExports.Create`
- `FinanceSepaPaymentExports.Export`
- `FinanceSepaPaymentExports.Manage`

The standard Finance role receives operational Banking rights but not Payment Proposal approval; the Approver role receives the approval permission.

## Provider production acceptance

Finance schema 9 Banking DDL and service behavior are now exercised in the real provider matrix for the certified SQLite, SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11 baselines.

The live acceptance creates a real Finance bank account, imports and idempotently replays a CSV statement, reconciles a statement line to a General Ledger entry and reverses that reconciliation. The same provider jobs also exercise restart/re-entry and provider-native backup/restore for remote databases.

This proves the Depot database/runtime boundary; it does **not** certify direct bank connectivity, EBICS, PSD2/open-banking APIs, payment initiation, sanctions/AML/KYC decisioning, bank-specific `camt.053` profiles or jurisdiction-specific payment procedures. Those remain separate integration and organizational responsibilities.

See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) for the exact supported database baselines and recovery boundary.


## SEPA SCT payment export

Approved payment runs can be converted into a retained SEPA Credit Transfer customer-to-PSP artifact using ISO 20022 `pain.001.001.09`. The implementation pins the supported profile to **EPC SCT 2025 rulebook v1.1 / Customer-to-PSP implementation guidelines 2025 v1.0** rather than silently following later scheme revisions.

Generation is deterministic for one payment-run/export sequence. Depot stores the exact UTF-8 XML bytes, SHA-256, message/profile identifiers, transaction count, control sum, selected bank account, generation evidence and lifecycle state. Re-download returns those retained bytes; it never regenerates historical files from changed supplier, debtor or bank master data.

Creating a new file atomically locks and revalidates each current payable invoice balance. Generated, downloaded, externally submitted and accepted instructions in other runs reserve their unexecuted amounts. Their sum with the new instruction cannot exceed the remaining payable balance. Cancelled, rejected and superseded exports release their reservation; executing a run line consumes the payable balance instead. A settled or voided item cannot produce a new instruction. Superseding an eligible file replaces its reservation atomically.

Returning an existing export or downloading its retained bytes remains possible after settlement and never creates a new instruction. External submission and settlement still require the documented manual evidence; a retained file is not proof that a bank executed it.

A payment file requires an approved, unexecuted EUR payment run, a valid debtor bank IBAN, an explicit structured debtor payment profile and an active structured creditor payment profile for every supplier. Depot deliberately emits structured postal-address elements and does not emit unstructured-only `AdrLine` addresses.

Debtor and creditor IBAN country codes must also belong to the 41-country EPC SEPA geographical scope verified on 2026-09-23. This keeps non-SEPA credit transfers outside the supported artifact profile even if an IBAN is otherwise checksum-valid.

Under the 2025 SCT rulebook version 1.1, unstructured postal addresses are no longer permitted in EPC payment messages from 15 November 2026. Depot already emits structured payment-party addresses only, so generated artifacts remain compatible with that transition.

External submission is intentionally outside Depot's database transaction. V1 records manual evidence for `SubmittedExternally`, `Accepted`, `Rejected` and `Cancelled`; it does not claim that bank submission is ACID with payment-file creation.

See [SEPA Payment Export](SepaPaymentExport.md) for the exact profile, lifecycle, RBAC and compliance boundary.
