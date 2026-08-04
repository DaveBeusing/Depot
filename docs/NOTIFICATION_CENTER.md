# Depot Notification Center

Depot includes an internal, database-backed Notification Center. It is a personal inbox for workflow and system events; it is not a chat system and does not provide email, attachments, replies, browser content, or real-time push.

## Domain model

`Notification` stores immutable shared content, source metadata, creation and optional expiry timestamps, and an optimistic-concurrency version. `NotificationRecipient` materializes one concrete recipient per notification and stores that user's read/archive state. The unique `(NotificationId, UserId)` constraint prevents duplicate recipients.

Normal users never delete notifications and can query or update only their own recipient rows. Expired notifications remain stored for history but are excluded from inbox pages and unread counts.

## Recipient rules

Recipients are resolved from active users and active RBAC roles at creation time. Later role changes never rewrite historical recipients.

- Purchase order submission notifies active `PurchaseOrders.Approve` holders. A normal creator is excluded from their own approval request; an Administrator may remain a recipient because the Administrator system role permits the documented self-approval exception.
- Approval and rejection notify the creator and, when different, the submitter.
- Inventory counts entering Review notify active `InventoryCounts.Post` holders.
- Scheduled backup failures notify active members of the protected Administrator system role.

Recipient IDs are deduplicated before insertion. Idempotent workflow retries return before notification creation, preventing repeated messages for the same completed operation.

## Transactions

Purchase-order submission and decisions, inventory-count Review transitions, audit entries, and their workflow notifications use the same provider-neutral connection and transaction. A notification write failure therefore rolls back the associated status and audit change. Technical backup-failure notifications are independent because the failed backup has no business transaction to join.

## Polling

The shell badge uses a `COUNT` query only. It refreshes after login, when the Notification Center opens, whenever the main window becomes active, and every 60 seconds while active. Deactivation suspends polling work, and window/session disposal cancels the timer and outstanding operations.

## Navigation targets

Notifications store controlled `SourceType` and numeric `SourceId` values, never URLs. Initial targets are `PurchaseOrder`, `PurchaseOrderApproval`, `InventoryCount`, and `DatabaseAdministration`. Navigation rechecks the current permission, opens the correct module/subpage, and loads the referenced record where applicable. A notification never grants access.

## Privacy and security

Notification content is plain text. Do not include passwords, connection strings, hashes, salts, protected settings, SQL parameters, or sensitive diagnostics. Administrators receive system notifications through the normal recipient rules but do not gain access to another user's inbox.

## Adding an event

1. Select an existing controlled source type or add one to `NotificationSourceTypes` with an explicit permission mapping.
2. Build a short plain-text `NotificationRequest` without secrets.
3. Resolve concrete recipients in the service layer.
4. For workflow events, call the transacted notification operation before the workflow commit and after the business validation.
5. Add tests for recipient rules, rollback, idempotency, source navigation, and access isolation.
