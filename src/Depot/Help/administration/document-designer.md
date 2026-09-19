# Document Designer

## Summary
The **Administration > Documents > Document Designer** page controls the printable layout used by Depot business documents. It supports Sales Quote, Sales Order Confirmation, Pick List, Packing Slip, Delivery Note, Sales Invoice, Credit Note, and Customer Return output.

The designer changes presentation only. It does not change document numbers, prices, quantities, tax calculations, posting state, customer snapshots, issuer snapshots, or other business rules.

## Permissions
Viewing templates requires `DocumentTemplates.View`. Creating drafts, activating versions, and resetting a document family to its built-in default require `DocumentTemplates.Manage`. Service-side authorization remains authoritative even when UI controls are hidden.

## Safe bindings
Template elements can use only the bindings exposed by the designer. Arbitrary object paths, reflection-style expressions, SQL, scripts, and code execution are not supported. Validation blocks unsupported bindings, invalid coordinates, page overflow, invalid fonts, and invalid line-table definitions before a draft can be saved or previewed.

## Versions and activation
Each document type starts with a deterministic built-in version 1. **Save Draft** creates a new immutable version number. A saved draft does not affect production output until **Activate Version** is used.

Template versions and their active state are stored in the Depot database. All clients using the same database share the same active layout. Depot refreshes persisted state before productive rendering and designer reads, so another client's activation is observed without changing business records.

**Reset Default** reactivates built-in version 1. It does not delete newer template versions.

## Editing workflow
Start an editable draft with **New from Default** or **Copy selected version as draft**. The latter is intentionally separate from **Duplicate current draft** so copying a stored version and duplicating the draft being edited cannot be confused.

Editing changes mark the designer as having unsaved changes. The workspace tab shows the dirty marker, and navigating away or closing the workspace uses Depot's normal discard confirmation. **Save Draft** creates the immutable stored version and clears the dirty state. **Discard changes** restores the selected stored version.

Use **Ctrl+Z** / **Ctrl+Y** for bounded Undo/Redo. Selected elements can be resized with edge/corner handles, moved by mouse or arrow keys, aligned, distributed and normalized to the same width or height. Grid snapping and page bounds apply to mouse transforms as well as keyboard movement. The property inspector shows only options supported by the selected element type.

## Preview
Preview renders fixed publication-safe sample data. It is for layout validation only and is not a business document, accounting record, invoice finalization, XRechnung artifact, or compliance artifact. Preview does not save or clear unsaved changes. Depot opens the generated preview in the system PDF viewer; no separate general-purpose PDF editor or parallel PDF rendering engine is introduced.

## Productive documents
All supported Sales/warehouse PDFs resolve the active validated template at render time. Company name, address, legal/contact lines, and logo are supplied through the controlled Company profile render model; normal layout changes do not require changing C# coordinates.

Literal labels remain template-authored presentation text. Existing date, number, currency, and business-data formatting stays in the established render-model/application boundary; the designer does not introduce a parallel localization engine.

## Posted invoices and credit notes
Posted Sales Invoices and Credit Notes keep their immutable business and issuer/buyer evidence. A normal printable PDF may be rendered again from immutable posted data with the currently active presentation template; that regenerated presentation is not the authoritative electronic-invoice compliance artifact.

For XRechnung / ZUGFeRD / Factur-X, the PDF/A-3B hybrid artifact created during finalization is retained as exact bytes together with hashes and the finalized XML relationship. Later template activation does not rewrite or replace that artifact. Export uses the retained verified bytes.

## Compliance boundary
Template changes do not prove PDF/A, XRechnung, tax, legal, or archival compliance. The electronic-invoice acceptance workflow independently validates the bounded XRechnung matrix and generated PDF/A-3B artifacts. Designer Preview is deliberately outside that acceptance boundary.

## Related topics
- [Company Master Data](topic:administration.company)
- [Sales Invoices and Credit Notes](topic:sales.invoices)
- [Shipping, Packing and Customer Returns](topic:sales.shipping)
- [Audit Log](topic:administration.audit-log)
