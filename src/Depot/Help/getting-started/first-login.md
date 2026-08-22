# First Login

## Summary
Depot no longer uses a shared default administrator password. A new database requires creation of its initial administrator before normal sign-in can begin.

## New database / first run
1. Start Depot and allow the selected database provider to initialize.
2. If the database has no usable application user, Depot opens the first-run administrator setup.
3. Enter the administrator's individual display name/login email and a password that satisfies the displayed password policy.
4. Confirm the password and create the administrator.
5. Continue to normal sign-in.

> [!WARNING] Do not create shared administrator credentials or reuse a password from another system.

The bootstrap decision is based on the connected database, not merely whether the executable has been started before. Connecting a fresh Depot installation to an existing configured database therefore does not create another administrator automatically.

## Normal sign-in
1. Confirm that the database connection is available.
2. Enter your Depot email/login and password.
3. Select **Sign in**.
4. Depot opens the tabless Welcome page.
5. Select a module from the activity bar or use **Ctrl+P**.

Repeated failed sign-in attempts are temporarily throttled per account.

## Result
Depot shows only modules permitted by your active roles. No workspace tab is created automatically after sign-in. Closing the final tab returns to Welcome.

Use **Ctrl+P** for Quick Open, **Ctrl+Shift+P** for the Command Palette, **Ctrl+Tab** to move between tabs, **Ctrl+W** to close the active tab, and **F1** for context Help.

## Status bar
The database indicator shows connection state and the current application version opens About when selected.

## Common problems
- If the connection is unavailable, see [Database Connection Failures](topic:troubleshooting.database-connection-failures).
- If access is missing after login, ask an administrator to review your active roles.
- If the first-run administrator page appears unexpectedly, verify that Depot is connected to the intended database and that a usable user exists there.
- Never include passwords or protected connection information in diagnostics.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Users and Roles](topic:administration.users)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)
