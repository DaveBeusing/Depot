# Release Compliance Boundary

Updated: 2026-09-20

## Purpose

This document is the release-facing claim boundary for Depot. It consolidates what the current repository evidence supports, what remains deployment/organizational work, and what must not be described as certified, compliant, approved or supported beyond the implemented scope.

It is engineering/product documentation, not legal, accounting or tax advice. External qualified reviews remain external evidence and must not be marked complete unless their result is retained for the exact release/deployment.

## Claim rules

Release notes, product pages, support material and customer-facing documentation must follow these rules:

- state implemented capabilities precisely and identify bounded matrices where one exists;
- distinguish technical acceptance from legal/regulatory conformity;
- never infer jurisdiction compliance from a Finance localization assignment or support level;
- never infer production support from compilation, unit tests or a compatible provider family;
- never broaden an externally validated electronic-invoice fixture matrix to untested tax/profile/channel scenarios;
- keep deployment/operator responsibilities explicit for retention, backup, restore, identity-provider policy and external integrations.

## Release claim matrix

| Area | Repository-supported statement | Statement that is not supported by current evidence | External/deployment acceptance still required |
| --- | --- | --- | --- |
| GDPR / DSGVO | Depot has data-minimization, RBAC, privacy discovery/export, retention-boundary and audit controls documented in the technical baseline. | “Depot is GDPR/DSGVO compliant/certified.” | Controller/processor roles, lawful basis, notices, retention, DSAR procedure, recipients/processors, backup/erasure procedure and qualified legal/organizational assessment. |
| CRA | Depot has a technical CRA documentation set, security risk/SDLC controls, SBOM/dependency audit, vulnerability intake/handling and incident-reporting runbook. Article 14 reporting obligations are treated as operational from 11 September 2026. | “Depot is CRA compliant/certified” or that final product classification/conformity assessment is complete. | Final scope/economic-operator determination, classification, conformity route, release-specific support period, user/manufacturer information and organizational reporting authority/process. |
| Accounting / GoBD / statutory reporting | Depot provides immutable/correction-oriented records, GL/subledgers, audit evidence, configurable reporting and reconciliation controls. | HGB, GoBD, IFRS, US-GAAP, tax-filing or statutory-report certification. | Organization-specific accounting policy, period-end/reconciliation procedures, retention/export process, report mappings and qualified accounting/tax/legal acceptance. |
| Finance Localization | Depot provides effective-dated packs/assignments and the `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly` registry levels. | A localization assignment or built-in `DE` pack means the legal entity is compliant with German law or tax rules. | Qualified review of every enabled country pack, configuration item and external procedure. |
| Electronic invoicing | Bounded XRechnung 3.0 UN/CEFACT CII issuance plus ZUGFeRD 2.5.2 / Factur-X 1.09.2 `XRECHNUNG`-profile hybrid generation with retained KoSIT/veraPDF acceptance evidence. | Jurisdiction-wide e-invoicing certification, arbitrary EN 16931 tax semantics, arbitrary ZUGFeRD/Factur-X profiles or external delivery-network certification. | Recipient/channel acceptance and any additional tax/profile/channel scenario before marketing it. |
| Banking | Bank accounts, statement import, payment proposals/execution, reconciliation and cash position inside Depot. | Direct bank connectivity, EBICS, PSD2/open-banking APIs, external payment initiation, AML/KYC/sanctions decisioning or bank-specific `camt.053` certification. | Separate integration scope and organization/bank-specific procedure acceptance. |
| Database providers | Production database acceptance exists only for the exact matrix below and for the Depot release carrying green full provider evidence. | Support for a newer/older server version because the same provider family is compatible. | Separate full provider acceptance for every additional marketed provider/version. |
| Retention / backup / DR | Depot has technical retention boundaries, provider recovery tests, DR profile contract and restore runbooks. | That repository CI proves a customer backup policy, RPO/RTO or legal retention period. | Operator-owned schedule, retention, encryption, off-host copy, monitoring, accepted RPO/RTO and real isolated restore drill. |
| Vulnerability handling / support | Public vulnerability intake, triage/remediation policy, dependency audit and release-security controls exist. | That Preview is production-supported or that a Stable support commitment exists without a published release-specific support statement. | Release support dates, supported Windows/provider matrix, upgrade path, security-update channel, named owners and applicable regulatory reporting operations. |

