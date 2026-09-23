# Role & Persona Model

Updated: 2026-09-18

## Purpose

Depot uses database-backed RBAC with service-layer authorization as the authoritative security boundary. The role/persona model provides realistic ERP work roles while preserving the original broad system roles for existing installations.

System roles are reusable permission bundles. They do not bypass permission checks, transaction rules, approval separation, audit behavior, concurrency controls or provider-neutral data access.

## Compatibility

The following original system roles remain available with their existing definitions:

- Administrator
- Purchasing
- Approver
- Warehouse Operator
- Sales User
- Sales Manager
- Finance
- User

The protected **Administrator** role remains the full-access role. Existing users and role assignments are not remapped to the new personas.

## Work personas

| System role | Primary scope | Deliberate exclusions |
| --- | --- | --- |
| **Goods Receiver** | Expected Purchase Orders and Goods Receipt create/post/reverse workflows | General Purchasing administration and Purchase Order mutation |
| **Fulfillment Operator** | Released Sales Orders, shipping and customer returns | Sales Order creation/approval/release and pricing administration |
| **Inventory Controller** | Inventory movements, transfers, counts, corrections and reports | Finance posting and Finance configuration |
| **Accounts Receivable** | Customer open items, receipts/allocations and dunning | Accounts Payable, Banking and General Ledger administration |
| **Accounts Payable** | Supplier invoices and standard PO/receipt/invoice matching | Supplier-invoice approval, match-exception approval, Treasury and General Ledger administration |
| **Treasury** | Bank statements, reconciliation, payment proposals/runs, SEPA payment-file profiles/export/status evidence and cash position | Accounts Payable invoice capture, payment-proposal approval and General Ledger administration |
| **Accountant / Controller** | General Ledger, periods, posting profiles, inventory accounting, reconciliation and financial reporting | Manual journal posting and operational AP/AR/Treasury duties unless separately granted |
| **Management Viewer** | Operational/financial KPIs, reports and drilldowns | All mutation permissions |
| **Auditor / Compliance** | Audit log, security events, user/role visibility and relevant report exports | Post, edit, reverse, terminate and other mutation permissions |
| **Master Data Manager** | Items, customers, suppliers and warehouse/reference master data | Operational transaction posting |
| **Application Administrator** | Users, roles, sessions, settings, database, security and approval-policy administration | Sales, Warehouse and Finance posting/approval authority |

## Segregation of duties

The built-in personas intentionally preserve these boundaries:

- Accounts Receivable and Accounts Payable are distinct roles.
- Supplier-invoice approval and match-exception approval are not part of Accounts Payable by default; grant those authorities deliberately where required.
- Treasury can create payment proposals, execute payment runs and operate SEPA payment-file generation/download/status evidence but does not receive payment-proposal approval.
- Accountant / Controller does not receive `FinanceManualJournals.Post`; manual journal posting is intentionally explicit.
- Application Administrator can administer the application without receiving operational business posting permissions.
- `ApprovalPolicies.View` / `ApprovalPolicies.Manage` configure routing only; they do not grant Purchase Order, Sales Order, Accounts Payable exception or payment-proposal decision authority.
- Management Viewer and Auditor / Compliance contain view/export permissions only.
- Combining multiple roles produces the union of their permissions, so deployments remain responsible for reviewing combinations that could weaken local separation-of-duties policy.

## Provisioning and upgrades

`RbacCatalogSeeder` synchronizes the catalog during database initialization. New system roles are inserted when absent and their system-defined permission mappings are refreshed idempotently. This does not change the persisted database structure and therefore does not require a Core or feature schema-version increment.

System-role codes are reserved. If an existing installation already contains a custom role using one of those codes, catalog synchronization fails closed before that role is converted or its permission mapping is replaced. The custom role must be renamed deliberately before the upgrade proceeds.

Custom roles remain supported. New business behavior is not introduced by this model; roles only expose existing service-authorized capabilities.

## Navigation

Shell modules and Administration sections remain permission-aware. A persona sees only pages whose existing view permission is included in that persona. UI visibility is convenience and discoverability only; business services still enforce authorization independently.

## Administration

**Administration > Roles** shows all system roles through the existing role repository and role-management UI. System roles remain protected from normal editing/deactivation, while custom roles continue to be managed through the established workflow.

## Validation

Automated tests assert:

- legacy and new system-role presence;
- exact permission sets for every new persona;
- read-only Management/Auditor behavior;
- Application Administrator exclusion from operational posting authority;
- AR/AP/Treasury separation;
- manual-journal separation for Accountant / Controller;
- permission-bounded navigation profiles;
- idempotent seeding and role-repository visibility.

## Sales CRM permission profile

The Sales CRM capability extends the existing Sales personas without creating a separate CRM identity model. **Sales User** receives `SalesCrm.View`, `SalesCrmRecords.Manage`, `SalesCrmActivities.View` and `SalesCrmActivities.Manage` for operational work. **Sales Manager** additionally receives `SalesCrm.Manage` for stage/configuration and broader ownership authority. **Management Viewer** receives read-only CRM and activity visibility. Service-layer authorization remains authoritative.

## Finance budgeting responsibilities

The built-in **Finance** role receives `FinanceBudgeting.View`, `FinanceBudgeting.Manage` and `FinanceBudgeting.Lock`, but not `FinanceBudgeting.Approve`. The built-in **Approver** role receives `FinanceBudgeting.View` and `FinanceBudgeting.Approve`, but not draft-management authority.

Approval-policy stage eligibility remains an additional requirement for pending decisions. My Work uses the same policy-aware boundary.
