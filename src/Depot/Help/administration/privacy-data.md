# Privacy Data

## Summary
Administrators can use **Administration > Privacy Data** to locate person-related data stored in Depot and create a machine-readable discovery export for data-subject handling.

The workflow is a discovery aid. It does not automatically decide whether a record must be deleted, anonymized, archived, restricted, or retained for legal/business reasons.

## Steps
1. Open **Administration > Privacy Data**.
2. Enter a name, email address, company/contact reference, or other identifying search term.
3. Run the search.
4. Review matching users, customers, customer contacts, suppliers, and attributable audit references.
5. Export the discovery result to JSON when required for the administrative process.

## Data protection
- Password hashes, database credentials, protected settings, and other authentication secrets are excluded from the export.
- Business and audit records can have retention requirements. Do not remove retained evidence solely because it contains personal data.
- Historical backups can reintroduce previously removed or anonymized data after restore; applicable lifecycle actions must be reapplied after recovery.
- Generated PDFs, spreadsheets, CSV files, email attachments, or copies already outside Depot are separate data locations and are not modified by this workflow.

## Required permissions
Administration/privacy access is restricted to authorized administrators through Depot's RBAC controls.

## Related topics
- [Users and Roles](topic:administration.users)
- [Audit Log](topic:administration.audit-log)
- [Backup and Restore](topic:administration.backup-restore)
