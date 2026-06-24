// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class DashboardSummary
{
	public int TotalItems { get; init; }

	public int TotalStockQuantity { get; init; }

	public decimal TotalInventoryValue { get; init; }

	public int TotalMovements { get; init; }
}