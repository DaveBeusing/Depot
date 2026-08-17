# Goods Receipts

## Summary
Goods receipts record supplier deliveries against one Ordered purchase order and support partial deliveries.

## Prerequisites
- The purchase order is Ordered or Partially Received.
- Destination inventories are active and belong to the received items.
- Received quantities do not exceed the open order quantities.

## Steps
1. Open **Purchasing > Goods Receipts**. You can also use **Ctrl+Shift+P** and choose **Receive Goods** to open the workflow directly.
2. Select an open purchase order.
3. Enter the supplier delivery note number and receipt date.
4. For each received line, select the destination inventory and enter the quantity.
5. Review and post the receipt.

## Result
Depot atomically creates stock movements, updates received quantities, recalculates the order status, and writes the audit entry.

## Common problems
> [!NOTE] A supplier invoice is not required for a goods receipt.

- Overdelivery and mismatched inventories are rejected.
- Reversing a receipt creates counter-movements and reduces received quantities without changing history.
- Direct workflow commands are shown only when the corresponding workspace is available.

## Required permissions
`GoodsReceipts.View`; creation, posting, and reversal require separate permissions.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Purchase Orders](topic:purchasing.purchase-orders)
- [Insufficient Stock](topic:troubleshooting.insufficient-stock)
