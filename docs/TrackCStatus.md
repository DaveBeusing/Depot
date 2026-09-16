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

The gate uses pinned veraPDF `1.30.2` and verifies the downloaded official installer against the repository-pinned SHA-256 `6cc6341cb1af644044054b81f00a6590a7918abb18f762243de115258bcad838`. The CLI is installed unattended and its runtime version must match the pin before any document is accepted.

The production generator creates five hybrid artifacts bound to the same retained XRechnung fixtures used by the KoSIT matrix:

- Standard-rated Invoice (`S`);
- Zero-rated Invoice (`Z`);
- Exempt Invoice (`E`) with exemption evidence;
- Reverse-charge Invoice (`AE`) with exemption evidence;
- Standard-rated Credit Note (`381`).

For every case the generated CII must first equal the retained KoSIT-bound fixture. `ZugferdFacturXService` then creates the PDF/A-3B artifact from that exact XML. The external validator runs with explicit PDF/A-3B flavour and the workflow parses the machine-readable veraPDF report; missing reports, non-compliant validation results, parse/encryption/exception failures or validator execution errors fail closed.

The workflow retains the generated PDFs, a generator manifest, per-document veraPDF XML reports/logs and a validator summary as `electronic-invoice-conformance-evidence`. KoSIT validation remains a separate step in the same electronic-invoice conformance workflow, so PDF/A success cannot substitute for XRechnung XML success and vice versa.

F3 repository implementation is merged, but final external acceptance remains evidence-dependent until the required candidate workflows complete successfully. Merge presence does not manufacture KoSIT or veraPDF evidence.

## F4A enterprise identity foundation

PR #43 (`enterprise-identity-foundation`) is merged into `master`. Enterprise Identity feature schema `1` introduced non-secret provider configuration plus exact provider/issuer/subject links to existing local Depot users. The resolver returns only active local users and reloads roles/effective permissions exclusively from Depot RBAC.

F4A deliberately does not auto-provision users or trust external roles/groups/permission claims.

## F4B OpenID Connect / Microsoft Entra ID authentication

PR #44 (`enterprise-identity-oidc`) is merged into `master`. The bounded F4B scope is:

- Authorization Code + PKCE (`S256`) for the native Windows client;
- system-browser authorization;
- dynamically allocated `http://localhost:<port>/` loopback callback;
- cryptographically random and fail-closed `state` / `nonce` validation;
- HTTPS OpenID Connect discovery and signing-key retrieval;
- ID-token signature, issuer, audience, expiration/lifetime and advertised-algorithm validation;
- one controlled signing-key metadata refresh/retry for normal key rollover;
- tenant-bound Microsoft Entra ID sign-in only;
- no client secret, token persistence or external-password handling;
- no automatic local-user creation;
- no external group/role-to-Depot-permission mapping;
- normal Depot SessionService, concurrent-session policy, AuthorizationService and Security Event integration after F4A resolution succeeds.

PR #45 repaired F4B/F3 build integration against the stable PDFsharp 6.2.4 surface and the OIDC compile boundary. Its candidate evidence also exposed a stale Factur-X structural test fixture that is corrected together with the next candidate package so the independent F3 gate can run again.

## F4C external MFA claims and identity hardening

The `enterprise-identity-mfa` package advances Enterprise Identity feature schema `1` to `2` and adds an explicit provider-bound external authentication-assurance contract.

The bounded F4C scope is:

- optional exact required `amr` value per provider;
- optional exact required `acr` value per provider;
- optional maximum `auth_time` age from 1 through 1440 minutes;
- `acr_values` and `max_age` request hints when those requirements are configured;
- fail-closed verification of the validated ID-token assurance evidence before identity resolution or session creation;
- `azp` validation against the configured client ID when present and mandatory authorized-party evidence for multi-audience tokens;
- malformed, ambiguous, stale, future-dated or mismatching assurance evidence is rejected with controlled failure codes;
- provider assurance-policy changes reuse `UsersManage`, optimistic concurrency and transactional Audit evidence;
- `amr`, `acr`, `auth_time` and `azp` values remain runtime-only and are not stored on identity links;
- no Depot-managed TOTP/MFA secrets, no automatic user provisioning and no external role/group/permission mapping.

For Microsoft Entra ID deployments, Conditional Access and Authentication Strength remain the primary tenant-side method-policy controls. Depot does not infer universal MFA semantics from an arbitrary claim string; an `amr`/`acr` requirement is an explicit administrator-selected provider trust contract.

The schema-1→2 migration preserves existing provider/link identities and initializes all new assurance requirements to null, so an upgrade cannot silently add a new MFA requirement.

F4C repository implementation remains acceptance-evidence dependent until its CI, quality, security, packaged-E2E, electronic-invoice conformance, release and provider gates complete successfully.

## Track status and next work

F1 is merged. F2 repository implementation/conformance breadth are merged and remain final-evidence dependent. F3A and F3B are merged; final F3 acceptance remains external-evidence dependent. F4A, F4B and the F4B stabilization are merged. F4C is implemented on `enterprise-identity-mfa` and awaits candidate gates. F5 has not been started.

After F4C is merged with green repository evidence, the next implementation package is F5A Security Event Export Contract.
