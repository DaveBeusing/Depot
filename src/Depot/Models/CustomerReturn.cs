// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum CustomerReturnStatus
{
	Draft = 1,
	Posted = 2
}

public sealed class CustomerReturn
{
	public long Id { get; set; }
	public string ReturnNumber { get; set; } = string.Empty;
	public long ShipmentId { get; set; }
	public long SalesOrderId { get; set; }
	public long CustomerId { get; set; }
	public DateTime ReturnDate { get; set; } = DateTime.Today;
	public CustomerReturnStatus Status { get; set; } = CustomerReturnStatus.Draft;
	public string Reason { get; set; } = string.Empty;
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public DateTime? PostedAtUtc { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<CustomerReturnLine> Lines { get; set; } = [];
}

public sealed class CustomerReturnLine
{
	public long Id { get; set; }
	public long CustomerReturnId { get; set; }
	public long ShipmentLineId { get; set; }
	public long InventoryId { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public long Version { get; set; } = 1;
}
