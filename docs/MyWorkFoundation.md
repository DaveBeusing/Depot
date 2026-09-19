# My Work Foundation

Updated: 2026-09-18

## Purpose

My Work turns existing Depot business state into concrete, permission-aware work for the signed-in user. It is a read/projection layer, not a workflow engine and not a second persistence model.

The dashboard always exposes the same five semantic sections:

```text
Needs my action
My drafts
Waiting
Exceptions
Recently completed
```

## Architecture

The implementation keeps the established boundary:

```text
Dashboard View
→ DashboardViewModel
→ MyWorkService
→ IMyWorkProvider
→ existing domain Services
→ existing Repositories
→ DatabaseAccess
```

ViewModels do not query repositories. My Work itself exposes no mutation API. Opening an item navigates to an existing permission-filtered shell route; the destination ViewModel and service remain responsible for any subsequent business action.

## Provider model

`MyWorkService` runs eligible providers in parallel. Each provider is permission-gated and returns only projections from existing domain state.

Initial providers cover:

- Purchasing, Goods Receipt work and Purchase Approvals;
- Sales Orders, Sales Approvals and Shipping;
- Inventory Counts;
- Accounts Receivable;
- Accounts Payable;
- Banking.

Administration/Security is deliberately absent from the initial provider set because this package does not invent an actionable state where the domain does not already model one.

## Classification

### Needs my action

Items appear only when the source state permits a concrete next step and the signed-in user has the corresponding permission. Examples include approval decisions, release, receipt, shipping, posting and payment execution.

### My drafts

Only owned mutable drafts are projected when the domain records creator ownership.

### Waiting

Owned work is projected when it has moved to a step that currently belongs to another authority, such as pending approval.

### Exceptions

Only existing domain evidence is used. The initial projections include overdue Purchase Orders, Sales backorders, Inventory Count differences, supplier match exceptions, overdue receivables and unreconciled bank statement lines.

### Recently completed

This section is read-only and limited to source records with reliable completion timestamps. It does not duplicate the Audit Log.

## Bounds and resilience

- Each provider receives a bounded query limit.
- Section output is capped at `MyWorkService.MaximumItemsPerSection`.
- Provider source queries use existing paging or explicit bounded repository paging.
- Providers execute in parallel.
- Cancellation flows to every provider.
- A safely isolated provider failure is returned as provider status and does not make the complete My Work surface unavailable.
- Duplicate projections with the same section, kind, entity and route are collapsed.

## Security

My Work is not an authorization boundary. Service-layer authorization remains authoritative.

Provider eligibility is recalculated on every refresh. Permission loss therefore removes projected work without relying on cached UI state. Navigation resolves through the current permission-filtered shell catalog and fails closed if the route is no longer available.

## Persistence and schema impact

My Work adds no persistence and no new business status. Core and feature schema versions remain unchanged. The Help manifest remains unchanged because no topic ID, permission mapping or Help routing contract is added.

## UI behavior

The Dashboard places **My Work** before the existing **My workspace** panel. The personal Favorites, Recents, Quick Actions and default landing behavior remain unchanged.

My Work provides:

- five tabs with item counts;
- compact read-only rows;
- priority/due-age ordering;
- empty states;
- refresh;
- double-click and action-button navigation;
- provider failure status without suppressing healthy providers.

## Validation contract

Regression coverage verifies permission removal, deterministic section ordering and bounds, cancellation, isolated provider failure handling, the absence of My Work mutation APIs and the ViewModel-to-service architecture boundary.


## Adaptive Home integration

The Home workspace treats My Work as the primary action surface. **Needs my action** is the default section, with Overdue, Today and High priority filters applied as presentation projections over the existing My Work snapshot.

Home KPI cards are bounded to existing Dashboard and My Work data and are selected from effective permissions. Operational quick actions are offered only when the corresponding create/execute permission already exists. Favorites, Recents and Default Landing remain on the existing Workspace Productivity persistence model and are presented compactly after action-oriented content.

Provider errors remain isolated by MyWorkService. No new workflow status, permission, repository, or persisted schema is introduced by the adaptive Home presentation.
