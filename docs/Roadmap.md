# Depot Roadmap

Updated: 2026-09-20

This roadmap describes product capabilities and acceptance work without coupling the repository to historical implementation tranche names. Track C repository work is complete through F5B, and the Depot 1.0 technical gap reconciliation is complete. Further repository work should be evidence-driven rather than another assumed feature tranche.

## Implemented product capabilities

### Platform and operations

- [x] WPF/MVVM shell, navigation and contextual offline Help
- [x] inventory, warehouse, purchasing, sales and approval workflows
- [x] deterministic inventory replenishment with per-warehouse policies, explainable MOQ-aware purchase suggestions and controlled Purchase Requisition conversion
- [x] bounded configurable approval policies with deterministic routing, immutable approval snapshots and an Administration designer for the four existing approval domains
- [x] serial/lot traceability and reversal-safe stock evidence
- [x] database-backed RBAC and service-layer authorization
- [x] provider-neutral persistence for SQLite, SQL Server, MariaDB and MySQL
- [x] real production database-provider acceptance for the exact certified baselines
- [x] structured Audit evidence and correction-oriented retained business records
- [x] reusable versioned business attachments across the initial Customer, Supplier, Item, Sales, Purchasing and AP record set
- [x] company/document identity controls and persisted XRechnung evidence
- [x] one authoritative Source-to-Release path with Preview/Stable channels and retained release evidence
- [x] technical DR, accessibility and Track A production-closure evidence contracts
- [x] packaged DepotManager lifecycle acceptance including real Windows registration/shortcut repair and cleanup
- [x] persisted user workspace productivity with Saved Views, supported grid presentation state, Favorites, Recents and default landing
- [x] permission-filtered Quick Open, Command Palette and Global Search
- [x] bounded productive visual designers for document layout, posting flow, pricing strategy, reporting mapping, bank reconciliation, localization hierarchy, role permissions and import mapping
- [x] source-controlled `master` governance contract plus active ruleset `23590604` PR/deletion/non-fast-forward protection; the missing live required-status-check bindings are tracked separately as H1 `BLOCKED`

### ERP-native Sales CRM

- [x] Lead capture with ownership, qualification state, contact channels and optimistic concurrency
- [x] controlled Lead → Customer/Opportunity conversion without a second customer master
- [x] stage-based Opportunities with expected amount, user-maintained probability and Won/Lost evidence
- [x] Sales Activities with due/completion/cancellation evidence
- [x] Opportunity → Sales Quote conversion through the existing quote authority
- [x] bounded pipeline aggregation, Global Search/deep links, My Work and Sales role-center integration
- [x] service-layer CRM RBAC and provider-neutral Sales schema 15 persistence

### Costing and sales pricing

- [x] scoped Global, Region and Customer PriceLists with Customer → Region → Global item resolution
- [x] explicit Base Cost sources: Preferred Supplier, Last Purchase, Manual Standard and Inventory Cost Reference
- [x] explicit Item Cost currency and versioned calculation evidence
- [x] Absolute and Percentage Cost Components
- [x] Percentage bases `BaseCost` and `RunningTotal`
- [x] deterministic Sequence + persisted identity calculation order
- [x] effective-dated and active/inactive Cost Components
- [x] central `ItemCostCalculationService` with calculation evidence
- [x] Percentage Markup as an explicit pricing method
- [x] Target Gross Margin as a distinct formula with `< 100%` validation
- [x] controlled direct Cost Currency → PriceList Currency FX with effective date, source and version evidence
- [x] fail-closed behavior for missing FX data without inversion, triangulation or implicit 1:1 fallback
- [x] deterministic commercial rounding: currency precision, 0.01, 0.05, 0.10, 0.50 and `.99` ending
- [x] All Active, Category, Manufacturer and Selected Item bulk filters
- [x] mandatory Preview with Base Cost, FX, converted cost, formula and rounding intermediates
- [x] Replace, Only Increase and Only Missing Apply modes
- [x] atomic Bulk Apply with optimistic concurrency across PriceList, cost and FX evidence, Audit and service-layer RBAC
- [x] historical Sales-document price snapshots remain immutable

