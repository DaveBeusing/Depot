// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum InventoryReservationStatus
{
	Active = 1,
	Consumed = 2,
	Released = 3
}

public sealed class InventoryReservation
{
	public long Id { get; set; }
	public long SalesOrderLineId { get; set; }
	public long InventoryId { get; set; }
	public int Quantity { get; set; }
	public InventoryReservationStatus Status { get; set; } = InventoryReservationStatus.Active;
	public DateTime CreatedAtUtc { get; set; }
	public long CreatedByUserId { get; set; }
	public DateTime? ReleasedAtUtc { get; set; }
	public long? ReleasedByUserId { get; set; }
	public long Version { get; set; } = 1;
	public string InventoryDisplay { get; set; } = string.Empty;
}

public sealed class SalesInventoryAvailability
{
	public long InventoryId { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string WarehouseName { get; set; } = string.Empty;
	public string StorageLocationName { get; set; } = string.Empty;
	public string PurposeName { get; set; } = string.Empty;
	public long OnHand { get; set; }
	public long Reserved { get; set; }
	public long Available => OnHand - Reserved;
	public string Display => $"{PartNumber} · {WarehouseName} / {StorageLocationName} · Available {Available:N0}";
}
