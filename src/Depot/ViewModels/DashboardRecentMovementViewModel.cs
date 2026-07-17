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
		InventoryId = movement.InventoryId;
		PartNumber = movement.PartNumber;
		Description = movement.Description;
		PurposeName = movement.PurposeName;
		WarehouseName = movement.WarehouseName;
		LocationName = movement.LocationName;
		MovementType = movement.MovementType;
		Quantity = movement.Quantity;
	}

	public DateTime TimestampUtc { get; }

	public DateTime TimestampLocal =>
		TimestampUtc.ToLocalTime();

	public long InventoryId { get; }

	public string PartNumber { get; }

	public string Description { get; }

	public string PurposeName { get; }

	public string WarehouseName { get; }

	public string LocationName { get; }

	public StockMovementType MovementType { get; }

	public int Quantity { get; }
}
