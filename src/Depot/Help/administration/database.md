# Database Configuration

## Summary
Depot supports SQLite, Microsoft SQL Server, and MySQL/MariaDB through one provider-neutral data-access layer. Persisted connection secrets are protected with Windows DPAPI for the current user.

## Steps
1. Open **Administration > Database**.
2. Select the provider and enter its connection settings.
3. Run **Test Connection** before saving.
4. Save the protected `depot.settings` configuration.
5. Restart Depot if instructed to activate the new provider.

## Security behavior
- SQL Server connections created through supported settings require encrypted transport and do not trust an unvalidated server certificate by default.
- MySQL/MariaDB connections created through supported settings require TLS.
- Passwords, protected settings, and full connection strings are not shown in normal diagnostics or Audit Log output.
- Server accounts should follow least privilege: grant only the schema/data operations required by Depot for the supported deployment.
- DPAPI `CurrentUser` protection means protected settings are bound to the relevant Windows user context and are not a portable password vault for moving configuration between users or machines.

## Common problems
- Verify host, port, database name, login, certificate/TLS configuration, and firewall rules.
- If a previously working protected configuration is moved to another Windows identity or machine, re-enter the credentials rather than copying protected secret material blindly.
- For timeouts, see [Database Connection Failures](topic:troubleshooting.database-connection-failures).

## Required permissions
`Database.View`; testing or changing settings requires `Database.Manage`.

## Related topics
- [Backup and Restore](topic:administration.backup-restore)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)
