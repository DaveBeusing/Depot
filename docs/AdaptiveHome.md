# Adaptive Home and My Work consolidation

Updated: 2026-09-19

## Purpose

Home is the daily starting workspace. It prioritizes action and continuation over module-level dashboard density.

The presentation order is:

1. greeting and daily context;
2. Needs my action;
3. three to five permission-relevant KPI cards when data is available;
4. operational quick actions authorized by effective permissions;
5. Favorites and Recent workspaces, with Default Landing as a compact preference;
6. secondary recent activity.

## Permission adaptation

Adaptive content is derived from effective permissions plus existing Dashboard and My Work projections. No `Role == X` branching is introduced.

Sales, warehouse, purchasing, finance and administration KPI candidates are built only when their existing permission-filtered source is available. Finance cards use existing My Work projections for receivables, supplier documents, bank reconciliation and payment runs. The Home projection does not add new business KPIs or queries.

Operational quick actions are permission-gated:

- New Quote and New Order for Sales creation permissions;
- New Purchase Order and Receive for Purchasing/Receiving creation permissions;
- Ship and Count for warehouse execution permissions;
- New Supplier Invoice for AP creation permission.

Users with read-only management permissions receive no mutation-oriented quick actions from this surface.

## My Work

My Work keeps the canonical sections and defaults to `Needs my action`.

Quick filters are presentation-only:

- Overdue;
- Today;
- High priority.

Amount/quantity values use semantic display (`EUR`, `pcs`, or `documents` where explicitly projected). Due state is normalized to labels including Overdue, Due today, Waiting and Blocked.

Provider failures remain isolated. A failed provider is reported without suppressing healthy provider results.

## Workspace productivity

Favorites, Recents and Default Landing continue to use User Preferences schema 2. The Home presentation no longer gives Favorites, Recents and Quick Actions three equal columns. Favorites and Recents are compact continuation surfaces; operational Home actions are presented separately above them.

## Compatibility

- no persisted schema changes;
- no new permissions;
- no new workflow/status model;
- no Help topic ID or routing changes;
- existing route IDs, service-side authorization, Favorites, Recents and Default Landing remain authoritative.
