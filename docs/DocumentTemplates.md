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

The visual designer is intentionally outside this foundation.

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

AP09 deliberately does not introduce template database persistence. The code-based catalog provides deterministic defaults for every existing installation without a data migration or schema-version change. A later designer/persistence package can store template versions behind the same model and validation boundary.

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
controlled render model + default template
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

## Next step

The next package is the visual document designer. It should edit this bounded template model rather than introduce a second rendering or scripting engine.
