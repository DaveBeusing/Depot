# Document Templates and Rendering

Depot separates business-document data from page geometry.

```text
Business document
    ↓
Document render model
    ↓
Versioned document template
    ↓
Layout renderer
    ↓
PDF
```

## Scope

The foundation covers the productive Sales document outputs:

- Sales Quote
- Sales Order Confirmation
- Sales Invoice
- Credit Note
- Pick List
- Packing Slip
- Delivery Note
- Customer Return

The rendering foundation is consumed by the built-in visual designer under **Administration → Documents → Document Designer**.

## Presentation boundary

Templates are presentation-only. They cannot mutate business records, recalculate posted values, change tax treatment, replace seller or buyer identity, alter XRechnung XML, or modify finalized document evidence.

Posted Sales Invoices and Sales Credit Notes continue to resolve the immutable issuer snapshot captured by the existing posting transaction. The rendering layer receives that resolved projection; it does not query or rewrite posted business state.

## Render model

`DocumentRenderModel` is a bounded projection of publication-safe values. It contains scalar binding values and line rows only. The renderer never receives an arbitrary domain object for reflective property traversal.

Examples include:

```text
Company.Name
Company.Address
Document.Number
Document.Date
Customer.Name
Customer.BillingAddress
Document.Currency
Document.Net
Document.Tax
Document.Total
Line.Item
Line.Description
Line.Quantity
Line.UnitPrice
Line.TaxRate
Line.Total
```

Bindings are checked against `DocumentTemplateBindings`. Unknown paths fail template validation. Missing allowed values render as a fail-safe placeholder instead of invoking arbitrary code.

## Template model

A `DocumentTemplate` has:

- a stable template ID;
- a document type;
- an integer version;
- one active-version marker;
- page dimensions;
- an ordered element collection.

Supported element kinds are:

- `Text`
- `BoundText`
- `Image`
- `Line`
- `Rectangle`
- `LineTable`
- `TotalsBlock`
- `PageNumber`

Elements carry explicit page coordinates and dimensions plus bounded font, alignment, format, visibility and binding metadata. Line tables use an explicit column list and controlled `Line.*` bindings.

## Versioning

`DocumentTemplateCatalog` requires exactly one active version per registered document type and keeps explicit access to older registered versions. The initial defaults are version 1.

The deterministic built-in defaults remain version 1 for every document family. The visual designer keeps the same validated version workspace, but template versions and active state are now durable and shared through the provider-neutral database. Saving creates an inactive immutable version; activation changes only the active pointer for that document family; reset reactivates built-in version 1 without deleting history. Runtime reads refresh from persistence so another Depot client using the same database observes the active version.

The active runtime catalog is read at render time by business-document PDF generation and by the visible Factur-X/ZUGFeRD layer. Activation therefore takes effect without restarting Depot while preserving the existing business-document and immutable-finalization boundaries.

## Visual designer

The WPF designer exposes:

- the eight bounded template element types from the rendering model;
- an A4 design canvas with zoom, grid, snap, mouse/keyboard movement and extended selection;
- visible edge/corner resize handles with minimum-size, page-bound and snap-to-grid enforcement;
- bounded Undo/Redo history for add, delete, move, resize, property edits, duplicate and multi-selection transforms, including Ctrl+Z / Ctrl+Y;
- a contextual designer toolbar: global Undo/Redo, zoom, grid and snap controls remain available while selection tools are shown only for single or multi-selection states;
- alignment, distribution and equal-size commands that remain command-authoritative and are shown only for meaningful multi-selections;
- a context-sensitive property inspector that hides properties which do not apply to the selected element type;
- a normal unsaved-changes boundary: edit operations mark the draft dirty, shell navigation/tab close uses the shared discard guard, and Save Draft clears the dirty state;
- primary lifecycle actions for Save Draft, Preview and Activate, with New from Default, current-draft Duplicate, Copy selected version as draft, Discard changes and Reset Default in secondary actions.

The page is named **Document Designer** and deliberately describes itself as a designer for Depot PDF output templates rather than as a general-purpose PDF editor. Designer chrome uses the shared Depot theme resources while the paper surface remains white.

