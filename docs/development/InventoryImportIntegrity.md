# Inventory import integrity

Excel opening-balance imports commit as one write transaction. New item/reference data, purpose, warehouse, location, inventory, opening movements, tracking validation, audits and the completed-operation marker share that transaction. A failing row or cancellation before commit rolls back the complete batch. The synchronous and asynchronous entry points use the same service workflow.

## Identity and retries

Generated previews derive their operation ID from a versioned SHA-256 fingerprint of the normalized import content. The fingerprint excludes the preview-only `ItemAlreadyExists` flag and is independent of row ordering. Reloading the same workbook after a successful import therefore recognizes the original operation even though item masters now exist.

The existing `WorkflowOperations` table retains the operation GUID, full content fingerprint in its workflow name, and the first resolved inventory as its entity anchor. Its unique operation key is locked and checked in the serializable write transaction. The marker is written only after all rows succeed. No additional persisted schema is required.

A replay returns `AlreadyCompleted` with zero new item/movement counts and an explicit UI message. A caller cannot reuse a completed operation ID with changed content. A lost response after commit is safe to retry with the same preview. Identical source content is treated as the same opening-balance import; deliberate later stock changes use the normal stock-movement workflows.

Import identities created before this behavior existed are not reconstructed from historical unkeyed opening balances. Review legacy imports before submitting them again.

## Validation and authority

The import service requires Import permission, retains item-creation and master-data permissions, and delegates physical stock posting to `MovementService`. Existing inactive item, purpose, warehouse, location or inventory records are rejected. Serial/lot requirements remain authoritative; Excel rows without required tracking allocations fail the whole batch. Non-positive quantity rows retain the existing master-data-only behavior and are reported as skipped movements.

Regression coverage includes failure on a later tracked row, master/reference/audit rollback, cancellation at the commit boundary, lost committed responses, changed-request rejection, workbook reload and overlapping retries. The same operation and rollback contracts run against SQLite, SQL Server, MariaDB and MySQL. Provider acceptance retains uniquely identified test evidence without clearing unrelated fixtures.
