# Finance Fixed Assets

Updated: 2026-10-25

## Purpose

Depot Fixed Assets is a production-oriented Finance subledger for asset master data, capitalization, deterministic depreciation, controlled adjustments and transfers, disposal, retained accounting evidence and subledger-to-General-Ledger reconciliation.

It does **not** create a second General Ledger. Every accounting mutation posts through `FinanceGeneralLedgerService`; Fixed Assets stores subledger state and evidence linked to the resulting journal entries.

## Architecture

The implementation preserves Depot's standard dependency direction:

```text
FinanceFixedAssetsView
  → FinanceFixedAssetsViewModel
    → FinanceFixedAssetService
      → FinanceFixedAssetRepository
        → DatabaseAccess
```

`FinanceFixedAssetService` owns authorization, validation, transactions, idempotency, depreciation rules and state transitions. The repository owns bounded queries and persistence. Provider-specific DDL remains in `FinanceFixedAssetsSchemaInitializer`.

## Persistence

Finance feature schema **10** adds:

- `FinanceAssetClasses`
- `FinanceFixedAssets`
- `FinanceAssetDepreciationPeriods`
- `FinanceAssetTransactions`

Asset masters use optimistic versions. Asset transactions are retained accounting evidence with immutable operation IDs and optional General Ledger journal references. Schedule rows retain their posting operation/journal evidence once posted.

The schema initializer has provider-specific DDL for bundled SQLite, SQL Server and the shared MySQL/MariaDB provider path.

## Asset classes

An asset class belongs to one Legal Entity and configures:

- fiscal calendar;
- default useful life;
- default depreciation method;
- capitalization posting profile;
- depreciation posting profile;
- impairment posting profile;
- disposal posting profile.

Posting profiles must be active, belong to the same Legal Entity and use the expected `FixedAssets` source/event contract. Their accounting books and the class fiscal calendar must belong to that same entity. Every accounting operation revalidates these relationships and requires a period in the class calendar.

A class referenced by any asset cannot be reassigned to another Legal Entity. Descriptive and valid same-entity configuration updates remain available. Existing asset Legal Entity identities are also retained; moving financial ownership requires a separate, explicit migration workflow.

## Asset master

A Fixed Asset records:

- stable asset number;
- Legal Entity and asset class;
- description;
- acquisition/capitalization/depreciation-start dates;
- transaction currency evidence;
- original cost and salvage value;
- useful life and depreciation method;
- current location and custodian;
- status;
- optional supplier-document-line source link.

After capitalization, original cost, currency and Legal Entity cannot be silently rewritten.

## Depreciation

The initial bounded methods are:

- **Straight Line**
- **No Depreciation**

Straight-line schedules are deterministic and use the configured fiscal periods. The final scheduled period absorbs decimal rounding remainder so total planned depreciation equals original cost less salvage value.

A schedule containing posted periods cannot be silently replaced.

### Closed periods

Depreciation posting has an explicit policy:

- `Fail` — closed period rejects the posting.
- `NextOpenPeriod` — the posting uses the next open fiscal period and retains that policy in evidence.

There is no implicit posting into a closed period.

### Period runs and idempotency

A period run selects bounded pending schedule rows for the requested accounting period. Each schedule row receives a deterministic child operation ID derived from the run operation ID and schedule identity. A partially completed run can therefore be retried without double-posting completed rows.

Each individual accounting mutation remains transactional. Depreciation journals use the immutable schedule-row identity as their source; impairment journals use the immutable adjustment operation identity. A retry returns the existing event evidence, including a previously posted depreciation period submitted under a new operation ID, and cannot append a second subledger transaction for the same source journal. Reusing an impairment operation ID for a different asset, amount, period, date or reason is rejected.

## Accounting workflows

### Capitalization

Only draft assets can be capitalized. The configured capitalization profile posts the asset cost through General Ledger and the asset becomes Active.

### Depreciation

Only active assets can post depreciation. A schedule row can be posted once; asset-row serialization, unique evidence and operation IDs protect retry behavior.