Preview continues to render fixed sample data through the existing validated PDF renderer and opens the generated PDF in the system PDF viewer. An embedded PDF surface is intentionally not introduced because Depot currently has no shared production-grade WPF PDF viewing component; this keeps Preview inside the existing renderer/security boundary instead of adding a parallel rendering stack.

`DocumentTemplates.View` gates access. `DocumentTemplates.Manage` gates layout changes and version activation. The protected Administrator role receives all permissions through the central permission catalog; the Application Administrator role receives both document-template permissions explicitly. Sales and Finance roles do not receive them implicitly.

Preview uses fixed publication-safe sample data and never mutates a business record. Invalid bindings, page overflow and invalid table configuration remain blocked by the existing `DocumentTemplateValidator`.

## Default-template migration

The default templates reproduce the existing Sales-document structure: issuer header, recipient block, metadata, line table, totals where applicable, issuer footer and page numbering. Productive page geometry is no longer owned by `SalesDocumentService`.

`SalesDocumentService` now:

1. resolves the current or immutable issuer identity;
2. projects the business record into a `DocumentRenderModel`;
3. selects the active template for the document type;
4. delegates PDF generation to `DocumentLayoutRenderer`.

Business rules and authorization remain in the existing service boundaries.

## Electronic invoices

The Factur-X/ZUGFeRD finalization path remains:

```text
finalized business invoice
    ↓
controlled render model + active validated template
    ↓
PDF/A configuration and embedded finalized XML
    ↓
XMP and SHA-256 evidence
    ↓
persisted immutable hybrid artifact
```

The renderer replaces only the visible page-layout implementation. The existing XRechnung payload, PDF/A-3B compatibility configuration, embedded XML, conformance metadata, hashing and immutable artifact persistence remain authoritative.

## Validation and deterministic serialization

`DocumentTemplateValidator` rejects:

- missing IDs;
- invalid or negative coordinates and dimensions;
- absolute elements outside the page;
- invalid fonts or font sizes;
- unsupported bindings;
- invalid line-table columns;
- duplicate element IDs.

`DocumentTemplateSerializer` uses a stable model order and string enums so the same validated template serializes deterministically.

## Tests

Regression coverage verifies:

- default-template validity and deterministic serialization;
- the binding allowlist;
- fail-safe missing binding behavior;
- invalid-coordinate rejection;
- explicit historical-version lookup and one active version;
- multi-page line tables;
- controlled image/logo rendering.

Existing electronic-invoice and finalization tests remain the authority for XRechnung and Factur-X acceptance and immutable posted-document behavior.

## Rollout and compliance acceptance

All eight currently supported productive document families use the shared rendering stack: Sales Quote, Sales Order Confirmation, Pick List, Packing Slip, Delivery Note, Sales Invoice, Credit Note and Customer Return.

Each family has a deterministic default template, a bounded render model, designer Preview, productive PDF export, controlled Company/logo bindings, line-table pagination and page-number support. Long-line regression coverage executes every default template, not only invoices.

Template persistence is feature schema **1** under the `DocumentTemplates` key in `DepotFeatureVersions`. Core schema 30 and Sales schema 14 are unchanged.

### Finalized invoice boundary

A posted invoice or credit note keeps its immutable business, issuer and buyer evidence. A normal printable representation may be rendered again from that immutable data using the currently active presentation template; this does not mutate the posted record.

The finalized XRechnung / ZUGFeRD / Factur-X artifact is generated once during finalization using the active validated template at that time and is retained as exact PDF/A-3B bytes linked to finalized XML/hash evidence. Later template activation never rewrites or silently replaces the retained compliance artifact. Export continues to use the verified stored bytes.

Designer Preview always uses fixed sample data and is explicitly not a final compliance artifact.

### Localization boundary

Literal headings and labels are template-authored presentation text. Existing business-data/date/number/currency formatting remains in the established render-model/application boundary. This rollout does not introduce a second localization engine inside the designer.
