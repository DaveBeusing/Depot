# Users and Roles

## Summary
Depot uses database-backed role-based access control. A user can hold multiple active roles, and effective permissions are their union. Authorization is enforced by business services as well as UI visibility.

## First administrator
A new database does not use a shared default administrator password. Depot requires creation of an individual initial administrator during first-run setup before normal sign-in.

## User management
1. Open **Administration > Users** to create or edit an account.
2. Enter identity information and, when setting/resetting a password, satisfy the current password policy shown by the application.
3. Assign one or more active roles.
4. Use **Roles** to inspect or maintain non-protected role permissions.
5. Save changes and start a new session where required for the updated permissions to take effect.

## Security behavior
- Passwords are stored as salted, versioned PBKDF2-HMAC-SHA256 hashes rather than recoverable plaintext.
- Repeated failed authentication attempts are temporarily throttled per account.
- The protected Administrator role cannot be removed or stripped of its intended protection.
- Creator/approver separation remains a business rule independent of simple permission visibility.
- Administrator overrides are attributable and recorded in audit evidence.
- Deactivating an account prevents future authorization/session use according to the normal session lifecycle.

> [!WARNING] Do not create shared administrator accounts unless an approved operational policy explicitly requires them. Individual attributable accounts provide better audit evidence.

## Required permissions
`Users.View` or `Roles.View`; changes require the corresponding Manage permission.

## Related topics
- [First Login](topic:getting-started.first-login)
- [Audit Log](topic:administration.audit-log)
- [Privacy Data](topic:administration.privacy-data)
