// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ManufacturerInventoryReportItemViewModel
	: BaseViewModel
{
	public ManufacturerInventoryReportItemViewModel(
		ManufacturerInventoryReportItem item)
	{
		ManufacturerName =
			item.ManufacturerName;

		InventoryRows =
			item.InventoryRows;

		TotalItems =
			item.TotalItems;

		TotalStockQuantity =
			item.TotalStockQuantity;

		InventoryValue =
			item.InventoryValue;
	}

	public string ManufacturerName { get; }

	public int InventoryRows { get; }

	public int TotalItems { get; }

	public int TotalStockQuantity { get; }

	public decimal InventoryValue { get; }
}
