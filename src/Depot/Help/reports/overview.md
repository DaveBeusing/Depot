# Reports

## Summary
Reports aggregate inventory and movement information on the database server and can export permitted results.

## Prerequisites
- You have report access.
- The selected report filters are valid.

## Steps
1. Open **Reports**.
2. Select a report and define its filters.
3. Run the report.
4. Page through the result or export it when permitted.

## Result
Depot displays a bounded result set. Large exports are processed page by page to limit memory use.

## Common problems
- A report can be empty because of date or warehouse filters.
- Export access is separate from report viewing.

## Required permissions
`Reports.View`; export additionally requires `Reports.Export`.

## Related topics
- [Inventory Overview](topic:inventory.overview)
- [Stock Movements](topic:inventory.movements)
