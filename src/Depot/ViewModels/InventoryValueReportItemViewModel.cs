// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class InventoryValueReportItemViewModel
	: BaseViewModel
{
	public InventoryValueReportItemViewModel(
		InventoryValueReportItem item)
	{
		InventoryId =
			item.InventoryId;

		ItemId =
			item.ItemId;

		PartNumber =
			item.PartNumber;

		Description =
			item.Description;

		Manufacturer =
			item.Manufacturer;

		Category =
			item.Category;

		PurposeName =
			item.PurposeName;

		WarehouseName =
			item.WarehouseName;

		LocationName =
			item.LocationName;

		CurrentStock =
			item.CurrentStock;

		AverageCost =
			item.AverageCost;

		InventoryValue =
			item.InventoryValue;
	}

	public long InventoryId { get; }

	public long ItemId { get; }

	public string PartNumber { get; }

	public string Description { get; }

	public string? Manufacturer { get; }

	public string? Category { get; }

	public string PurposeName { get; }

	public string WarehouseName { get; }

	public string LocationName { get; }

	public int CurrentStock { get; }

	public decimal AverageCost { get; }

	public decimal InventoryValue { get; }
}
