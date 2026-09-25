# Business Attachments

Updated: 2026-09-25

## Purpose

Business Attachments provide one reusable document-management boundary for user-supplied files linked to supported Depot business records. The subsystem remains separate from authoritative generated document archives and immutable electronic-invoice evidence.

## Supported records

The allowlist covers Customer, Supplier, Item, Sales Quote, Sales Order, Sales Invoice, Purchase Order, Goods Receipt, Supplier/AP Document, Purchase Requisition, Request for Quotation, Supplier Quote Response, Project, Subscription Contract, Service Case and Service Order. Arbitrary table names are not accepted. Service-layer validation verifies the allowlisted entity kind, referenced record and the corresponding domain permission.

## Storage and revision model

`IBusinessAttachmentContentStore` is the replaceable content-storage contract. V1 stores attachment bytes in the configured Depot database so supported database backup/restore procedures retain content together with metadata.

Replacing content creates a new immutable revision and retains earlier revision metadata/content. Current metadata includes SHA-256, byte length, file name, media type, description/category, creation identity/time, current revision, status and optimistic version. Retrieval verifies byte length and SHA-256 before returning content.

The persisted contract is tracked independently as **Business Attachments feature schema 4** in `DepotFeatureVersions`. Schema 4 extends the provider-enforced entity-kind constraint through Service Case and Service Order while preserving existing metadata, revisions and content. Completed Service Orders and closed Service Cases reject attachment mutations so retained service evidence cannot be silently altered.

## Security and file boundary

Authorization is enforced by `BusinessAttachmentService`, not by UI visibility. Each entity kind reuses the underlying domain view/manage permissions.

Uploads are limited to **25 MiB per file**. File names are normalized, metadata lengths are bounded and known executable/script extensions are rejected. MIME type is descriptive metadata and is not trusted as proof that content is safe.

Depot does not execute attachment content and V1 does not contain an antivirus engine. Deployments that require malware inspection must provide an external endpoint, gateway or endpoint-protection scanning boundary.

## Performance boundary

Metadata and revision lists are bounded and never include binary content. Content is requested only for the selected attachment/revision. The 25 MiB product limit provides an explicit memory/I/O bound for the database-backed V1 path. Metadata search is bounded and does not full-text index arbitrary file contents.

## Audit, privacy and retention

Attachment mutations produce Audit evidence. Attachments linked to personal-data records are represented by the existing data-subject export workflow, including current verified content where that workflow permits it.

Retiring an attachment removes it from normal active lists but does not erase retained revisions. Legal retention, legal hold and deployment-specific erasure/restriction procedures remain external policy responsibilities.

## Provider and recovery behavior

The database-provider acceptance matrix validates Business Attachments schema provisioning and binary round-trip behavior on SQLite, SQL Server, MariaDB and MySQL. Because V1 content is database-backed, provider-native database backup/restore is the recovery boundary for remote providers.

## UI behavior

The reusable attachment panel provides upload, download, content replacement, metadata editing, retirement and revision history. It is shared across supported workspaces instead of duplicating attachment business logic.

## Explicit non-goals

Business Attachments do not replace XRechnung/ZUGFeRD evidence or generated business-document archives. OCR, automatic classification, e-signatures, Office editing, collaborative authoring, external object storage, arbitrary file-content full-text indexing and an antivirus engine remain outside V1.