## Electronic-invoice advertised matrix

The currently advertised XML issuance matrix is intentionally bounded:

| Document | Tax category | Advertised |
| --- | --- | --- |
| Invoice | Standard rated (`S`) | Yes |
| Invoice | Zero rated (`Z`) | Yes |
| Invoice | Exempt (`E`) with exemption evidence | Yes |
| Invoice | Reverse charge (`AE`) with exemption evidence | Yes |
| Credit Note (`381`) | Standard rated (`S`) | Yes |

Other VAT category/document combinations are outside the advertised production matrix unless separately implemented and accepted. Unsupported category codes fail closed. Recipient/routing metadata is retained as evidence; Depot does not currently perform external electronic-invoice transport.

The hybrid-document boundary is ZUGFeRD 2.5.2 / Factur-X 1.09.2 using the `XRECHNUNG` reference profile and the same finalized `xrechnung.xml`. Other hybrid profiles are not implied.

## Database supported baselines

Current technical production-support evidence is limited to:

- bundled SQLite runtime through `Microsoft.Data.Sqlite`;
- SQL Server 2022 / engine 16.x, with CI acceptance on SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

Versions outside these baselines are not implicitly production-supported. The detailed evidence and provider-specific limitations remain authoritative in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

## Localization boundary

The built-in `GENERIC → EU → DE` hierarchy is a reference/configuration framework. `DE` demonstrates country validation and inherited requirement metadata; it is not a certified German tax/accounting pack.

Support levels mean only:

- `SoftwareCapability`: relevant software behavior exists;
- `ConfigurationRequired`: deployment-specific configuration is still required;
- `ExternalProcedureRequired`: an organizational/professional/legal/tax/filing/retention/signing procedure remains external;
- `ReferenceOnly`: informational reference only.

No level is a compliance pass/fail status.

## CRA status at this baseline

The repository's working engineering assumption remains that a commercially supplied Depot release in the EU is a product with digital elements in general CRA scope, subject to final qualified review.

Regulation (EU) 2024/2847 Article 14 reporting obligations apply from 11 September 2026; the wider Regulation applies from 11 December 2027. The repository contains an incident-reporting runbook for the current reporting boundary. That does not complete final product classification, conformity assessment, declaration/marking obligations, manufacturer/economic-operator documentation or release-specific support-period determination.

The support-period policy uses the CRA engineering baseline of at least five years unless the expected product use is genuinely shorter, while allowing a longer period where expected use requires it. The final marketed support period is a release-specific organizational commitment and must be published separately.

## External review/evidence status

No qualified legal, accounting or tax opinion is represented by this repository documentation as a completed external review for Stable 1.0. No external review result may be promoted to `PASS` from internal engineering evidence alone.

Before Stable publication, the exact release package must make the remaining external/manual gates visible in [Version 1.0 Release Checklist](Release1.0.md) and publish [Known Limitations](KnownLimitations.md).

## Related authoritative documents

- [Compliance Overview](ComplianceOverview.md)
- [Finance Compliance](FinanceCompliance.md)
- [Finance Localization](FinanceLocalization.md)
- [Electronic Invoicing Technical Baseline](compliance/ElectronicInvoicing.md)
- [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md)
- [Data Protection Baseline](compliance/DataProtection.md)
- [Vulnerability Management](compliance/VulnerabilityManagement.md)
- [Support Policy](compliance/SupportPolicy.md)
- [CRA Product Classification](compliance/CraClassification.md)
- [CRA Vulnerability and Incident Reporting Runbook](compliance/CraIncidentReporting.md)
- [Production Operations & Disaster Recovery](ProductionOperationsDisasterRecovery.md)
