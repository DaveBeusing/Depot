# Subscriptions and Recurring Billing

Use **Sales > Subscriptions** to manage recurring commercial agreements and the billing periods they create.

## Contract setup

A contract requires a customer, legal entity, currency, start date, billing cadence and at least one recurring line. The supported cadences are monthly, quarterly and annual. The end date is optional; leaving it empty creates an evergreen term.

Each line has an explicit pricing policy:

- **FixedContractPrice** keeps the commercial price snapshot stored on the contract.
- **RepriceAtBilling** resolves the current applicable Sales price when the billing period is generated and stores the resulting source evidence.

Historical billed periods are never repriced retroactively.

## Lifecycle

Contracts begin as drafts. Authorized users can activate, pause, resume, amend and cancel them. Lifecycle changes retain historical evidence. Already generated billing periods cannot be destructively rewritten.

## Due billing

**Calculate due periods** materializes deterministic billing instances up to today. Month-end contracts preserve month-end behavior, including leap years. Each contract and period can have only one billing instance.

Select a due period and choose **Generate invoice draft** to create the invoice. Generation is explicit and idempotent. Depot creates a normal Sales Invoice draft and links it to the originating contract and billing period.

Blocked periods remain visible with their reason, for example when controlled repricing cannot resolve a valid price.

## Downstream processing

Recurring billing does not introduce a second invoice or accounting path. The generated draft continues through the normal Sales Invoice workflow, including existing posting authorization, electronic-invoice finalization, Accounts Receivable and General Ledger integration.

Recurring billing never auto-posts invoices and does not collect payments.

## My Work and attachments

Due, overdue and blocked billing periods can appear in **My Work** according to the current user's permissions. The contract workspace also uses standard Business Attachments, including revision history and existing attachment security controls.

## Permissions

- **SubscriptionContracts.View** — view contracts and billing history.
- **SubscriptionContracts.Manage** — create and manage contract terms and lifecycle.
- **SubscriptionBilling.Generate** — materialize due periods and generate invoice drafts.

Invoice creation/posting permissions remain independent and are still enforced by the existing Sales Invoice authority.