### Finance

- [x] legal entities and functional currencies
- [x] exchange rates
- [x] fiscal calendars and accounting periods
- [x] charts of accounts and accounts
- [x] accounting books and journals
- [x] accounting dimensions
- [x] tax registrations
- [x] Finance number sequences
- [x] immutable balanced journals and reporting-currency/FX evidence
- [x] posting profiles, period/account/dimension validation, idempotency and reversals
- [x] Accounts Receivable, payments/allocation/write-off, aging/statements and dunning
- [x] Accounts Payable, three-way matching, exception approval, settlement/reversal and aging/statements
- [x] FIFO inventory valuation, GRNI/COGS, adjustments, variance, landed cost and Inventory ↔ GL reconciliation
- [x] Banking with immutable statements, payment proposals/execution, reconciliation and cash position
- [x] Financial Reporting with Trial Balance, GL, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS
- [x] deterministic CSV export and immutable SHA-256-bound report snapshots
- [x] provider-neutral Finance Localization with explicit effective-dated assignments and `GENERIC → EU → DE` reference hierarchy
- [x] Finance workspaces and contextual Help


- [x] Fixed Assets and depreciation foundation: asset register, configurable classes/posting profiles, deterministic straight-line/no-depreciation schedules, GL-authoritative capitalization/depreciation/impairment/disposal, retained transaction evidence, period runs and reconciliation.
- [ ] Complete deployment-specific accounting/tax policy acceptance for Fixed Assets; jurisdiction-specific tax depreciation remains outside the generic product boundary.

### Electronic invoicing

