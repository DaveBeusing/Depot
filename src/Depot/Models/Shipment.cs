// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ShipmentStatus
{
	Draft = 1,
	Posted = 2,
	Cancelled = 3
}

public sealed class Shipment
{
	public long Id { get; set; }
	public string ShipmentNumber { get; set; } = string.Empty;
	public long SalesOrderId { get; set; }
	public string SalesOrderNumber { get; set; } = string.Empty;
	public long CustomerId { get; set; }
	public string CustomerName { get; set; } = string.Empty;
	public DateTime ShipmentDate { get; set; } = DateTime.Today;
	public ShipmentStatus Status { get; set; } = ShipmentStatus.Draft;
	public string? Carrier { get; set; }
	public string? TrackingNumber { get; set; }
	public string? ShippingAddress { get; set; }
	public string? Notes { get; set; }
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public DateTime? PostedAtUtc { get; set; }
	public DateTime? ReversedAtUtc { get; set; }
	public long? ReversedByUserId { get; set; }
	public string? ReversalReason { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<ShipmentLine> Lines { get; set; } = [];
}

public sealed class ShipmentLine
{
	public long Id { get; set; }
	public long ShipmentId { get; set; }
	public long SalesOrderLineId { get; set; }
	public long InventoryReservationId { get; set; }
	public long InventoryId { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public long Version { get; set; } = 1;
}
