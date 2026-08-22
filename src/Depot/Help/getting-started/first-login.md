# First Login

## Summary
Sign in with your Depot email address and password. After authentication, Depot opens the workspace shell on a tabless Welcome page instead of selecting a module automatically.

## Prerequisites
- Depot has been installed and initialized.
- Your account is active.
- For a server provider, the workstation can reach the database server.

## Steps
1. Start Depot.
2. Confirm that the database connection is available.
3. Enter your email address and password.
4. Select **Sign in**.
5. Review the Welcome page. It greets you by display name according to the local time of day and lists supported keyboard shortcuts.
6. Select a module from the activity bar or use **Ctrl+P** to open a workspace, section, or supported record.

## Result
Depot shows only modules permitted by your active roles. No workspace tab is selected or created automatically after sign-in. Workspaces and supported records open as tabs when you choose them.

If you close the final open tab, Depot returns to the Welcome page. The Welcome page itself is not a workspace tab.

Use **Ctrl+P** for Quick Open, **Ctrl+Shift+P** for the Command Palette, **Ctrl+Tab** to move between open tabs, **Ctrl+W** to close the active tab, and **F1** for context-sensitive Help.

## Status bar
The database indicator shows the current connection state. Hover it to see the configured database detail. The current application version is also displayed in the status bar; selecting the version opens the About page.

## Common problems
> [!WARNING] Do not share passwords or include them in diagnostics.

- If the connection is unavailable, see [Database Connection Failures](topic:troubleshooting.database-connection-failures).
- If access is missing after login, ask an administrator to review your active roles.
- If a workspace or command is not visible, your account may not have the required permission.
- Seeing the Welcome page with no tabs after sign-in or after closing the final tab is expected behavior.

## Required permissions
No application permission is required to open the sign-in window. Workspace visibility is permission-aware after sign-in.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)
