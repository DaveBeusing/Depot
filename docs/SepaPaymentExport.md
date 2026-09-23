# SEPA Payment Export

Updated: 2026-09-23

## Scope

Depot generates a controlled SEPA Credit Transfer customer-to-PSP payment-initiation artifact from an approved Finance payment run.

The supported profile is explicitly pinned to:

- EPC SCT 2025 rulebook version 1.1;
- EPC SCT Customer-to-PSP implementation guidelines 2025 version 1.0;
- ISO 20022 message `pain.001.001.09`;
- EUR credit transfers only.

This is an implementation/interoperability boundary. XML validation and repository tests do **not** constitute EPC, bank or clearing-network certification.

## Architecture

```text
FinanceBankingView
  -> FinanceBankingViewModel
    -> FinanceSepaPaymentExportService
      -> FinanceBankingService / FinanceBankingRepository
      -> FinanceSepaPaymentExportRepository
        -> DatabaseAccess
```

Business and authorization rules remain in the service layer. Persistence retains the exact XML artifact and status history.

## Eligibility and validation

Generation requires an approved payment run that has not started AP execution, EUR on the run and bank account, individual amounts from 0.01 through 999999999.99 EUR with at most two decimal places, debtor/creditor names of at most 70 characters, a valid debtor IBAN, explicit structured debtor payment data, an active structured creditor profile for every supplier, valid creditor IBANs, optional syntactically valid BICs, a non-past requested execution date, bounded remittance text, and no more than 500 transfers per artifact.

The supported IBAN geography is pinned to the **41-country EPC SEPA geographical scope verified on 2026-09-23**. Debtor and creditor IBANs whose country code falls outside that scope are rejected even when their IBAN checksum is formally valid. A future EPC scope expansion requires an explicit compatibility update rather than being accepted silently.

The general Supplier `Address` field is intentionally not parsed or guessed into payment data. Structured payment profiles are explicit Finance configuration.

## Postal-address policy

Depot emits structured `PstlAdr` elements and does not generate unstructured-only `AdrLine` payment addresses.

The 2025 EPC guidance originally scheduled the end of unstructured addresses for 15 November 2026. On 9 September 2026 the EPC announced that this deadline would be postponed, with a revised date to follow. Depot nevertheless keeps structured-address-only output as its supported profile.

## Deterministic identity and retained evidence

For each payment run/export sequence Depot deterministically derives the message ID, payment-information ID, per-line end-to-end ID and export identity key. The UTF-8 XML bytes are hashed with SHA-256 and retained with source payment-run identity, profile/message versions, generated timestamp/user, transaction count, control sum, bank account, file name, lifecycle state and supersede linkage.

A normal repeated generation request returns the already retained artifact. A changed instruction requires explicit supersede/regenerate behavior, creates a new sequence and preserves the previous artifact.

## Lifecycle and external boundary

The normal retained lifecycle is `Generated -> Downloaded -> SubmittedExternally -> Accepted | Rejected`. A generated/downloaded artifact may be marked `Cancelled`; eligible regeneration marks the prior artifact `Superseded`.

External submission and acceptance/rejection are manual evidence. Depot does not perform EBICS, PSD2/Open Banking, host-to-host upload, clearing submission or automatic bank status polling.

## Authorization

Separate service-layer permissions are used for each payment-export authority:

- `FinanceSepaPaymentProfiles.Manage` — maintain debtor/creditor payment profiles;
- `FinanceSepaPaymentExports.Create` — validate and generate retained artifacts;
- `FinanceSepaPaymentExports.Export` — read/download the exact retained XML payload;
- `FinanceSepaPaymentExports.Manage` — record external lifecycle/status evidence.

The built-in Finance and Treasury roles receive these operational permissions. The Approver role keeps payment-proposal approval authority without implicit file-generation/download rights. Service-layer authorization remains authoritative.

## Persistence and migration

Finance schema **12** adds `FinanceSepaDebtorProfiles`, `FinanceSepaCreditorProfiles`, `FinanceSepaPaymentExports` and `FinanceSepaPaymentExportStatusHistory`. Historical XML bytes are never rewritten after master-data changes.

## Validation evidence

Repository validation covers deterministic identity/hash behavior, control totals, required structured payment data, EUR-only scope, no unstructured `AdrLine`, immutable re-download, supersede/lifecycle rules, permission separation and SQLite/SQL Server/MariaDB/MySQL persistence paths.

A project-authored pinned XSD subset validates the exact XML structure emitted by Depot. It is a regression fixture, not a redistributed third-party certification artifact and not proof of bank acceptance.

## Out of scope

SEPA Instant, direct debit/`pain.008`, non-EUR/non-SEPA payments, EBICS, bank-specific host-to-host connectivity, PSD2/Open Banking submission, automatic status polling, sanctions/AML/KYC decisioning and bank certification remain outside this capability.
