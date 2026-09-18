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

## Built-in work personas
Depot keeps the original broad system roles for upgrade compatibility and adds narrower work personas for normal ERP duties:

| Role | Primary responsibility |
| --- | --- |
| **Goods Receiver** | Expected receipts and controlled Goods Receipt create/post/reverse actions without general Purchasing management. |
| **Fulfillment Operator** | Released Sales Orders, shipping and customer returns without Sales approval authority. |
| **Inventory Controller** | Counts, transfers, controlled stock corrections and inventory traceability. |
| **Accounts Receivable** | Customer open items, receipts, allocations and dunning. |
| **Accounts Payable** | Supplier invoices and PO/receipt/invoice matching. |
| **Treasury** | Bank statements, reconciliation, payment proposals, payment runs and cash position. |
| **Accountant / Controller** | General Ledger, periods, posting profiles, inventory accounting, reconciliation and financial reporting. |
| **Management Viewer** | Read-only operational and financial KPIs, reports and drilldowns. |
| **Auditor / Compliance** | Read-only/export-oriented audit, security, role and reporting evidence. |
| **Master Data Manager** | Items, customers, suppliers, warehouse/location and reference master data. |
| **Application Administrator** | Users, roles, sessions, settings, database and security administration. |

### Separation of duties
- **Accounts Receivable** and **Accounts Payable** are separate system roles.
- **Accounts Payable** does not implicitly receive supplier-invoice approval; the existing **Approver** role remains the standard approval boundary.
- **Treasury** can prepare and execute payment workflows but does not implicitly receive payment-proposal approval.
- **Accountant / Controller** does not receive `FinanceManualJournals.Post`; manual journal authority must be granted deliberately.
- **Application Administrator** does not receive operational Sales, Warehouse or Finance posting permissions.
- **Management Viewer** and **Auditor / Compliance** contain only view/export permissions.
- The protected **Administrator** role remains the only built-in full-access role.

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
