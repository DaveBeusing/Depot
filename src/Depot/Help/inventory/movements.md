# Stock Movements

## Summary
The movement ledger records immutable stock increases and decreases with reason codes and document references.

## Prerequisites
- An active inventory exists for the item and storage location.
- A valid reason code is available for manual operations.

## Steps
1. Open **Inventory > Movements**.
2. Use filters and paging to locate a movement.
3. Select a movement to inspect its reason and reference.
4. If reversal is available, provide a reason code and explanation, then confirm the counter-booking.

## Result
Posted movements remain unchanged. A reversal creates one linked movement with the opposite quantity.

## Common problems
- A reversal movement cannot be reversed again.
- An original movement can only be fully reversed once.
- See [Insufficient Stock](topic:troubleshooting.insufficient-stock) if a negative counter-movement is rejected.

## Required permissions
`StockMovements.View`; posting and reversal require their specific permissions.

## Related topics
- [Material Issues](topic:warehouse.material-issues)
- [Insufficient Stock](topic:troubleshooting.insufficient-stock)
