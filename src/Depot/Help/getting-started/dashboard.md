# Dashboard

## Summary
The Dashboard provides a permission-aware operational overview of Depot, a **My Work** queue for concrete business work and a personal **My workspace** region for recurring navigation. It combines role-oriented work projections, inventory summary information, module metrics, recent inventory activity, favorites, recently used workspaces and permission-aware quick actions.

Administrators receive every dashboard overview currently provided by the application: Inventory, Purchasing, Warehouse, Sales, Approvals, Administration, and a Reports entry point. Non-administrator visibility remains permission-aware.

## Prerequisites
- You are signed in.
- Your account can access the Dashboard.
- Individual module metrics require the corresponding application access unless you are an administrator.

## My Work
**My Work** projects existing business states into a bounded personal work queue. It does not create a second workflow engine or copy business status into separate persistence.

The five sections are:

- **Needs my action** — records for which your current permissions expose a concrete next action.
- **My drafts** — your own mutable drafts where the source domain records ownership.
- **Waiting** — your own submitted or handed-off work that is currently at another process step.
- **Exceptions** — modeled business exceptions such as overdue supplier deliveries, sales backorders, supplier match exceptions, overdue receivables, inventory-count differences and unreconciled bank items.
- **Recently completed** — a bounded read-only view of recently completed work when the source domain has reliable completion evidence.

Each row links back to the existing workspace and, where supported by that workspace, opens the referenced record directly. Removing a permission removes the corresponding provider or action from My Work; stored My Work state does not exist.

My Work currently projects Purchasing and Purchase Approvals, Sales and Shipping, Inventory Counts, Accounts Receivable, Accounts Payable and Banking. Administration/Security is intentionally not projected until a genuine user-action state exists in those domains.

A failure in one safely isolated work provider is reported below the queue without suppressing the remaining providers. Refresh reloads My Work with cancellation and bounded source queries.

## My workspace
The personal region is user-specific and persists across application restarts.

- **Favorites** contains workspaces you explicitly pin.
- **Recently used** contains a bounded, deduplicated list of workspaces you opened most recently.
- **Quick Actions** exposes supported actions only when their target route is currently available to you.
- **Default landing workspace** lets you choose the workspace Depot should prefer after sign-in.

Stored route identifiers are preferences, not permissions. Titles, icons and available actions are rebuilt from the current permission-filtered shell catalog. If access to a previously stored favorite, recent workspace or landing route is removed, Depot does not display or open that route. The stored preference may remain so it can become useful again if access is restored.

If the configured default landing workspace is unavailable, Depot falls back safely to Dashboard and then to another permitted workspace instead of attempting unauthorized navigation.

## Module overviews
The Dashboard can show the following existing information:

- **Inventory** — total items, total stock quantity, inventory value, and total stock movements.
- **Purchasing** — pending or approved Purchase Orders, partially received orders, overdue deliveries, and Supplier Returns requiring attention.
- **Warehouse** — Inventory Counts awaiting review or posting and open Stock Transfers.
- **Sales** — pending approvals, orders awaiting reservation, backorders, orders ready to ship, draft shipments, draft invoices, returns this month, credit notes this month, and net sales this month.
- **Approvals** — open Purchase Order approvals, oldest submission information, and approval amount summary when available.
- **Administration** — distinct **Online Users**, total **Active Sessions**, **Sessions Today**, **Admin Logouts**, and **Revoked Today**. Online presence uses the same heartbeat rule as User Sessions; the three daily counters use the current client's local calendar day translated to UTC storage timestamps.
- **Reports** — direct access to the Reports workspace only when the signed-in account has `Reports.View` (or is an administrator).

A user signed in on two clients contributes one Online User and two Active Sessions. **Sessions Today** counts session starts during the current local day. **Admin Logouts** counts sessions ended with `AdministrativeLogout` during that day. **Revoked Today** counts sessions ended with `Revoked`, for example because an account was deactivated.

The Administration presence card navigates to **Administration > User Sessions** when the signed-in account is allowed to view user sessions. The Reports card is permission-independent from user-session administration and does not require `Users.View`.

The Dashboard also retains **Recent activity** for the latest inventory movements.

## Steps
1. Open **Dashboard** from the activity bar.
2. Review **My Work** and open a concrete work item from the applicable section.
3. Refresh My Work when you need to re-evaluate current business states.
4. Use **My workspace** to open a favorite, a recent workspace or an available quick action.
5. Pin or unpin workspaces according to your recurring work.
6. Select an available workspace as your personal default landing workspace when desired.
7. Review the module overview cards available to your account.
6. Select a module card to open its corresponding workspace.
7. Use the Administration presence card to open **User Sessions** and review active clients or recent session history.
8. Use the Reports card when your account has `Reports.View`.
9. Use **Recent activity** to review the latest inventory movements or open the Inventory movements workspace.

## Result
The Dashboard acts as an operational and personal starting point. Personal navigation preferences reduce repeated navigation without changing the authorization boundary of any workspace.

## Common problems
- A favorite, recent workspace, quick action or default landing route is hidden when the signed-in user no longer has access.
- An unavailable stored landing preference intentionally falls back to an allowed workspace.
- Administrators receive all currently implemented dashboard role metrics.
- Online presence is heartbeat-derived; a crashed or disconnected client may remain visible only until the configured presence timeout expires.
- Daily session KPIs reset by calendar day; they are not all-time counters.
- The Reports card requires `Reports.View`; it is no longer coupled to Administration/User Session visibility.
- Dashboard figures reflect the metrics implemented by the corresponding services and repositories; the Dashboard does not create separate business data.

## Required permissions
Dashboard content is permission-aware. My Work providers run only when the current user can view the source domain, and business actions are offered only when the corresponding action permission exists. Viewing User Sessions requires `Users.View`; terminating sessions requires `UserSessions.Terminate`; opening Reports requires `Reports.View`. Favorites, recents, quick actions and landing preferences do not grant access to their stored routes.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Inventory Overview](topic:inventory.overview)
- [Purchase Orders](topic:purchasing.purchase-orders)
- [Inventory Counts](topic:warehouse.inventory-counts)
- [Sales Overview](topic:sales.overview)
- [Approvals](topic:approvals.queue)
- [Reports](topic:reports.overview)
- [Users and Roles](topic:administration.users)
- [User Sessions](topic:administration.user-sessions)
