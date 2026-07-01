// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class StockMovement
{
	public long Id { get; set; }

	public long ItemId { get; set; }

	public long? InventoryId { get; set; }

	public StockMovementType MovementType { get; set; }

	public DateTime TimestampUtc { get; set; }

	public int Quantity { get; set; }

	public decimal? UnitPrice { get; set; }

	public string? Reference { get; set; }

	public string? Notes { get; set; }
}