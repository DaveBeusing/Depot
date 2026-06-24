// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class DashboardRecentMovementViewModel
	: BaseViewModel
{
	public DashboardRecentMovementViewModel(
		DashboardRecentMovement movement)
	{
		TimestampUtc = movement.TimestampUtc;
		PartNumber = movement.PartNumber;
		Description = movement.Description;
		MovementType = movement.MovementType;
		Quantity = movement.Quantity;
	}

	public DateTime TimestampUtc { get; }

	public DateTime TimestampLocal =>
		TimestampUtc.ToLocalTime();

	public string PartNumber { get; }

	public string Description { get; }

	public StockMovementType MovementType { get; }

	public int Quantity { get; }
}