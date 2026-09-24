# Purchase Requisitions and Supplier Sourcing

## Summary

Use **Purchasing > Sourcing** to turn an internal purchase requirement into controlled supplier-sourcing evidence and, after an explicit award decision, a Purchase Order draft.

The workflow is:

1. Create and complete a Purchase Requisition.
2. Submit it for approval.
3. Approve, return or reject the requisition according to the current authorization and Approval Policy rules.
4. Create an RFQ from approved demand and select one or more supplier recipients.
5. Capture supplier quote responses with currency, price, minimum order quantity, lead time, validity and supplier reference evidence.
6. Compare the captured responses side by side.
7. Explicitly select an eligible quote.
8. Convert the selected quote into the existing Purchase Order draft workflow.

Depot does **not** automatically select a supplier, award a quote or place a Purchase Order.

## Purchase Requisitions

Draft and returned requisitions can be edited by users with `PurchaseRequisitions.Manage`. A requisition records the requester, creator, required-by date, optional preferred-supplier hint, business justification and item/quantity lines.

Submitting a requisition moves it into the approval boundary. Users with `PurchaseRequisitions.Approve` may approve, return or reject eligible submissions. Approval remains service-authoritative and uses the configured Approval Policy behavior where applicable. Creator/approver separation is enforced where the purchasing authorization rules require it.

Approved requisitions can proceed to sourcing. Cancelled, rejected or converted demand cannot be silently reused as active sourcing demand.

## Requests for Quotation

An RFQ can be created only from eligible approved demand. Choose the intended supplier recipients and, when useful, a response-due date. The RFQ keeps a stable link to the originating requisition and requested lines.

Users with `SupplierSourcing.Manage` can capture supplier responses. Quote lines must correspond to the requested RFQ lines. Currency, quantity, price, MOQ, lead time and validity values are validated before the response becomes sourcing evidence.

Expired responses are not eligible for award.

## Compare and select

The comparison view is deterministic and shows supplier response evidence without making a recommendation. Selection is always an explicit user action.

Before selecting a response, verify the supplier, currency, unit prices, MOQ, lead time, validity date and supplier reference. Selecting a quote records the award evidence; it does not itself create or place a Purchase Order.

## Convert to Purchase Order

A user with `SupplierSourcing.Convert` can convert the explicitly selected eligible response. Conversion uses Depot's existing Purchase Order service and creates a **Draft** Purchase Order. Repeating the same conversion is idempotent and does not create a duplicate Purchase Order.

The resulting Purchase Order retains sourcing evidence linking it to the Purchase Requisition, RFQ and selected Supplier Quote Response. Purchase Order submission, approval and ordering continue through the normal Purchase Order workflow.

See [Purchase Orders](topic:purchasing.purchase-orders) for the downstream lifecycle and [Approvals](topic:approvals.queue) for approval work.

## Attachments and evidence

Purchase Requisitions, RFQs and Supplier Quote Responses can carry Business Attachments in the Sourcing workspace. Supplier quote evidence becomes protected from destructive replacement after award so the decision trail remains stable.

Use attachments for source documents such as supplier quotations or supporting requisition evidence; attachment visibility does not grant authorization to change the underlying business record.

See [Business Attachments](topic:getting-started.business-attachments).

## Permissions and work queues

Relevant permissions are separated by responsibility:

- `PurchaseRequisitions.View` and `PurchaseRequisitions.Manage` for requisition access and preparation;
- `PurchaseRequisitions.Approve` for requisition decisions;
- `SupplierSourcing.View` and `SupplierSourcing.Manage` for RFQ and quote work;
- `SupplierSourcing.Convert` for conversion of an awarded quote to a Purchase Order draft.

UI visibility is only a convenience. Service-layer authorization remains authoritative.

Eligible requisitions, RFQs awaiting supplier quotes, overdue response dates and selected quotes awaiting Purchase Order conversion can surface in **My Work** and the **Buyer Workbench** according to the current user's permissions.

## Approval Policies

Configured Approval Policies can route requisition approvals while preserving the purchasing authorization boundary and deterministic approval evidence. Policy administration does not grant requisition approval authority.

See [Approval Policies](topic:administration.approval-policies).
