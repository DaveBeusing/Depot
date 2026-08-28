# Banking and Payments

Use **Finance > Banking** for bank-account master data, external bank statements, payment proposals, bank reconciliation, and cash position.

## Bank accounts

Each bank account is tied explicitly to one legal entity, one accounting book, one active direct-posting General Ledger account, and one currency. Depot does not invent a bank account, clearing account, IBAN, BIC, or currency default.

Changing bank-account configuration requires `FinanceBanking.Manage`. The service layer validates that the legal entity, accounting book, and General Ledger account are compatible.

## Bank statement import

F5 accepts two normalized statement inputs:

- CSV with booking date and amount columns plus optional currency, value date, transaction ID, reference, counterparty, and transaction code;
- ISO 20022 `camt.053` statement XML.

Imported statements and lines are retained external accounting evidence. Import uses an operation ID and SHA-256 content hash to prevent accidental duplicate imports. Reusing an operation ID for different content is rejected.

Statement currency must match the selected bank account. Opening balance plus imported signed transactions must equal closing balance exactly; the generic core applies no hidden reconciliation tolerance.

## Reconciliation

A statement line can be matched to:

- an Accounts Receivable customer payment;
- an Accounts Payable supplier payment;
- a General Ledger entry containing the configured bank GL account.

The amount, currency, and accounting book must match exactly. Reversed AR/AP payments cannot be matched. One statement line can have only one active reconciliation.

A reconciliation does not rewrite the bank statement, subledger payment, or journal. It creates separate retained evidence. A correction uses **Reverse reconciliation**, which preserves the original match and its reversal evidence.

## Payment proposals and execution

Payment proposals are created from active supplier-invoice open items. The proposed amount cannot exceed the outstanding payable amount and must use the same currency/accounting book as the selected bank account.

The creator of a payment run cannot approve that same run. The default Finance role can create and execute payment proposals but does not receive `FinancePaymentProposals.Approve`; the standard Approver role receives the approval right.

Executing a payment-run line posts the actual supplier payment through the existing Accounts Payable service and allocates it to the proposed payable open item. The deterministic execution operation ID makes retries idempotent. Depot does not pretend that an external banking network participates in the same database transaction; after an external-side interruption, retry the same payment-run line rather than creating a replacement run.

Actual supplier-payment reversal remains the Accounts Payable reversal workflow. The payment run is retained as execution evidence.

## Cash position

Cash Position compares the most recent imported statement closing balance with the configured bank General Ledger balance at the statement date and shows the number of unreconciled statement lines. A difference is an operational reconciliation signal, not an automatic correcting journal.

## Permissions

- `FinanceBanking.View` — open Banking and read accounts/statements/payment runs.
- `FinanceBanking.Manage` — maintain bank-account configuration.
- `FinanceBankStatements.Create` — import bank statements.
- `FinanceBankReconciliation.Manage` — create and reverse reconciliations.
- `FinancePaymentProposals.Create` — prepare payment runs.
- `FinancePaymentProposals.Approve` — approve payment runs.
- `FinancePaymentRuns.Post` — execute approved payment-run lines through Accounts Payable.
- `FinanceCashPosition.View` — read cash-position information.

UI visibility is not an authorization boundary. The Finance services enforce the permissions.

## Control and compliance boundary

F5 provides technical accounting controls and ISO 20022 `camt.053` import. It does not claim connectivity to a bank, payment initiation certification, PSD2/open-banking conformance, sanctions screening, AML/KYC decisioning, EBICS support, country-specific payment-file approval, or statutory cash-management compliance. These require explicit later integrations, localization, and production acceptance.

Related topics: [Accounts Payable](topic:finance.payables), [Accounts Receivable](topic:finance.receivables), [General Ledger](topic:finance.general-ledger), and [Finance Foundation](topic:finance.foundation).
