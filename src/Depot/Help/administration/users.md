# Users and Roles

## Summary
Depot uses database-backed role-based access control. A user can hold multiple active roles, and effective permissions are their union.

## Prerequisites
- You have access to user or role administration.
- At least one protected Administrator role remains usable.

## Steps
1. Open **Administration > Users** to create or edit an account.
2. Assign one or more active roles.
3. Use **Roles** to inspect or maintain non-protected role permissions.
4. Save changes and ask the affected user to start a new session when necessary.

## Result
Permissions are loaded for the active login session and enforced by services as well as UI visibility.

## Common problems
> [!WARNING] Deactivating a user immediately prevents effective authorization on the next session.

- The protected Administrator system role cannot be removed or stripped of its intended protection.
- Workflow rules such as self-approval prevention remain separate from permissions.

## Required permissions
`Users.View` or `Roles.View`; changes require the corresponding Manage permission.

## Related topics
- [Audit Log](topic:administration.audit-log)
