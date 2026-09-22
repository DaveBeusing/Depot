# Approval Policies

## Purpose

Administration > Approval Policies configures Depot's bounded approval-routing rules for Purchase Orders, Sales Orders, Accounts Payable match exceptions and payment proposals.

Approval policies decide which configured stages and eligible approvers are required. They do not replace the owning business service, posting/status rules, or role-based authorization.

## Permissions

- `ApprovalPolicies.View` allows access to the policy workspace and non-mutating preview.
- `ApprovalPolicies.Manage` allows creating, editing, activating and deactivating policies and managing explicit delegations.
- Actual approval decisions still require the existing domain approval permission and eligibility for the current approval stage.

## Policy lifecycle

Create a policy as an inactive draft, configure its typed conditions and ordered stages, validate it, then activate it when ready.

Active policies are versioned. Editing or deactivating a policy does not rewrite approval chains that already started because each approval instance retains an immutable snapshot of the resolved policy version and stages.

## Conditions and precedence

Conditions are allowlisted and typed. Supported values are limited to business attributes that Depot persists for the corresponding subject, such as amount thresholds/ranges, currency where meaningful, legal entity and accounting book where available.

Conditions within one policy are combined together. Among applicable active/effective policies, the highest priority resolves. If multiple applicable policies share the same highest priority, Depot fails closed instead of choosing an implicit tie-breaker.

## Stages and approvers

Stages execute sequentially. Every stage must have at least one eligible role or user target. A decision is accepted only for the current stage.

Role/user targets must reference active Depot identities. Delegation is honored only when explicitly configured with effective dates and retained evidence; UI state never creates implicit approval authority.

## Preview

Use Preview to evaluate bounded sample attributes without creating or changing Purchase Orders, Sales Orders, supplier documents or payment proposals. Preview uses the same deterministic policy resolver as business initiation.

A successful preview shows the resolved policy and stage flow. An ambiguity or invalid configuration produces an actionable validation/error state.

## Domain behavior

- Purchase Orders and Sales Orders remain pending while intermediate stages are outstanding; their owning services perform final approved/rejected transitions.
- Accounts Payable match exceptions remain within the supplier-document transaction boundary.
- Payment proposals keep the existing Banking boundary; policy stages do not create a separate posting/execution workflow.

## My Work and notifications

Needs My Action uses both the existing domain permission and current snapshot-stage eligibility for policy-backed approvals. Notifications and workflow projections use the same persisted approval evidence.

Legacy pending records created before a policy snapshot remain readable under their established domain behavior.

## Troubleshooting

If a document cannot resolve an approval plan:

1. confirm an active/effective policy exists for its subject kind;
2. verify condition values and effective dates;
3. check that the highest-priority match is unique;
4. verify every stage has active role/user targets;
5. confirm the deciding user has both current-stage eligibility and the existing domain approval permission.

For stale edits or duplicate decisions, reload the current record and retry from the latest version.

Related topics: [Approvals](topic:approvals.queue), [Purchase Orders](topic:purchasing.purchase-orders), [Sales Approvals](topic:sales.approvals), [Accounts Payable](topic:finance.payables), [Banking and Payments](topic:finance.banking), [Audit Log](topic:administration.audit-log).
