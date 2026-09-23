# Finance Fixed Assets

Updated: 2026-09-23

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

Posting profiles must be active, belong to the same Legal Entity and use the expected `FixedAssets` source/event contract.

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

Each individual accounting mutation remains transactional.

## Accounting workflows

### Capitalization

Only draft assets can be capitalized. The configured capitalization profile posts the asset cost through General Ledger and the asset becomes Active.

### Depreciation

Only active assets can post depreciation. A schedule row can be posted once; optimistic/unique evidence and operation IDs protect retry behavior.

### Impairment

Impairment requires an explicit positive amount and reason and posts through the configured impairment profile.

### Transfer

Location/custodian transfers require a reason. They update current assignment data while retaining a separate transaction record; prior accounting entries are not rewritten.

### Disposal

Only active assets can be disposed. Disposal computes carrying value from retained cost, depreciation and impairment evidence and sends cost, accumulated depreciation, impairment, proceeds, carrying value and gain/loss amounts to the configured disposal posting profile.

## Accounts Payable source linkage

An asset can retain the originating `FinanceSupplierDocumentLine` identity. The Fixed Assets service validates that the linked line exists but does not own or rewrite AP posting, matching or historical supplier-document evidence.

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
