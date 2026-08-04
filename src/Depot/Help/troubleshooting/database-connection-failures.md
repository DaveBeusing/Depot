# Database Connection Failures

## Summary
A connection failure means Depot could not open or complete an operation against the configured provider.

## Prerequisites
- Do not copy credentials or connection strings into tickets or chat messages.

## Steps
1. Check the connection indicator in the login window or sidebar.
2. Confirm network access and the database service status.
3. If permitted, open **Administration > Database** and run **Test Connection**.
4. Verify provider, host, port, database name, and account status without exposing the password.
5. Retry only after connectivity is restored.

## Result
Depot reconnects through the configured SQLite, SQL Server, or MySQL/MariaDB provider and resumes normal operations.

## Common problems
| Symptom | Check |
| --- | --- |
| Timeout | Network route, firewall, server load |
| Login rejected | Account status and credentials |
| Database missing | Database name and initialization |
| Local file locked | Other Depot processes and file permissions |

> [!WARNING] Diagnostics must exclude passwords, connection strings, hashes, salts, protected configuration, and sensitive SQL parameters.

## Required permissions
No permission is required to view this troubleshooting topic. Database tests require database administration permission.

## Related topics
- [First Login](topic:getting-started.first-login)
- [Database Configuration](topic:administration.database)
