# Serial and Lot Traceability

Use traceability when an item must be followed by serial number or lot/batch through receiving, storage, transfers, consumption, shipping, returns and reversals.

## Before you post

Open the item master and verify that the item is a **Stock item** and that **Tracking mode** is correct. `Serial number` means every physical unit has its own code. `Lot number` means a quantity can share one batch/lot code. Changing tracking policy after stock has already been posted should be treated as a controlled master-data change.

## Enter serials and lots

Tracked workflow grids show a **Serial / lot** column. Enter one allocation per line:

- `SN-001` — serial without expiry
- `SN-001|2027-12-31` — serial with expiry
- `LOT-4711|25` — lot and quantity
- `LOT-4711|25|2027-12-31` — lot, quantity and expiry

For several serials or lots, put each allocation on its own line. The allocation total must equal the document-line quantity. The entry remains transient while the document is a draft and is persisted only when the posting transaction succeeds.

The same format is available under **Inventory > Movements > New movement** for manual purchases, withdrawals and corrections.

## Receiving tracked items

For a serial-tracked receipt, enter one serial number for every received unit. Each serial allocation has quantity 1. For a lot-tracked receipt, enter each lot and its quantity until the allocation total equals the receipt-line quantity. Record the expiry date when the lot is expiry-controlled.

Depot posts the goods receipt, stock movement and tracking allocations together. If any tracking validation fails, nothing from that posting is committed.

## Moving or issuing tracked stock

Always enter the serial number or lot that is physically present at the source inventory. Depot checks the tracked balance at that exact inventory. A code that exists elsewhere is not sufficient.

A blocked tracking unit cannot be issued. An expired unit cannot be posted outbound. Resolve the underlying quality/compliance decision instead of using an unrelated stock adjustment.

## Transfers

A transfer preserves tracking identity from source to destination. The same serial/lot allocation leaves the source and enters the destination. This allows the history to show where a unit moved without creating a second identity.

## Inventory counts

Only an actual count difference creates a traceability allocation. If the count reduces tracked stock, enter the serials/lots being removed. If the count adds tracked stock, enter the serials/lots being introduced. An unchanged count needs no tracking input.

## Returns

Supplier returns and customer/material returns preserve the relevant tracking identity. When returning stock outbound, the selected serial/lot must still be available at the source inventory. When receiving a legitimate return, the historical tracking code is reused rather than replaced with a new identity.

## Reversals

A reversal uses the exact serial/lot allocations of the original movement with opposite quantities. If the original unit has since moved away and the reversal would create an invalid tracked balance, Depot blocks the reversal. Move/correct the stock through the proper workflow first.

## Traceability browser

Open **Inventory > Overview > Serial / lot traceability**. Search by part number, serial/lot code, warehouse or storage location. The balance list shows the current location and quantity derived from signed movement allocations.

Select a balance to see its movement history with movement type, quantity, warehouse/location and document reference.

## Blocking a serial number or lot

Users with item-management permission can select a traceability balance, enter a block reason and choose **Block unit**. Blocking does not change stock quantity; it prevents outbound use. **Unblock** removes the control state. Both transitions use optimistic concurrency and are audited.

## Excel opening balances

The legacy Excel import has no serial/lot allocation columns. Opening balances for tracked items therefore fail closed instead of inventing tracking identity. Post tracked opening stock through a traceable manual movement or another supported inbound workflow.

## Common validation messages

**Tracking data is required** — the item is serial/lot controlled but no allocation was supplied.

**Tracking quantities must equal movement quantity** — the allocation is incomplete or exceeds the document line.

**Multiple serial/lot captures match this stock movement** — more than one draft line could map to the same movement. Remove the unrelated transient entry or separate the posting; Depot will not guess.

**Serial number is already in stock** — the same serial currently has an on-hand balance and cannot be received again.

**Tracking unit does not exist** — an outbound workflow references a serial/lot that has never been received for that item.

**Tracking unit is blocked** — review the block reason before continuing.

**Tracking unit expired** — the expiry date is before today and outbound posting is prevented.

**No longer available at the original location** — a reversal would require tracked stock that has subsequently moved.

## Related topics

- Items
- Inventory Overview
- Stock Movements
- Goods Receipts
- Stock Transfers
- Material Issues and Returns
- Shipping and Customer Returns
- Supplier Returns
- Insufficient Stock
