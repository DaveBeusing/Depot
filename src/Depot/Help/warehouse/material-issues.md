# Material Issues

## Summary
A material issue records stock issued to a recipient or purpose and creates one withdrawal movement per line.

## Prerequisites
- Each line references an active inventory with sufficient stock.
- Each line has a reason code and a positive quantity.

## Steps
1. Open **Warehouse > Material Issues** and create a draft.
2. Enter the issue date, recipient, reference, and notes as required.
3. Add inventory lines, quantities, and reason codes.
4. Save and review the draft.
5. Select **Post Material Issue** and confirm.

## Result
Stock validation, movements, status, user information, and audit are committed atomically.

## Common problems
- The same inventory should not be added ambiguously.
- Posting is rejected when available stock is insufficient.
- A posted issue is reversed with counter-movements and remains historically visible.

## Required permissions
`MaterialIssues.View`; actions require create, edit, post, or reverse permissions.

## Related topics
- [Material Returns](topic:warehouse.material-returns)
- [Insufficient Stock](topic:troubleshooting.insufficient-stock)
