# Depot CRA Vulnerability and Incident Reporting Runbook

## Status

Technical/process baseline. Organizational contacts, competent CSIRT jurisdiction, ENISA Single Reporting Platform accounts, legal decision authority and production communication channels must be completed before CRA reporting obligations apply to a commercially supplied Depot product.

## Regulatory timing baseline

CRA reporting obligations apply from **11 September 2026**. When the manufacturer becomes aware of a reportable actively exploited vulnerability or severe incident affecting the security of a product with digital elements, the working runbook must preserve the following deadlines:

- **within 24 hours:** early-warning notification,
- **within 72 hours:** main notification with available assessment/details,
- actively exploited vulnerability: final report no later than 14 days after a corrective or mitigating measure is available,
- severe incident: final report within one month from the 72-hour notification.

The authoritative regulation/current Commission guidance and the CRA Single Reporting Platform must be checked at incident time; this document is an engineering timer/runbook, not legal advice.

## Trigger categories

Immediately escalate when any of the following is known or reasonably suspected:

- active exploitation of a Depot vulnerability,
- severe incident affecting Depot product security,
- compromise of release/signing/build infrastructure affecting shipped artifacts,
- compromise of credentials or update channels with product-wide impact,
- vulnerability in a bundled component that is actively exploited and applicable to Depot,
- incident capable of materially compromising confidentiality, integrity or availability of deployed Depot instances.

## Required incident record

Create a restricted incident record containing:

- detection/awareness UTC timestamp (the reporting-clock anchor),
- incident owner and legal/reporting decision owner,
- affected product/release versions,
- affected components and distribution scope,
- active-exploitation evidence/status,
- known user/customer impact,
- containment and remediation actions,
- 24h/72h/final-report due timestamps,
- notifications submitted and confirmation references,
- user/customer communication decision,
- fixed/mitigated release and validation evidence,
- lessons learned and risk-assessment updates.

## Operational timeline

### T+0 to T+4h

- preserve evidence and record the awareness timestamp in UTC,
- assign incident and engineering owners,
- establish affected versions and immediate containment,
- determine whether active exploitation/severe incident criteria may apply,
- involve legal/compliance decision authority without waiting for root-cause completion.

### Before T+24h

- obtain the reporting decision,
- submit required early warning through the current CRA reporting mechanism when applicable,
- record submission reference/time,
- start user/customer impact assessment and remediation branch/workstream.

### Before T+72h

- update technical assessment, affected versions and severity,
- submit the main notification when applicable,
- record available indicators, mitigation status and planned next actions without exposing unnecessary secrets/personal data.

### Final reporting

Track the applicable final-report deadline separately for actively exploited vulnerabilities and severe incidents. Do not close the incident until reporting obligations, remediation validation, release evidence, affected-user communication decisions and post-incident risk updates are complete.

## Evidence retention

Incident evidence, reporting confirmations, decision records and remediation/release references are compliance records and must be retained under the deployment/legal retention policy with access limited to authorized personnel.

## External acceptance gates

Before production distribution, assign named organizational roles for security incident ownership, legal/regulatory decision authority, customer communications and CRA submission; determine the competent CSIRT/main establishment; establish access to the ENISA CRA Single Reporting Platform; and run a tabletop exercise against the 24h/72h timeline.
