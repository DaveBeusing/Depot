// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class MovementOverviewItem
{
	public long MovementId { get; init; }

	public DateTime TimestampUtc { get; init; }

	public long InventoryId { get; init; }

	public long ItemId { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string PurposeName { get; init; } = string.Empty;

	public string LocationName { get; init; } = string.Empty;

	public StockMovementType MovementType { get; init; }

	public int Quantity { get; init; }

	public decimal? UnitPrice { get; init; }

	public string? Reference { get; init; }

	public string? Notes { get; init; }
}
