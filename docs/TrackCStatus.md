# Track C acceptance and product decision

Reviewed: 2026-09-16

## F2 validation follow-up

PR #36 (`electronic-invoice-completion`) introduced the bounded F2 production path. PR #37 repaired provider/migration and packaged-E2E regressions. PR #38 (`packaged-e2e-acceptance-repair`) removed stale packaged-version assumptions and hardened executable corruption detection. PR #39 (`electronic-invoice-conformance-closure`) is merged into `master` and closes the repository implementation's external XML-coverage breadth.

The conformance closure binds retained fixtures to the production `ElectronicInvoiceService` and validates the bounded XRechnung 3.0 CII issuance matrix through KoSIT: Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`), Reverse-charge (`AE`) invoices and Standard-rated Credit Note (`381`). F2 remains acceptance-evidence dependent until the required candidate gates are green; merge status alone is not final acceptance.

## F3 product decision: approved

On 2026-09-16 the product decision was explicitly recorded to include ZUGFeRD/Factur-X in the Depot product promise. F3 is therefore unblocked.

The bounded initial product scope is:

- ZUGFeRD `2.5.2` / Factur-X `1.09.2`;
- German `XRECHNUNG` reference profile;
- PDF/A-3B hybrid documents generated as new documents, not conversion of an arbitrary existing PDF;
- exactly one embedded structured invoice payload named `xrechnung.xml`;
- the embedded XML is the same finalized XRechnung CII payload already produced and validated by Depot, never a second XML generator;
- Factur-X XMP metadata declares the embedded file/profile contract;
- exact PDF bytes, PDF SHA-256 and finalized XML SHA-256 are retained as immutable evidence;
- Invoice and Sales Credit Note use the same hybrid-artifact boundary;
- external PDF/A acceptance is independent from KoSIT XML acceptance and must use a pinned veraPDF validation path before F3 is production-accepted.

## F3A hybrid artifact foundation

The `zugferd-facturx` package introduces Sales feature schema `14` and a provider-neutral `SalesHybridElectronicInvoiceArtifacts` store. `ZugferdFacturXService` creates a PDF/A-3B document from the immutable `ElectronicInvoice` finalization model, embeds the exact finalized `xrechnung.xml` using `AFRelationship=Alternative`, writes the Factur-X/XRECHNUNG XMP contract, hashes the PDF and persists the exact artifact in the same transaction as invoice or credit-note finalization.

Exports use persisted bytes and verify SHA-256 evidence. Legacy records are not reconstructed from mutable customer/company master data.

The repository test boundary verifies the embedded XML bytes, PDF/A/XMP markers, conformance constants, schema migration, persistence and tamper detection. This structural implementation is not itself independent PDF/A certification.

## Remaining F3 acceptance work

F3 remains pending until representative production-generated invoice and credit-note hybrid artifacts pass a pinned external veraPDF PDF/A-3 validation gate together with the established KoSIT/XRechnung validation. The external validator evidence must be retained and must fail closed on invalid PDF/A/XMP/associated-file output.

## Track status and next work

F1 is merged. F2 repository implementation/conformance breadth are merged and remain final-evidence dependent. F3 is unblocked and F3A hybrid artifact implementation is in progress. F4 and F5 have not been started.

After F3A compiles and passes repository gates, the next Track C package is F3B independent PDF/A/Factur-X conformance closure; only after that evidence is green should F3 be marked accepted and Track C proceed to F4.
