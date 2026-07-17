// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class MovementOverviewItemViewModel
	: BaseViewModel
{
	public MovementOverviewItemViewModel(
		MovementOverviewItem item)
	{
		MovementId = item.MovementId;
		TimestampUtc = item.TimestampUtc;
		InventoryId = item.InventoryId;
		ItemId = item.ItemId;
		PartNumber = item.PartNumber;
		Description = item.Description;
		PurposeName = item.PurposeName;
		WarehouseName = item.WarehouseName;
		LocationName = item.LocationName;
		MovementType = item.MovementType;
		Quantity = item.Quantity;
		UnitPrice = item.UnitPrice;
		Reference = item.Reference;
		Notes = item.Notes;
	}

	public long MovementId { get; }

	public DateTime TimestampUtc { get; }

	public DateTime TimestampLocal =>
		TimestampUtc.ToLocalTime();

	public long InventoryId { get; }

	public long ItemId { get; }

	public string PartNumber { get; }

	public string Description { get; }

	public string PurposeName { get; }

	public string WarehouseName { get; }

	public string LocationName { get; }

	public StockMovementType MovementType { get; }

	public int Quantity { get; }

	public decimal? UnitPrice { get; }

	public string? Reference { get; }

	public string? Notes { get; }
}
