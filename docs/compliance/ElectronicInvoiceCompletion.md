# Electronic Invoice Production Completion

Track C / F2 closes the remaining production gaps on Depot's existing EN 16931 / XRechnung CII path.

## Supported VAT semantics

Depot persists an explicit EN 16931 VAT category on every Sales Invoice and Sales Credit Note line. The supported production set is intentionally bounded:

| Category | Meaning | Rate rule | Exemption evidence |
| --- | --- | --- | --- |
| `S` | Standard rated | positive | not required |
| `Z` | Zero rated | exactly 0 | optional |
| `E` | Exempt | exactly 0 | reason or reason code required |
| `AE` | Reverse charge | exactly 0 | reason or reason code required |

Unsupported category codes fail closed. Depot does not infer exempt or reverse-charge treatment from a zero percentage.

## Immutable finalization

Sales Invoice posting and Sales Credit Note posting finalize their structured electronic representation in the same database transaction as posting. Buyer evidence and the exact generated XRechnung XML are retained, along with a SHA-256 digest. Export reads the retained XML and verifies the digest; it does not regenerate a legal document from mutable master data.

Credit Notes reuse the source invoice's immutable Buyer snapshot and persist their own finalization record. If the source invoice has no immutable finalization record, electronic Credit Note finalization fails closed.

## Recipient and routing evidence

`SalesElectronicInvoiceEvidence` persists the recipient electronic address, scheme, routing channel, guideline identifier and validator profile used when the document is finalized. Routing metadata is evidence only; F2 does not implement an external transport platform.

## KoSIT / XRechnung release matrix

The F2 production path explicitly targets:

- XRechnung `3.0`
- UN/CEFACT CII syntax
- guideline `urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0`
- validator evidence profile `KoSIT-XRechnung-3.0-CII`

Automated external conformance covers the complete bounded F2 issuance matrix:

| Fixture | Document | VAT semantics |
| --- | --- | --- |
| `xrechnung-cii-basic.xml` | Invoice | Standard rated (`S`) |
| `xrechnung-cii-zero-rated.xml` | Invoice | Zero rated (`Z`) |
| `xrechnung-cii-exempt.xml` | Invoice | Exempt (`E`) with exemption evidence |
| `xrechnung-cii-reverse-charge.xml` | Invoice | Reverse charge (`AE`) with exemption evidence |
| `xrechnung-cii-credit-note.xml` | Credit Note | Standard rated (`S`) |

`ElectronicInvoiceConformanceFixtureTests` regenerates every matrix case through `ElectronicInvoiceService` and requires normalized XML equality with the retained fixture. The GitHub conformance workflow then validates every retained fixture with the pinned KoSIT Validator and XRechnung configuration. A hand-edited validator fixture therefore cannot become acceptance evidence unless it still matches Depot's production generator output.

The fixtures also contain the XRechnung BuyerReference and seller/buyer electronic endpoint evidence used by the generator. Recipient/routing persistence remains separately covered by Sales finalization tests because F2 does not perform external message transport.

A future XRechnung release must update the explicit conformance constants and validator evidence; it is not accepted implicitly.

## Legacy remediation

Posted legacy documents without a finalization record remain fail closed. Depot does not manufacture historical Buyer or XML evidence from current master data. Those documents require external/manual evidence handling rather than silent reconstruction.

## ZUGFeRD / Factur-X boundary

This package does not claim ZUGFeRD or Factur-X support. That requires the separate PDF/A-3 work package and independent validator evidence.
