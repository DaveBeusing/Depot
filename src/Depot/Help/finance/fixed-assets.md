# Fixed Assets and Depreciation

Use **Finance > Fixed Assets** to maintain the fixed-asset subledger while the General Ledger remains the authoritative accounting boundary.

## Asset register

The Asset Register stores a stable asset number, legal entity, class, acquisition and capitalization dates, functional transaction currency evidence, cost, salvage value, useful life, depreciation method, location/custodian information, status, and optional supplier-document-line linkage.

Asset class configuration controls the fiscal calendar and the posting profiles used for capitalization, depreciation, impairment, and disposal.

## Supported depreciation methods

The initial supported methods are:

- **Straight Line** — deterministic depreciation over the configured useful-life periods, with the final period absorbing rounding remainder.
- **No Depreciation** — the asset remains registered without an automatic depreciation schedule.

No other depreciation method is implied by the current implementation.

## Capitalization

Capitalization is available only for draft assets. The service validates the configured asset class and posting profile, then posts through the existing Finance General Ledger service. Fixed Assets never writes parallel ledger rows.

## Depreciation schedule and period run

Recalculate the schedule before posting when the draft asset configuration changes. Once a schedule contains posted periods it cannot be silently replaced.

A period depreciation run processes pending schedule rows using deterministic child operation identifiers. Re-running the same operation does not double-post completed depreciation.

For a closed accounting period, choose an explicit policy:

- **Fail** — no posting is created.
- **Next Open Period** — the posting is moved to the next open period and the retained transaction records that policy.

## Adjustments, transfers and disposal

- **Impairment** posts an explicit write-down with a required reason.
- **Correct impairment** reverses a selected posted impairment through General Ledger and records a separate correction transaction; the original evidence stays unchanged.
- **Transfer** changes current location/custodian data without rewriting historical transactions.
- **Disposal** closes the active asset through the configured disposal profile while retaining acquisition, depreciation, impairment and journal evidence.

## Reconciliation

The Reconciliation tab compares carrying value from the fixed-asset subledger with asset-account activity in General Ledger for the same Fixed Assets source. Differences remain visible for investigation rather than being silently adjusted.

## Permissions

Fixed Assets separates:

- view access;
- configuration authority;
- asset transaction authority;
- depreciation posting authority.

UI availability follows these permissions, but service-layer authorization remains authoritative.

## Accounting and tax boundary

Depot provides technical asset-accounting workflows and evidence. It does **not** certify jurisdiction-specific tax depreciation, HGB/IFRS/US-GAAP policy, statutory useful lives, revaluation, IFRS 16 lease accounting, component accounting, pooled assets, or tax-book compliance. Those decisions require deployment-specific accounting and tax review.

Related topics: [Finance Foundation](topic:finance.foundation), [General Ledger and Posting](topic:finance.general-ledger), [Accounts Payable](topic:finance.payables), [Financial Reporting](topic:finance.reporting), and [Audit Log](topic:administration.audit-log).