- [x] bounded XRechnung 3.0 CII invoice finalization with persisted immutable XML/integrity evidence
- [x] Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`) and Reverse-charge (`AE`) invoice semantics for the advertised matrix
- [x] Standard-rated electronic Sales Credit Note (`381`) finalization
- [x] independent KoSIT repository conformance for every currently advertised XML scenario
- [x] ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile PDF/A-3B hybrid generation
- [x] exact finalized `xrechnung.xml` embedded in the retained hybrid artifact
- [x] immutable PDF/XML SHA-256 evidence
- [x] independent pinned veraPDF acceptance for the advertised hybrid matrix

Unsupported special-tax/channel scenarios, other Factur-X profiles and arbitrary existing-PDF conversion remain outside the current product promise until separately implemented and accepted.

### Enterprise identity and authentication

- [x] provider-neutral external identity configuration and exact provider/issuer/subject links
- [x] Authorization Code + PKCE with system-browser sign-in and loopback callback handling
- [x] OIDC discovery/signing-key and issuer/audience/lifetime/nonce validation
- [x] tenant-bound Microsoft Entra ID support
- [x] provider-bound optional `amr`, `acr` and authentication-age requirements
- [x] `azp` validation and multi-audience fail-closed behavior
- [x] local Depot RBAC remains authoritative; external roles/groups/permission claims are ignored for authorization
- [x] protocol tokens and runtime assurance claims are not persisted on identity links

### Security and sessions

- [x] persistent user sessions and heartbeat-derived online presence
- [x] idle/maximum lifetime and concurrent-session policy
- [x] credential/deactivation session invalidation
- [x] shared database authentication throttling
- [x] persisted Security Events, Security Center and bounded retention maintenance
- [x] immutable Security Event export projection and deterministic bounded snapshots
- [x] provider-neutral filter-bound export checkpoint semantics
- [x] persisted export targets separate from source events
- [x] durable fixed-snapshot at-least-once delivery state
- [x] retry/backoff, suspension and short worker lease persistence
- [x] HTTPS `http-json-v1` sink with deterministic delivery ID and no persisted endpoint secrets/tokens/response bodies
- [x] retention protection for events not yet consumed by enabled targets

## Database provider production acceptance

The generic database-provider technical gate is complete for the exact baselines in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

- [x] bundled SQLite full acceptance baseline
- [x] SQL Server 2022 / engine 16.x migration/concurrency/recovery/performance matrix
- [x] MariaDB 11.8.9 LTS migration/concurrency/recovery/performance matrix
- [x] MySQL 8.4.11 LTS migration/concurrency/recovery/performance matrix
- [x] MariaDB/MySQL independent execution and support decisions
- [x] provider-native migration serialization
- [x] transaction rollback, lock/deadlock/write-conflict and bounded retry validation
- [x] SQL/type/constraint/date/decimal/timestamp compatibility validation
- [x] Sales and Procurement live business workflows
- [x] Finance GL/AR/AP/FIFO live business workflows
- [x] Banking import/reconciliation/reversal live business workflow
- [x] Financial Reporting/export/snapshot live business workflow
- [x] server restart and post-restart Depot re-entry
- [x] provider-native remote backup/restore and post-restore recognition boundary
- [x] representative 100,000-row indexed provider performance guard

This does not certify versions outside the support matrix and does not close deployment-specific accounting/legal/accessibility/signing gates.

## Repository acceptance state

Track C F1 through F5B are complete at the repository implementation/acceptance boundary. The final closure after F5B has green CI, Software Quality, Security Supply Chain, Database Provider Acceptance and DepotManager packaged-E2E evidence on the accepted repair head.

Track A repository implementation is complete, but H1 has been reopened by live governance drift while H2 remains closed:

- H1 Repository Governance: `BLOCKED` until active ruleset `23590604` again requires all five aggregate gates and passes live validation;
- H2 Release Pipeline & Channels: repository-level `PASS`;
- H3 Production Signing: `PRODUCTION_RC_REQUIRED`;
- H4 Production Operations & DR: `DEPLOYMENT_REQUIRED`;
- H5 Accessibility & Desktop Acceptance: `MANUAL_REQUIRED`.

The 1.0 technical gap reconciliation found no currently known generic implementation defect and no currently known missing automated-test/evidence defect inside the advertised repository boundary. See [Depot 1.0 Release Candidate Readiness](ReleaseCandidateReadiness.md).

## Production acceptance still required before 1.0

- [ ] accounting-book/chart/calendar/posting-profile/valuation/reporting policy approval, reusing the bounded Approval Policies foundation where domain semantics require configurable approval routing
- [ ] AR/AP/inventory/bank reconciliation and period-end procedures
- [ ] segregation-of-duties review for posting, approval, payment and configuration roles
- [ ] jurisdiction-specific accounting/tax/localization acceptance
- [ ] retention/export/backup/restore operating procedures and ownership
- [ ] realistic customer-specific sizing including network latency, concurrent users and large reports/exports
- [ ] keyboard-only, screen-reader and DPI accessibility acceptance
- [ ] H1 live `master` governance restored so ruleset `23590604` requires all five aggregate gates and fresh validation evidence is retained
- [ ] production Authenticode signing and exact-RC publisher/timestamp acceptance
- [x] generic repository lifecycle acceptance for install/update/repair, rollback safety, manager self-update, uninstall scopes and Windows shell integration
- [ ] exact production-signed Stable RC lifecycle acceptance with the final release artifacts
- [ ] ACTIVE deployment DR evidence with real restore drill inside accepted RPO/RTO
- [ ] remaining electronic-invoice special-tax/channel scenarios before they are marketed
- [ ] qualified GDPR/CRA/legal/organizational review required for marketed deployment scenarios

## Engineering focus after the 1.0 reconciliation

The read-only-first reconciliation is complete. Remaining work is no longer an undifferentiated repository backlog.

Open items are classified as:

1. repository implementation defect — none currently known;
2. missing automated test/evidence defect — none currently known for the advertised generic boundary;
3. documentation/baseline defect — the 2026-09-20 reconciliation corrects stale H1, UI/productivity and Help statements against current `master`;
4. repository administration requirement — H1 is reopened / `BLOCKED` until the live ruleset again binds all five aggregate required checks and passes validation;
5. production-RC/deployment/manual/legal acceptance — H3, H4, H5, deployment accounting/operations, sizing and qualified review;
6. demand-driven future extension — capabilities outside the current 1.0 product promise.

The immediate Track A action is to restore and revalidate H1 live governance. After H1 returns to `PASS`, execute H3 production-signed Stable acceptance-only workflow and retain its evidence. Do not create another generic feature or hardening tranche solely because Depot has not reached 1.0. New repository work should be triggered by concrete failing evidence, a reproducible RC defect or an explicit product-scope change.

## Demand-driven extensions

Future pricing extensions should continue to use the current cost, FX and bulk-pricing boundaries. Plausible demand-driven additions include controlled automated rate ingestion with source governance, further explicit costing references backed by qualified accounting evidence, customer-contract pricing mechanics and additional market-specific commercial rounding policies.

The existing Finance architecture can host additional regional/country localization packs without a schema change when requirements are metadata/configuration only. Jurisdictions that require new executable workflows, statutory filing formats, additional costing methods, direct bank connectivity or other missing behavior require separately scoped implementation and qualified acceptance.

Provider versions outside the certified matrix are likewise demand-driven certification extensions and must not be inferred from a green baseline for a different version/product.

## Finance budgeting and variance analysis

Controlled Finance Budgeting is implemented with schema 11 persistence, policy approval, immutable version history, import/export, period spreading, My Work integration and Actual-vs-Budget reporting. Remaining acceptance is the normal PR build/test/provider/quality gate; automated forecasting and broader planning engines remain outside this scope.


## Integrated Finance payment-initiation artifact

The bounded SEPA SCT payment-export capability is implemented at the repository boundary: `pain.001.001.09`, structured payment addresses, immutable artifact/hash evidence, role-separated generation/download/status operations and provider-backed persistence. Future banking connectivity such as EBICS, PSD2/Open Banking, SCT Inst and automated status ingestion remains separate roadmap scope and must not be inferred from the file-export capability.


## Procurement sourcing foundation

- [x] Purchase Requisition lifecycle with requester/creator evidence and optimistic concurrency
- [x] configurable Approval Policy integration with explicit authorization boundaries
- [x] RFQ recipients, requested lines and response-due tracking
- [x] Supplier Quote Response capture with currency, price, MOQ, lead time, validity and supplier reference
- [x] deterministic side-by-side comparison without automatic supplier award
- [x] explicit quote selection and idempotent conversion through the existing Purchase Order authority
- [x] immutable Purchase Order sourcing evidence and sourcing Business Attachments
- [x] My Work, Buyer Workbench, shell navigation and contextual Help integration
- [x] SQLite and remote-provider sourcing persistence/round-trip acceptance coverage

Inventory replenishment suggestions now consume authoritative stock, reservation, backorder and open-supply evidence and convert user-approved suggestions into the established Purchase Requisition sourcing boundary. Automatic supplier award and automatic Purchase Order placement remain outside the product scope.


## Project and cost accounting foundation

- [x] independent project lifecycle and shallow phase model
- [x] explicit source-document/manual-journal attribution with immutable posted-evidence protection
- [x] GL-backed project actuals and separate open Purchase Order commitments
- [x] Finance Budgeting line links and Actual-vs-Budget projection
- [x] permissions, Audit, optimistic concurrency and Business Attachments
- [x] Projects workspace and owned-project My Work integration
- [x] provider-neutral feature migration and real-provider provisioning acceptance

Complex construction accounting, percentage-of-completion revenue recognition, payroll/timekeeping, resource scheduling, full PSA and a separate project billing engine remain outside this foundation.
