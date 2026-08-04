# Audit Log

## Summary
The read-only audit log records who changed a business entity, when it changed, and sanitized before and after values.

## Prerequisites
- You have audit-log access.

## Steps
1. Open **Administration > Audit Log**.
2. Filter by time, user, entity, action, or entity ID.
3. Select an entry to load its details on demand.
4. Compare the structured before and after values.
5. Export the filtered result when permitted.

## Result
Historical users remain readable even if their accounts were later changed or removed. UTC storage and local display are identified clearly.

## Common problems
- Passwords, hashes, salts, connection strings, and protected configuration are masked.
- Large JSON payloads load only after selection.

## Required permissions
`AuditLog.View`; export additionally requires `AuditLog.Export`.

## Related topics
- [Users and Roles](topic:administration.users)
- [Concurrency Conflicts](topic:troubleshooting.concurrency-conflict)
