# Depot CRA Product Classification

## Status

**Technical preliminary assessment — legal/conformity review required before commercial placing on the EU market.**

This document records the engineering view of Depot under Regulation (EU) 2024/2847 (Cyber Resilience Act, CRA). It is not a legal opinion and does not replace a conformity assessment.

## Product description

Depot is a Windows desktop business application for inventory, warehouse, purchasing, sales, reporting, administration, authentication/authorization, audit, backup/restore, import/export, and database-backed business workflows. It can connect to local or remote databases and therefore has direct or indirect logical data connections to devices/networks.

## Preliminary scope assessment

For a commercially supplied Depot release in the EU, the working engineering assumption is that Depot is a **product with digital elements** within the general CRA scope.

Based on the functionality implemented as of this assessment, Depot does not have the core functionality of the Annex III/IV categories reviewed for important or critical products. In particular, Depot is not designed primarily as:

- an identity-management or privileged-access-management product,
- a password manager,
- anti-malware software,
- a VPN,
- a network-management system,
- a SIEM,
- an operating system,
- a PKI/certificate-issuance product,
- a security appliance or other product whose primary function is cybersecurity of other products/networks/services.

Depot contains authentication, authorization, audit, database connectivity and security controls, but those controls support its business-operations purpose rather than constituting its core product functionality.

## Working conformity route

Unless the final legal/product classification changes, the technical planning assumption is the CRA route applicable to a product with digital elements that is **not** classified as important Class I/Class II or critical. The final conformity-assessment route must be confirmed against the product version, intended purpose, marketing claims, distribution model, harmonised standards/common specifications available at that time, and any later delegated acts or guidance.

## Reassessment triggers

The classification must be reassessed before a production release and whenever Depot adds or materially expands any of the following:

- identity-management or privileged-access-management as a primary product function,
- security monitoring/SIEM capabilities,
- network/device management or configuration control,
- remote administration of third-party systems,
- malware detection/removal,
- VPN/network protection,
- substantial remote-data-processing/cloud functionality,
- functionality that changes the intended purpose or reasonably foreseeable use,
- commercial packaging/branding that changes who is the CRA manufacturer/economic operator.

## External acceptance gates

Before commercial distribution in the EU, qualified legal/compliance review must confirm:

1. that the release is in CRA scope,
2. the economic-operator role(s),
3. final product classification,
4. applicable conformity-assessment route,
5. required EU declaration of conformity and CE-marking process,
6. support-period determination for the specific marketed product,
7. user-information requirements and manufacturer contact details,
8. reporting contacts and competent CSIRT/ENISA procedures.

## References

- Regulation (EU) 2024/2847, Articles 6-8, 13 and 32; Annexes I, II, III, IV and VII.
- European Commission CRA implementation guidance and FAQs, current at the time of release review.
