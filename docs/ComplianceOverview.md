# Compliance overview

Updated: 2026-09-20

Depot separates implemented technical controls from legal, accounting, tax, audit or certification claims. Security/compliance roadmap phases retain their technically implementable controls; remaining acceptance gates are tracked in the security, release, Finance and compliance documentation.

The canonical release-facing supported/unsupported claim boundary is [Release Compliance Boundary](ReleaseComplianceBoundary.md). Customer-facing release language must stay within that documented boundary.

## Finance technical baseline

Depot Finance provides explicit legal entities, currencies/exchange rates, periods, accounting books/charts/accounts, immutable balanced double-entry posting, customer and supplier subledgers, FIFO inventory valuation, Banking and Payments, configurable Financial Reporting and an effective-dated Localization framework.

These capabilities improve traceability, repeatability, reconciliation, correction history, authorization and retry safety. They do not by themselves establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, statutory retention, payment-services, audit or tax-filing conformity.

GL-derived financial reports use persisted reporting-currency values and preserve posting-time FX evidence. AR/AP Aging remains in open-item transaction currency. Cash Flow and Tax Summary require explicit account mappings. Historical Inventory Valuation uses retained valuation evidence. `FinanceReportSnapshot` is immutable `AuditEvidence` containing report parameters, canonical CSV, hashes, creator and creation time.

Localization requires explicit effective-dated assignment. The built-in `GENERIC → EU → DE` hierarchy and support-level registry distinguish software capability, deployment configuration, external procedures and reference-only information; assignment is not a compliance certification.

## Current versions

- Application: **0.15.x-preview**
- Core database schema: **30**
- Sales feature schema: **14**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **3**
- User Preferences feature schema: **2**
- Document Templates feature schema: **1**
- Enterprise Identity feature schema: **2**
- Help manifest: **1.24**

`Directory.Build.props` is authoritative for the exact application patch/version. Compliance documentation records the development line and stable schema/help contracts instead of duplicating the moving preview patch number.

Sales schema 14 adds retained ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile hybrid artifacts. The bounded advertised matrix now has repository-level KoSIT/XRechnung and pinned veraPDF PDF/A-3B acceptance. This remains an engineering/conformance evidence statement, not jurisdiction-wide tax/legal certification or support for unadvertised profiles/scenarios; release and deployment acceptance remain separate.

Security Events schema 3 adds persisted export targets and durable at-least-once delivery state while keeping source Security Events immutable. This is an engineering delivery capability; downstream SIEM retention, receiver deduplication, endpoint governance and operational monitoring remain deployment responsibilities.

Enterprise Identity schema 2 adds non-secret provider configuration, exact external-identity links and optional provider-bound `amr`/`acr`/authentication-age requirements. OIDC/Entra protocol and assurance validation are technical authentication controls, not an identity-provider certification. External groups, roles and permission claims do not grant Depot authorization; local Depot RBAC remains authoritative.

## Database-provider acceptance

The database-provider technical gate is complete for the exact baselines documented in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md): Depot's bundled SQLite runtime, SQL Server 2022 engine 16.x, MariaDB 11.8.9 LTS and MySQL 8.4.11 LTS.

The real-provider matrix covers provisioning/migration, locking/deadlock/retry, rollback, constraints, date/decimal/timestamp handling, representative Finance/Sales/Procurement/session workflows, Banking/reconciliation, Financial Reporting/snapshots, remote restart and provider-native backup/restore boundaries plus representative 100k indexed access. Enterprise Identity schema 2 extends the provider migration/persistence boundary with assurance-policy fields while preserving existing schema-1 provider/link identity data.

Database-provider acceptance is an engineering/runtime statement. It does not establish jurisdiction-specific accounting, tax, legal, accessibility, operating-system, banking-network or regulatory certification.

## External authentication boundary

Depot's external authentication implementation uses OIDC Authorization Code + PKCE and validates signature, issuer, audience, lifetime, nonce, tenant boundary and configured external assurance evidence before local identity resolution. Required `amr`/`acr` values are explicit administrator-selected provider contracts; Depot does not assign universal MFA semantics to arbitrary claim strings.

For Microsoft Entra deployments, Conditional Access and Authentication Strength remain deployment-side controls operated in the tenant. Depot's application-side claim checks do not replace those controls, certify their configuration or prove an organization's MFA policy is adequate.

Depot does not persist protocol tokens or raw `amr`, `acr`, `auth_time` or `azp` evidence and does not implement its own TOTP/MFA secret store in this package.

## Remaining acceptance

Production use still requires deployment-specific accounting/reporting policy approval, reconciliation and period-end procedures, segregation-of-duties review, retention/export/restore procedures, realistic customer-specific sizing, accessibility/signing/deployment acceptance and qualified organizational/legal/accounting validation.

Enterprise external authentication additionally requires deployment-specific identity-provider configuration, redirect/application registration and Conditional Access / Authentication Strength or equivalent provider-policy acceptance before it can be advertised as an accepted customer SSO/MFA deployment.

This document is engineering evidence and not a certification statement.

## Business attachments

User-supplied Business Attachments are separate from immutable generated/electronic-invoice evidence. Attachment mutations are audited, revisions are retained, and attachments associated with personal-data Customer/Supplier records are represented by the data-subject export boundary. File retention, legal hold, erasure/restriction procedures and external malware scanning remain deployment-policy responsibilities. See [Business Attachments](BusinessAttachments.md).
