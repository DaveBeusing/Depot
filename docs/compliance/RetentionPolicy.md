# Depot Data Retention and Lifecycle Policy

## Principle

Depot separates technical lifecycle operations from legal retention decisions. The application must not silently destroy evidence that may be subject to accounting, tax, contractual, security, or other retention requirements.

## Lifecycle operations

- **Delete** — irreversible removal; only appropriate for transient/non-required data after dependency and retention checks.
- **Deactivate** — record remains intact but is excluded from normal active workflows. Preferred for users, customers, contacts, suppliers and master data when historical references exist.
- **Anonymize** — replace identifying attributes while retaining structurally required historical records. Requires entity-specific rules and must not invalidate tax/accounting/business evidence.
- **Archive** — retain data outside normal operational views while preserving readability and integrity.
- **Legal/business retention** — prevent destructive lifecycle actions until the deployment-defined retention obligation expires.

## Technical categories

| Category | Default technical treatment |
| --- | --- |
| Users with history | deactivate; retain stable ID and audit/business references |
| Customer/supplier master with transactions | deactivate; do not cascade-delete historical documents |
| Contacts without historical dependencies | eligible for anonymization/deactivation after policy decision |
| Draft/transient records | may be deletable where existing workflow permits and no evidence requirement exists |
| Posted/finalized business documents | retain; corrections/reversals instead of destructive deletion |
| Audit evidence | retain append-oriented evidence according to deployment policy |
| Notifications | lifecycle/expiry may be shorter than business evidence; must not replace authoritative audit/business records |
| Backups | automatic retention policy applies; expiry of a backup is independent from source-record retention |
| Generated exports/PDFs | operator/deployment controlled; retention and secure disposal occur outside the source database |

## Backups and erasure

Deletion/anonymization in the live database does not rewrite historical backups. Expired backups must be removed by the backup-retention process. Restoring an older backup can reintroduce data previously removed from the live database; deployment procedures must therefore maintain an erasure/restriction log outside the restored dataset and reapply required actions after recovery where legally applicable.

## Legal parameters

Concrete retention periods are deployment policy and must be approved by the controller/qualified adviser. Depot intentionally does not hard-code statutory periods into destructive database jobs.
