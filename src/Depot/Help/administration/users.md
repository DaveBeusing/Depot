# Users and Roles

## Summary
Depot uses database-backed role-based access control. A user can hold multiple active roles, and effective permissions are their union. Authorization is enforced by business services as well as UI visibility.

## First administrator
A new database does not use a shared default administrator password. Depot requires creation of an individual initial administrator during first-run setup before normal sign-in.

## User management
1. Open **Administration > Users** to create or edit an account.
2. Enter identity information and, when setting or resetting a password, satisfy every password requirement shown below the password field. Each rule turns green when satisfied and remains red while unmet.
3. Re-enter the password in **Confirm password**. The confirmation must match before the user can be saved.
4. For an existing account, leave both password fields blank to keep the current password unchanged.
5. Assign one or more active roles.
6. Use **Roles** to inspect or maintain non-protected role permissions.
7. Save changes and start a new session where required for updated permissions to take effect.

## Password policy
The Users editor uses the same central policy as first-run administrator setup: 12–128 characters, at least one uppercase letter, one lowercase letter, one number and one symbol, and the password must not contain the account name. The UI is advisory feedback; the same policy is also enforced when the account is saved.

## Session security
- Every successful sign-in creates a persistent User Session; failed authentication creates none.
- Users may have multiple simultaneous Depot sessions.
- **Administration > User Sessions** requires `Users.View` and shows active clients plus recent ended-session history.
- Destructive session actions require the separate `UserSessions.Terminate` permission.
- Deactivating a user ends every still-open session for that user with `Revoked` in the same transaction as the account deactivation. Already-authenticated clients therefore lose their session rather than remaining authorized indefinitely.
- Affected clients return to sign-in after their next heartbeat detects the server-side revocation.
- The currently signed-in user cannot deactivate their own account through the normal user-management workflow.

## Other security behavior
- Passwords are stored as salted, versioned PBKDF2-HMAC-SHA256 hashes rather than recoverable plaintext.
- Repeated failed authentication attempts are temporarily throttled per account.
- The protected Administrator role cannot be removed or stripped of its intended protection.
- Creator/approver separation remains a business rule independent of simple permission visibility.
- Administrator overrides are attributable and recorded in audit evidence where implemented.

> [!WARNING] Do not create shared administrator accounts unless an approved operational policy explicitly requires them. Individual attributable accounts provide better audit evidence.

## Required permissions
`Users.View` or `Roles.View`; changes require the corresponding Manage permission. Session termination additionally requires `UserSessions.Terminate`.

## Related topics
- [First Login](topic:getting-started.first-login)
- [User Sessions](topic:administration.user-sessions)
- [Audit Log](topic:administration.audit-log)
- [Privacy Data](topic:administration.privacy-data)
