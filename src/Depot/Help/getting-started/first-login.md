# First Login

## Summary
Sign in with your Depot email address and password. The connection indicator shows whether Depot can reach the configured database.

## Prerequisites
- Depot has been installed and initialized.
- Your account is active.
- For a server provider, the workstation can reach the database server.

## Steps
1. Start Depot.
2. Confirm that the connection indicator reads **Connected**.
3. Enter your email address and password.
4. Select **Sign in**.
5. Use the activity bar on the left to open the workspaces available to your roles.

## Result
Depot opens the workspace shell and shows only modules permitted by your active roles. Open activities remain available as tabs across the top of the workspace area. Context navigation below the tabs exposes the sections of the active module.

Use **Ctrl+P** for Quick Open, **Ctrl+Shift+P** for the Command Palette, **Ctrl+Tab** to move between open workspaces, and **F1** for context-sensitive Help.

## Common problems
> [!WARNING] Do not share passwords or include them in diagnostics.

- If the connection is unavailable, see [Database Connection Failures](topic:troubleshooting.database-connection-failures).
- If access is missing after login, ask an administrator to review your active roles.
- If a workspace or command is not visible, your account may not have the required permission.

## Required permissions
No application permission is required to open the sign-in window. Workspace visibility is permission-aware after sign-in.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)
