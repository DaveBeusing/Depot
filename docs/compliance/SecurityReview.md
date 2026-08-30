# Depot Security Review Standard

## Scope

A security review is required for changes that materially affect authentication, authorization, database access, credentials/secrets, cryptography, logging/audit, backups, imports/exports, generated business documents, dependencies, update/release mechanisms, or personal-data handling.

## Required review questions

1. What asset or business process can be affected?
2. Which trust boundary changes?
3. What realistic misuse or failure scenario is introduced?
4. Are controls enforced in services/business logic rather than only in the UI?
5. Can the change expose secrets, personal data, or commercially sensitive information?
6. Can the change silently alter or delete historical business state?
7. Does it introduce a dependency, parser, file format, external process, or remote endpoint?
8. What automated or manual evidence proves the control works?
9. What residual risk remains?
10. Does the threat model or another compliance document need updating?

## Evidence

The pull request should contain enough information to reconstruct the decision later. For material changes, link tests and identify the relevant threat/control identifier when one exists.

## Approval

Before 1.0, define who may approve high-risk security decisions and formal risk acceptance. Until then, unresolved material security risks must be treated as release blockers unless explicitly documented.
