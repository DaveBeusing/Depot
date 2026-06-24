// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class DashboardRecentMovement
{
	public DateTime TimestampUtc { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public StockMovementType MovementType { get; init; }

	public int Quantity { get; init; }
}