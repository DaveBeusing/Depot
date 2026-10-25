# Service Management

Use **Service > Service Management** to manage customer service cases and, when operational execution is required, a linked service order.

## Service cases

A Service Case owns the customer-service lifecycle, priority, ownership, due target and resolution state. Cases move through **New**, **Open**, **In Progress**, **Waiting**, **Resolved**, **Closed** or **Cancelled** through controlled service commands.

Generic customer interactions, reminders and follow-up tasks remain CRM Activity responsibilities. Service Case history retains lifecycle evidence such as state transitions and resolution notes rather than creating a second generic activity model.

Owned open and overdue cases can appear in **My Work**. Assignment creates a workflow notification for the assigned owner. Due and overdue state is projected from the authoritative due target; Depot does not introduce a separate service scheduler.

## Service orders

Create a Service Order only when the case requires operational work, parts or completion evidence. A case can have at most one linked execution order in the current bounded model.

The order can reference the serviced Item, Inventory record and an optional serial/lot reference. Parts consumption and returns always use the existing stock-movement authority, including its traceability rules. Service Management never changes inventory quantities directly.

Work lines capture commercial service evidence such as hours or quantities, unit price, tax rate and whether the work is billable. They are not payroll or time-attendance records.

## Parts consumption and returns

Choose the part inventory and enter quantity, commercial values and any required serial/lot allocations, then choose **Consume part**. The stock movement, tracking, service evidence and order state are saved together. A failure before commit leaves all of them unchanged.

To return a part, select its original **Consumption** row in **Parts evidence**, enter the return quantity and choose **Return part**. The displayed remaining quantity accounts for earlier linked returns. The service checks it again when posting, including requests from another session. A return uses the original location, price and tax snapshot; serial or lot allocations must identify the specific units originally consumed. Repeated or excessive returns are rejected.

If a posting result cannot be confirmed, retry the unchanged request. The workspace retains its operation identity and recovers an already committed result without creating another movement. A confirmed success clears the quantity, so another posting requires a new quantity entry. Consumption and return evidence cannot be reversed independently in the Movements workspace; use the service return workflow.

Historical returns saved before consumption links were introduced remain visible. If a location has such unmatched return evidence, further returns for that service order and location are blocked until the retained records have been reconciled. The application does not guess which consumption an older return belongs to.

## Completion

Completing a Service Order requires completion notes plus work or parts evidence. Completed and cancelled orders are immutable through normal editing. Completion resolves the linked case when appropriate and retains links to work, stock-movement and attachment evidence.

Attachments added to a Service Order become part of the retained completion evidence. Closed Service Cases and completed Service Orders reject new attachment mutations.

## Billing

For a completed order with billable work or consumed parts, an authorized user can choose **Generate invoice draft**. Depot creates one normal Sales Invoice draft through the existing Sales Invoice authority and links it to the Service Order.

Generation is idempotent. Service Management does not post invoices, calculate a separate accounting result or bypass the existing tax, electronic-invoice, Accounts Receivable or General Ledger workflow.

## Permissions

- **ServiceManagement.View** — view service cases, orders and evidence.
- **ServiceCases.Manage** — create, edit and transition service cases.
- **ServiceOrders.Manage** — create and edit service orders, work evidence and controlled parts movements.
- **ServiceOrders.Close** — complete or cancel service orders.
- **ServiceBilling.Generate** — request a Sales Invoice draft; existing Sales Invoice permissions remain independently enforced.

## Product boundary

Field-service routing, technician GPS tracking, payroll/time attendance, warranty accounting, IoT telemetry, a customer portal, automatic invoice posting and separate inventory or invoicing engines are outside this feature.
