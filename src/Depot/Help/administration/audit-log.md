# Audit Log

## Summary
The read-only Audit Log records who changed a business entity, when it changed, and sanitized before/after values. It also supports structured evidence export for classified retained business records.

## Steps
1. Open **Administration > Audit Log**.
2. Filter by time, user, entity, action, or entity ID.
3. Select an entry to load its details on demand.
4. Compare the sanitized before and after values.
5. Use **Export CSV** for the filtered audit list when permitted.
6. For a classified business record, use **Export evidence** to create structured JSON containing record classification, chronological audit history, actor/timestamps, sanitized snapshots, and the latest known snapshot.

## Integrity and privacy
- Audit timestamps are stored as UTC evidence and displayed with clear time context.
- Passwords, hashes, salts, connection strings, tokens, protected settings, and other secrets are masked/excluded.
- Normal application workflows do not edit or delete audit records.
- Direct database manipulation is privileged break-glass activity and is outside normal Depot operation.

## Required permissions
`AuditLog.View`; exports require the applicable audit export permission.

## Related topics
- [Users and Roles](topic:administration.users)
- [Privacy Data](topic:administration.privacy-data)
- [Concurrency Conflicts](topic:troubleshooting.concurrency-conflict)
