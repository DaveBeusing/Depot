# Known Limitations

Updated: 2026-09-20

## Status

This document is the canonical release-facing limitation list for the current Depot development baseline. It must be reviewed against the exact Stable release candidate before publication.

Depot remains on the `0.15.x-preview` development line. Preview builds are not production-supported. Preparing this limitation list does not close the remaining Stable 1.0 release, deployment, legal or manual acceptance gates.

## Compliance and legal status

- Depot provides technical security, privacy, accounting, audit and release controls; it is not represented by this repository as GDPR/DSGVO, GoBD, HGB, IFRS, US-GAAP, CRA or other jurisdiction-wide certified/compliant software.
- CRA engineering documentation, vulnerability handling and incident-reporting procedures exist, but final product classification, conformity route, release-specific support-period determination and organizational/legal acceptance remain external release work.
- No qualified legal, accounting or tax opinion is recorded by this repository as a completed Stable 1.0 external review.
- Organization/controller responsibilities such as lawful basis, privacy notices, retention decisions, data-subject procedures and processor/recipient governance are deployment-specific.

See [Release Compliance Boundary](ReleaseComplianceBoundary.md).

## Finance and localization

- Finance is jurisdiction-neutral by default. Depot does not invent tax rates, statutory charts, filing classifications, valuation policy or accounting-policy decisions.
- The built-in `GENERIC → EU → DE` localization hierarchy is reference/configuration infrastructure, not a certified country pack.
- `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly` describe capability/responsibility boundaries, not legal compliance status.
- Statutory filing implementations, country-specific tax logic, SKR mappings and other executable jurisdiction behavior require separate implementation/acceptance when not already present in the bounded product scope.
- Current inventory costing is FIFO-based. Additional costing methods and manufacturing/WIP costing are not part of the current advertised baseline.

## Electronic invoicing

The advertised production matrix is bounded to:

- XRechnung 3.0 using UN/CEFACT CII;
- Invoice Standard rated (`S`);
- Invoice Zero rated (`Z`);
- Invoice Exempt (`E`) with exemption evidence;
- Invoice Reverse charge (`AE`) with exemption evidence;
- Standard-rated Sales Credit Note (`381`);
- ZUGFeRD 2.5.2 / Factur-X 1.09.2 hybrid documents using the `XRECHNUNG` reference profile.

Limitations:

- other VAT category/document combinations are not implicitly supported;
- other ZUGFeRD/Factur-X profiles are not implied;
- recipient/routing metadata is retained, but Depot does not provide an external invoice-delivery network or guarantee recipient/channel acceptance;
- legacy posted documents without immutable finalization evidence are not reconstructed from mutable current master data;
- application hashes provide integrity evidence, not a qualified digital signature or independent non-repudiation proof.

## Banking and external connectivity

Depot Banking supports internal bank-account configuration, CSV/`camt.053` statement import, payment proposals/execution, reconciliation and cash position.

The current baseline does not provide or certify:

- direct bank connectivity;
- EBICS;
- PSD2/open-banking API connectivity;
- external payment initiation;
- sanctions/AML/KYC decisioning;
- bank-specific `camt.053` profile certification.

Any such integration requires separate implementation, security review and deployment/bank acceptance.

## Database support

Production database support is limited to the exact accepted baselines:

- bundled SQLite runtime through `Microsoft.Data.Sqlite`;
- SQL Server 2022 / engine 16.x, with CI acceptance on SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

Newer or older versions are not implicitly production-supported. MariaDB and MySQL are independently accepted.

SQLite uses dynamic `NUMERIC` affinity and does not guarantee the complete fixed `DECIMAL(28,9)` magnitude/precision range available on the supported server providers.

## Capacity and performance

Repository performance/provider gates do not define universal capacity limits or hardware recommendations.

Production sizing must be repeated in the representative target environment with its actual:

- data volume;
- client/database network latency;
- concurrent-user/workflow mix;
- reporting/export/import/document-generation workload;
- CPU/memory/storage/database infrastructure.

A customer-specific maximum user count, database size or latency SLA must not be inferred from the repository's generic 100k regression or calibration profiles.

## Backup, retention and disaster recovery

- Depot does not replace SQL Server, MariaDB or MySQL provider-native production backup infrastructure.
- Production operators own backup scheduling, retention, encryption, off-host copies, monitoring, credentials, accepted RPO/RTO and isolated restore drills.
- Repository recovery CI proves a generic technical restore boundary only.
- Depot does not hard-code legal retention periods into destructive data jobs.
- Restoring historical backups can reintroduce data previously removed from the live database; deployment procedures must account for legally required reapplication of erasure/restriction actions.

## Identity and security operations

- OIDC/Entra integration validates configured protocol and assurance evidence, but Depot does not certify a tenant's Conditional Access, Authentication Strength or organization-wide MFA policy.
- External roles/groups/permission claims do not grant Depot permissions; local Depot RBAC remains authoritative.
- Security Event HTTP delivery is at-least-once; receivers must deduplicate using the documented delivery identity.
- Downstream SIEM retention, receiver governance and monitoring remain external responsibilities.

## Platform and support

- Preview builds are not production-supported.
- A Stable 1.0 production support window and support end date are not published by the current preview baseline.
- Supported production Windows versions must be named in the release-specific support statement before Stable distribution.
- A green CI/provider gate does not itself create a commercial support commitment.
- Security vulnerabilities should be reported through the private process described in root `SECURITY.md`.

## Stable 1.0 acceptance still outstanding

At this baseline, Stable publication still depends on release/deployment evidence including:

- restoration of live required-check enforcement for repository governance;
- real production Authenticode signing/publisher/timestamp acceptance;
- real deployment DR ownership/profile/restore evidence;
- exact-release-candidate manual accessibility acceptance;
- release-specific support statement;
- GDPR/DSGVO deployment assessment and retention procedures;
- organization-specific accounting/GoBD/localization procedures;
- final CRA applicability/classification/conformity work;
- qualified review of each marketed localization;
- customer/deployment-specific sizing.

The authoritative gate state is [Version 1.0 Release Checklist](Release1.0.md) and [Current Project Status](CurrentStatus.md).

## Publication rule

Stable release notes must include or link this document and must remove, amend or add limitations when the exact release candidate evidence changes. A limitation may be removed only when implementation plus the required acceptance evidence supports the broader claim.