Each posting calculates the remaining depreciable carrying value from committed capitalization, depreciation, impairment and correction evidence in the same transaction. Salvage value is the depreciation floor. The remaining basis is distributed proportionally across the original positive weights of unposted schedule rows; the final remaining weight consumes the exact remainder. This adjusts future charges after impairment or its correction while retaining the original planned amounts and all posted charges. A depleted or zero-rounding charge is rejected without creating a zero-value journal. Correcting an impairment restores only the remaining basis; it does not reopen posted schedule rows.

### Impairment

Impairment requires an explicit positive amount and reason and posts through the configured impairment profile. The amount cannot exceed the current carrying value. The service locks the asset before reading posted valuation evidence, so concurrent impairments or depreciation cannot independently consume the same remaining value.

### Impairment correction

A posted impairment can be corrected only through an explicit General Ledger reversal. The original impairment transaction and journal remain immutable; the correction creates its own operation ID, reversal journal reference, reason and Fixed Assets transaction evidence. Corrected impairment is added back when carrying value is calculated. Other transaction kinds are not silently repurposed as generic corrections. Owning impairment corrections remain available to repair historical overdrawn carrying evidence; ordinary new charges and disposal reject inconsistent valuation evidence instead of clamping a negative value to zero.

### Transfer

Location/custodian transfers require a reason. They update current assignment data while retaining a separate transaction record; prior accounting entries are not rewritten.

### Disposal

Only active assets can be disposed. Disposal computes carrying value from retained capitalization, depreciation, impairment and correction evidence under the same asset lock and sends cost, accumulated depreciation, impairment, proceeds, carrying value and gain/loss amounts to the configured disposal posting profile.

A disposal retry checks the original asset, period, date, reason and retained proceeds evidence before returning the existing transaction. Numerically equal proceeds such as `900` and `900.00` are the same request; changed proceeds are rejected independently of later posting-profile changes.

## Accounts Payable source linkage

An asset can retain the originating `FinanceSupplierDocumentLine` identity. The Asset Register offers a bounded selection of lines from **posted supplier invoices** only. The Fixed Assets service revalidates the selected source at save time and requires its currency to match the asset currency; it does not own or rewrite AP posting, matching, approval, reversal or historical supplier-document evidence.

Classification, useful life, depreciation method and capitalization remain explicit Fixed Assets decisions rather than being inferred automatically from the supplier document.

## Reconciliation

The workspace exposes a bounded subledger-to-GL reconciliation projection per Legal Entity. It compares the Fixed Assets carrying-value projection with asset-account activity in General Ledger for the `FixedAssets` source.

Differences are reported; the reconciliation path does not create adjusting journals automatically.

## Workspace

**Finance > Fixed Assets** provides:

- Asset Register and detail editing;
- schedule and retained transaction history;
- capitalization/depreciation/impairment/transfer/disposal actions;
- period depreciation run;
- subledger-to-GL reconciliation;
- asset-class and posting-profile configuration.

The UI reuses Depot shared controls, loading/error/status states and central service authorization.

## Permissions

- `FinanceFixedAssets.View`
- `FinanceFixedAssets.Configure`
- `FinanceFixedAssets.Manage`
- `FinanceFixedAssetsDepreciation.Post`

The standard Finance role receives the complete Fixed Assets permission set. Accountant / Controller receives configuration/transaction/posting authority. Management Viewer and Auditor / Compliance receive view-only access.

Service-layer authorization remains authoritative; UI visibility is not a security boundary.

## Accounting, tax and compliance boundary

Depot does not certify or automatically determine:

- jurisdiction-specific tax depreciation;
- statutory useful lives;
- HGB, IFRS or US-GAAP asset policy;
- component accounting;
- revaluation models;
- pooled assets;
- IFRS 16 leases;
- tax books;
- organization-specific capitalization thresholds or accounting judgments.

The technical controls provide deterministic processing and evidence. Deployment-specific accounting/tax policy and qualified review remain required.

## Validation expectations

Changes to this area require, where applicable:

- warning-free Release build;
- fixed-asset unit/integration tests;
- Finance migration tests;
- SQLite integration;
- provider acceptance for persisted schema behavior;
- Finance GL/AP/reporting regression coverage;
- Help/documentation consistency;
- CI gate review.

See [Finance Architecture](FinanceArchitecture.md), [Finance Compliance](FinanceCompliance.md), [Finance Reporting](FinanceReporting.md), [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) and [Versioning](Versioning.md).

