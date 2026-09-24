# Procurement Sourcing

Updated: 2026-09-24

## Purpose

Depot's procurement-sourcing foundation connects internal purchase demand to supplier quotation and the existing Purchase Order authority without introducing a parallel purchasing stack.

```text
Purchase Requisition
  -> Approval
    -> Request for Quotation
      -> Supplier Quote Responses
        -> Explicit Comparison / Selection
          -> Purchase Order Draft
```

The implementation reuses Supplier, SupplierItem, Purchase Order, Approval Policy, Business Attachment, Audit, My Work and Buyer Workbench capabilities.

## Authority boundaries

Views provide presentation and bindings. `ProcurementSourcingViewModel` owns UI state and commands. `ProcurementSourcingService` is authoritative for validation, permissions, lifecycle transitions, concurrency, idempotency and transactional coordination. `ProcurementSourcingRepository` owns persistence queries and row mapping.

`PurchaseOrderService` remains the only Purchase Order creation authority used by sourcing conversion. Sourcing never places an order automatically.

## Requisition lifecycle

Purchase Requisitions use explicit `Draft`, `Submitted`, `Approved`, `Returned`, `Rejected`, `Converted` and `Cancelled` states. Requester/creator identity, relevant timestamps, required-by date, preferred-supplier hint, business justification and optimistic version are retained with the demand.

Approval can resolve through the configurable Approval Policy infrastructure while preserving the requisition approval permission and separation-of-duties rules.

## RFQ and quote evidence

An RFQ is created only from eligible approved demand and records one or more supplier recipients plus the requested item, quantity and date lines. Supplier responses retain supplier reference, currency, validity, received/captured evidence and per-line price, MOQ and lead time.

Comparison is deterministic and evidence-oriented. No scoring or recommendation engine chooses a supplier. An eligible response must be selected explicitly before conversion.

## Purchase Order conversion

Conversion requires `SupplierSourcing.Convert`, an awarded eligible RFQ and an explicitly selected response. Conversion is idempotent so repeated execution does not create duplicate Purchase Orders.

The Purchase Order is created as a draft through `PurchaseOrderService`. `ProcurementSourcingEvidence` preserves the immutable source relationship from Purchase Order to Purchase Requisition, RFQ and selected Supplier Quote Response.

## Attachments, Audit and work projection

Business Attachments support Purchase Requisition, RFQ and Supplier Quote Response evidence. Awarded quote evidence is protected from destructive replacement.

Material workflow transitions are Audit-visible. My Work and the Buyer Workbench project permission-eligible requisition approvals, RFQs awaiting responses, overdue response dates and selected quotes awaiting Purchase Order conversion.

## Permissions

- `PurchaseRequisitions.View`
- `PurchaseRequisitions.Manage`
- `PurchaseRequisitions.Approve`
- `SupplierSourcing.View`
- `SupplierSourcing.Manage`
- `SupplierSourcing.Convert`

Service-layer authorization is authoritative. Navigation, buttons and work projections never grant permission.

## Persistence and providers

Procurement Sourcing feature schema **1** is independently versioned through `DepotFeatureVersions`. It does not change Core schema 30.

The persistence contract is provisioned through the normal database path and must remain equivalent across SQLite, SQL Server, MariaDB and MySQL. Provider acceptance exercises sourcing creation, comparison, selection, conversion and evidence round-trip on the supported database families.

## Product boundary

The current feature intentionally excludes autonomous supplier selection, opaque recommendation/scoring, supplier portals, external supplier email/API transport, EDI procurement, contract lifecycle management, blanket orders, dynamic auctions and automatic Purchase Order placement.
