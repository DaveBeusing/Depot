# Approval Policies

## Purpose

Depot provides one bounded approval-policy foundation for the existing approval domains:

- Purchase Orders;
- Sales Orders;
- Accounts Payable match exceptions;
- Finance payment proposals.

The capability centralizes **approval routing** without becoming a generic workflow or BPM engine. Existing domain services remain authoritative for business status transitions, posting, release, ordering, execution and reversal. Approval policies determine which configured stages and eligible approvers are required before those domain transitions may complete.

## Policy model

An approval policy is versioned and contains:

- a name and optional description;
- one typed subject kind;
- priority;
- active/inactive state;
- optional UTC effective-from/effective-to dates;
- optimistic version metadata and audit attribution;
- allowlisted typed conditions;
- one or more sequential stages;
- one or more role or user approver targets per stage.

Supported conditions are deliberately constrained to persisted business attributes:

- minimum amount;
- maximum amount;
- currency where meaningful;
- legal entity identifier where available;
- accounting book identifier where available.

There are no free-form expressions, scripts, SQL fragments, webhooks or user-authored executable actions.

## Deterministic resolution

At initiation, Depot evaluates active and effective policies for the subject. Conditions are AND-combined inside each policy. The applicable policy with the highest priority wins.

If more than one applicable policy shares the highest priority, resolution fails closed with an actionable ambiguity error. Depot does not guess based on creation time, identifier, UI order or another hidden tie-breaker.

The Administration > Approval Policies preview uses the same resolver with bounded sample attributes and does not create or mutate business records.

## Immutable approval snapshots

When an approval begins, Depot stores an immutable snapshot containing the resolved policy identity/version and ordered stages/approver targets. Editing or deactivating the source policy later cannot change an in-flight or completed chain.

Each decision stores immutable evidence including:

- approval instance;
- stage order;
- decision;
- deciding user;
- display identity;
- optional comment;
- UTC decision time.

Optimistic concurrency prevents duplicate/stale stage decisions from silently succeeding.

## Domain integration

### Purchase Orders

Submitting a draft Purchase Order resolves and stores its approval snapshot in the same business transaction as the transition to Pending Approval. Intermediate policy stages keep the order Pending Approval. Only the final approved stage allows the existing Purchase Order service to transition the order to Approved. Rejection uses the existing Purchase Order rejection boundary.

### Sales Orders

Submitting a Sales Order resolves and stores the snapshot with the Draft -> Pending Approval transition. Intermediate stages preserve Pending Approval. The existing Sales Order service remains responsible for the final Approved/Rejected transition and later release/fulfillment behavior.

### Accounts Payable match exceptions

A submitted supplier document with match exceptions resolves an Accounts Payable exception policy. Intermediate approvals leave the document Pending Approval without stamping a final business decision. Final approval/rejection remains within the Accounts Payable transaction. Normal supplier-document approval without a match exception continues to use the existing domain behavior.

### Payment proposals

Creating a payment run resolves the payment-proposal approval snapshot. Intermediate approvals increment the proposal version while keeping it Draft. Only the final approved stage lets the Banking service mark the run Approved. Depot intentionally retains the existing Banking boundary, which does not expose a payment-proposal rejection operation.

## Authorization and delegation

Policy administration uses:

- `ApprovalPolicies.View`;
- `ApprovalPolicies.Manage`.

These permissions do not grant business approval authority. A stage decision requires both:

1. the existing domain approval permission; and
2. eligibility for the current snapshot stage.

Approver targets can reference active Depot roles or active Depot users. Delegation/substitution is honored only when explicitly configured with effective dates and retained audit evidence. UI state never creates implicit approval authority.

## My Work and role centers

My Work keeps using the established cross-cutting providers. For policy-backed pending approvals, Needs My Action is filtered by the current snapshot-stage eligibility in addition to the existing domain permission. Legacy pending records that predate a policy snapshot remain readable and fall back to their established domain permission behavior.

Role-center counts continue to derive from My Work, so they inherit the same policy-aware eligibility boundary.

## Persistence and compatibility

Approval Policies use a dedicated feature schema tracked in `DepotFeatureVersions`.

- Approval Policies feature schema: **1**
- Core database schema remains **30**

Schema 1 contains policy definitions, conditions, stages, approver targets, immutable approval instances/snapshots, decision evidence and explicit delegations. Provisioning and schema inspection include the feature independently of Core schema evolution.

Default active policies are seeded idempotently for the four supported subject kinds so existing single-stage approval semantics have a migration-compatible route while new approvals move to immutable snapshots.

## Administration workspace

Administration > Approval Policies provides:

- policy list and detail editing;
- typed conditions;
- ordered stages and role/user approvers;
- activate/deactivate lifecycle;
- effective dates and priority;
- validation issues;
- bounded non-mutating preview;
- existing Approval Flow visualizer integration;
- unsaved-change protection.

The workspace follows the existing Depot design system and service-layer authorization boundary.

## Explicit non-goals

Approval Policies do not provide:

- BPMN or general workflow automation;
- arbitrary scripts, expressions, SQL or webhooks;
- a replacement for RBAC;
- external identity/group authorization semantics;
- email/unauthenticated approval links;
- retroactive mutation of completed or in-flight approvals.

Any future workflow that needs configurable approvals should integrate with this bounded routing contract while retaining its own domain transaction and status authority.
