# Database Configuration

## Summary
Depot supports SQLite, Microsoft SQL Server, and MySQL or MariaDB through one provider-neutral data-access layer.

## Prerequisites
- You have database administration access.
- Server credentials and network details are available when changing to a server provider.

## Steps
1. Open **Administration > Database**.
2. Select the provider and enter its connection settings.
3. Run **Test Connection** before saving.
4. Save the encrypted `depot.settings` configuration.
5. Restart Depot if the page instructs you to activate the new provider.

## Result
The login and sidebar indicators reflect the active connection state. Passwords and full connection strings are never displayed.

## Common problems
- Verify host, port, database name, login, TLS requirements, and firewall rules.
- For timeouts, see [Database Connection Failures](topic:troubleshooting.database-connection-failures).

## Required permissions
`Database.View`; testing or changing settings requires `Database.Manage`.

## Related topics
- [Backup and Restore](topic:administration.backup-restore)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)
