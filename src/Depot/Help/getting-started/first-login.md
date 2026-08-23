# First Login

## Summary
Depot no longer uses a shared default administrator password. New databases and older databases that still depend on the retired shared administrator are migrated through the administrator setup dialog.

## New database or legacy database migration
1. Start Depot and allow the selected database provider to initialize.
2. Depot checks whether the connected database has an active personal administrator.
3. If no active administrator exists, or the retired `admin@depot.local` account is still active, Depot opens the administrator setup dialog.
4. Enter an individual display name/login email and a password that satisfies the displayed password policy.
5. Create the administrator and continue to normal sign-in.

The old shared administrator is not disabled merely by opening a database. If it is still active, Depot retires it atomically only after the replacement administrator is created successfully. If an earlier preview already disabled the legacy account but left the database without an active administrator, Depot detects that state and offers the same recovery/migration setup.

> [!WARNING] Do not create shared administrator credentials or reuse a password from another system.

## Normal sign-in
1. Confirm that the database connection is available.
2. Enter your Depot email/login and password.
3. Select **Sign in**.
4. Depot opens the tabless Welcome page.
5. Select a module from the activity bar or use **Ctrl+P**.

Repeated failed sign-in attempts are temporarily throttled per account. Invalid credentials are shown as one inline authentication message.

## Result
Depot shows only modules permitted by your active roles. No workspace tab is created automatically after sign-in. Closing the final tab returns to Welcome.

Use **Ctrl+P** for Quick Open, **Ctrl+Shift+P** for the Command Palette, **Ctrl+Tab** to move between tabs, **Ctrl+W** to close the active tab, and **F1** for context Help.

## Common problems
- If the connection is unavailable, see [Database Connection Failures](topic:troubleshooting.database-connection-failures).
- If an older remote database no longer accepts `admin@depot.local`, restart with the current build. Depot will offer administrator migration when no active administrator remains.
- If access is missing after login, ask an administrator to review your active roles.
- Never include passwords or protected connection information in diagnostics.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Users and Roles](topic:administration.users)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)
