# Track C acceptance and product decision

Reviewed: 2026-09-16

## F2 validation follow-up

PR #36 (`electronic-invoice-completion`) introduced the bounded F2 production path. PR #37 repaired provider/migration and packaged-E2E regressions. PR #38 (`packaged-e2e-acceptance-repair`) removed stale packaged-version assumptions and hardened executable corruption detection. PR #39 (`electronic-invoice-conformance-closure`) is merged into `master` and closes the repository implementation's external XML-coverage breadth.

The conformance closure binds retained fixtures to the production `ElectronicInvoiceService` and validates the bounded XRechnung 3.0 CII issuance matrix through KoSIT: Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`), Reverse-charge (`AE`) invoices and Standard-rated Credit Note (`381`). F2 remains acceptance-evidence dependent until the required candidate gates are green; merge status alone is not final acceptance.

## F3 product decision: approved

On 2026-09-16 the product decision was explicitly recorded to include ZUGFeRD/Factur-X in the Depot product promise.

The bounded initial product scope is:

- ZUGFeRD `2.5.2` / Factur-X `1.09.2`;
- German `XRECHNUNG` reference profile;
- PDF/A-3B hybrid documents generated as new documents, not conversion of an arbitrary existing PDF;
- exactly one embedded structured invoice payload named `xrechnung.xml`;
- the embedded XML is the same finalized XRechnung CII payload already produced and validated by Depot, never a second XML generator;
- Factur-X XMP metadata declares the embedded file/profile contract;
- exact PDF bytes, PDF SHA-256 and finalized XML SHA-256 are retained as immutable evidence;
- Invoice and Sales Credit Note use the same hybrid-artifact boundary;
- external PDF/A acceptance is independent from KoSIT XML acceptance.

## F3A hybrid artifact foundation

PR #41 (`zugferd-facturx`) is merged into `master`. Sales feature schema `14` adds the provider-neutral `SalesHybridElectronicInvoiceArtifacts` store. `ZugferdFacturXService` creates a PDF/A-3B document from the immutable `ElectronicInvoice` finalization model, embeds the exact finalized `xrechnung.xml` using `AFRelationship=Alternative`, writes the Factur-X/XRECHNUNG XMP contract, hashes the PDF and persists the exact artifact in the same transaction as invoice or credit-note finalization.

Exports use persisted bytes and verify SHA-256 evidence. Legacy records are not reconstructed from mutable customer/company master data. Repository tests verify embedded XML bytes, PDF/A/XMP markers, conformance constants, schema migration, persistence and tamper detection. This structural implementation alone does not prove external PDF/A conformance.

## F3B independent conformance closure

PR #42 (`zugferd-facturx-conformance-closure`) is merged into `master`. It adds the independent PDF/A acceptance gate without changing the persisted schema contract.

The gate uses pinned veraPDF `1.30.2` and verifies the downloaded official installer against the repository-pinned SHA-256 `6cc6341cb1af644044054b81f00a6590a7918abb18f762243de115258bcad838`. The production generator creates five hybrid artifacts bound to the same retained XRechnung fixtures used by the KoSIT matrix: Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`), Reverse-charge (`AE`) and Standard-rated Credit Note (`381`). KoSIT validation remains independent from PDF/A validation.

F3 repository implementation is merged, but final external acceptance remains evidence-dependent until the required candidate workflows complete successfully. Merge presence does not manufacture KoSIT or veraPDF evidence.

## F4A enterprise identity foundation

PR #43 (`enterprise-identity-foundation`) is merged into `master`. Enterprise Identity feature schema `1` introduced non-secret provider configuration plus exact provider/issuer/subject links to existing local Depot users. The resolver returns only active local users and reloads roles/effective permissions exclusively from Depot RBAC. F4A deliberately does not auto-provision users or trust external roles/groups/permission claims.

## F4B OpenID Connect / Microsoft Entra ID authentication

PR #44 (`enterprise-identity-oidc`) is merged into `master`. F4B implements Authorization Code + PKCE (`S256`), system-browser authorization, loopback callbacks, fail-closed `state` / `nonce`, HTTPS OIDC discovery/signing-key validation, tenant-bound Microsoft Entra ID sign-in and the existing local Session/RBAC boundary. It persists no client secret or protocol token and never maps external roles/groups to Depot permissions.

PR #45 repaired F4B/F3 build integration against the stable PDFsharp 6.2.4 surface and the OIDC compile boundary.

## F4C external MFA claims and identity hardening

PR #46 (`enterprise-identity-mfa`) is merged into `master`. Enterprise Identity feature schema `2` adds an explicit provider-bound external authentication-assurance contract:

- optional exact required `amr` value per provider;
- optional exact required `acr` value per provider;
- optional maximum `auth_time` age from 1 through 1440 minutes;
- `acr_values` and `max_age` request hints;
- fail-closed assurance validation before local identity resolution/session creation;
- `azp` validation against the configured client ID and mandatory authorized-party evidence for multi-audience tokens;
- `UsersManage`, optimistic concurrency and transactional Audit for assurance-policy changes;
- no persisted `amr`, `acr`, `auth_time`, `azp`, protocol tokens or Depot-managed MFA secrets.

For Microsoft Entra ID deployments, Conditional Access and Authentication Strength remain the primary tenant-side method-policy controls. Depot does not infer universal MFA semantics from arbitrary claim strings.

The first F4C candidate exposed three independent acceptance regressions after merge: invalid XRechnung exemption placement/order in the production CII generator, a stale User Preferences migration test setup and a transient packaged-E2E executable sharing violation. PR #47 (`f4c-acceptance-stabilization`) contains the deterministic XRechnung and migration-test repairs at `0.15.204-preview`; its exact-head acceptance evidence remains authoritative before the stabilization is treated as closed.

## F5A security event export foundation

The `security-event-export-foundation` package defines the source-side Security Event export contract without changing Security Events schema `2`.

The bounded F5A scope is:

- immutable export-record projection from existing `SecurityEvents` source evidence;
- exclusion of mutable Security Center review metadata and row version from exported event meaning;
- minimum-severity plus optional event-type filters;
- canonical SHA-256 filter fingerprint;
- filter-bound checkpoint based on monotonic Security Event ID;
- snapshot upper-bound capture before each bounded read;
- deterministic ascending batches with a maximum of 500 events;
- fail-closed checkpoint/filter mismatch;
- provider-neutral source repository and sink abstraction;
- SQLite, SQL Server, MariaDB and MySQL provider-smoke coverage;
- no durable delivery configuration, retry state or checkpoint persistence in F5A.

F5A deliberately keeps reliable delivery separate from source extraction. `ISecurityEventExportSink` is an adapter boundary only. F5B will own sink delivery orchestration, retry classification and durable checkpoint advancement after successful delivery. The source `SecurityEvents` rows are never mutated to represent export state.

See [Security Event Export Foundation](SecurityEventExport.md).

## Track status and next work

F1 is merged. F2 repository implementation/conformance breadth are merged and remain final-evidence dependent. F3A/F3B are merged and remain external-evidence dependent. F4A/F4B/F4C are merged; F4C acceptance stabilization is tracked in PR #47. F5A is implemented on `security-event-export-foundation` and does not change Security Events schema `2`.

After F5A candidate gates and merge, the next implementation package is F5B Security Event Delivery & Checkpointing.
