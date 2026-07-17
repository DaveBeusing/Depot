// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class InventoryRecentMovementViewModel
	: BaseViewModel
{
	public InventoryRecentMovementViewModel(
		StockMovement movement)
	{
		TimestampUtc = movement.TimestampUtc;
		MovementType = movement.MovementType;
		ReasonCodeName = movement.ReasonCodeName;
		Quantity = movement.Quantity;
		UnitPrice = movement.UnitPrice;
		Reference = movement.Reference;
		Notes = movement.Notes;
	}

	public DateTime TimestampUtc { get; }

	public DateTime TimestampLocal =>
		TimestampUtc.ToLocalTime();

	public StockMovementType MovementType { get; }

	public string? ReasonCodeName { get; }

	public int Quantity { get; }

	public decimal? UnitPrice { get; }

	public string? Reference { get; }

	public string? Notes { get; }
}
