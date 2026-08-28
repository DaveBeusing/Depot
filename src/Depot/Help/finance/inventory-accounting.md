# Inventory Accounting

Inventory Accounting connects physical stock movements to the existing Finance General Ledger without introducing a second ledger.

## Required configuration

Before activating Inventory Accounting, configure:

- legal entity;
- fiscal calendar;
- purchase-order valuation currency;
- valuation method;
- Goods Receipt / inventory-GRNI posting profile;
- Sales Issue / COGS posting profile.

The F4 close/control policy additionally requires:

- inventory-control account;
- inventory-adjustment posting profile;
- purchase-variance posting profile;
- landed-cost posting profile.

All configured posting profiles are validated against the same legal entity/accounting-book boundary. The current implementation supports **FIFO only**.

## Goods receipts and GRNI

When Inventory Accounting is active, posting a goods receipt also creates the valuation layer and configured General Ledger consequence in the same transaction. If valuation or GL posting fails, the goods receipt does not partially commit.

Each receipt layer retains acquisition date, item, quantity, currency, unit cost and source stock movement.

A goods receipt can only be financially reversed while its valuation layer is still fully unconsumed. If downstream valued issues already consumed the layer, reverse those issues first.

## Sales shipments and COGS

A posted shipment consumes FIFO layers oldest-first. The resulting cost amount is posted through the configured Sales Issue / COGS profile.

Inventory Accounting will not create a negative valued inventory balance. If there is insufficient valued quantity, posting fails instead of inventing a cost.

Shipment reversal restores the exact previously recorded layer consumptions and creates a linked GL reversal.

## Inventory-count adjustments

Use **Adjustments & Variances > Inventory count valuation** to process a retained inventory-count reference such as `IC-000123`.

Processing is idempotent by immutable stock-movement ID:

- negative corrections consume FIFO layers;
- positive corrections use the current valued FIFO average as the cost basis;
- a positive correction fails closed if no valued basis exists;
- linked count reversals restore/remove the corresponding valuation effect.

This command can also catch up count corrections created before F4 was activated. It does not rewrite Warehouse history.

## Purchase-price variance

Enter the ID of a **posted** supplier document and choose **Process variance**. F4 compares the expected purchase-order net value for referenced invoiced quantities with the posted supplier-document net value.

A zero difference creates no variance posting. A non-zero difference is posted through the configured purchase-variance profile using debit/credit amount keys according to the sign.

F4 does not invent a matching tolerance. Matching and exception approval remain Accounts Payable controls. A posted variance can be reversed explicitly with a posting date and reason.

## Landed cost

Landed cost can be allocated to selected valuation-layer IDs by:

- **Quantity**; or
- **ExistingValue**.

Enter a positive amount, currency, posting date, reference and one or more layer IDs.

Important constraints:

- selected layers must be fully unconsumed and unreversed;
- all layers must belong to the configured accounting book;
- the landed-cost currency must match the valuation-layer currency;
- reversal is allowed only while all affected layers are still unconsumed.

Depot does not decide which freight, duty, insurance or handling charges are capitalizable. That remains an accounting-policy decision.

## Period-end reconciliation

The **Reconciliation** tab compares inventory valuation with the configured inventory-control GL account as of a selected date.

Historical valuation is reconstructed from acquisition dates, FIFO consumptions, consumption reversals, layer reversals and landed-cost timing. Later activity therefore does not silently change the valuation calculated for an earlier cutoff.

Each run stores an immutable snapshot containing:

- accounting book and inventory-control account;
- as-of date and reporting currency;
- valuation amount;
- GL amount;
- difference;
- item-level quantity/value lines;
- user/time evidence.

Run a new reconciliation for a new assessment date instead of modifying an older snapshot.

## Permissions

- `FinanceInventoryAccounting.View` — view configuration, valuation and reconciliation evidence.
- `FinanceInventoryAccounting.Manage` — maintain configuration/policy and execute adjustment, variance and landed-cost operations.

UI visibility is not the authorization boundary; Finance services enforce permissions again.

## Accounting and compliance boundary

Inventory Accounting is jurisdiction-neutral. F4 supplies FIFO mechanics and configured GL consequences, but it does not claim that FIFO, a specific account mapping, capitalization decision or resulting financial statement complies with HGB, IFRS, US-GAAP, tax law or another jurisdiction-specific framework.

Live SQL Server/MySQL-MariaDB concurrency/recovery acceptance and organization-specific accounting procedures remain deployment requirements.

## Related topics

- Finance Foundation
- General Ledger and Posting
- Accounts Payable
- Goods Receipts
- Stock Movements
- Inventory Counts
