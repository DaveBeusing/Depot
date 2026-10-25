# Supplier invoice quantity matching

Supplier invoice save, submission, approval and posting use the same quantity matching calculation in `FinanceAccountsPayableService`.

For each purchase-order line, the service serializes evaluation on that persisted order line and reads active receipt quantities plus quantities consumed by other approved or posted supplier invoices. The current document is evaluated as a whole. Multiple current lines cannot each reuse the same received quantity.

Explicit receipt references consume only the referenced active receipt. They also consume the shared PO-line capacity. Current explicit references are evaluated before current PO-only lines; original display order is preserved. Quantities from a different explicitly referenced receipt do not reduce the selected receipt’s own capacity. Historical PO-only invoices reduce the shared total without inventing a receipt attribution.

The available quantity is bounded by both the selected receipt and the whole PO line. This also handles mixed explicit and PO-only lines, and previously approved exceptions that already exceed a receipt or the total. Each line retains its effective availability, quantity variance and price variance. A real aggregate excess still requires the separate matching-exception permission and explicit approval; posting re-evaluates the same invariant.

The calculation uses existing receipt and supplier-document evidence and introduces no new persisted schema. Regression and provider acceptance cover duplicate current lines, separate partial receipts, mixed references and concurrent approval attempts.
