// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class CategoryInventoryReportItemViewModel
	: BaseViewModel
{
	public CategoryInventoryReportItemViewModel(
		CategoryInventoryReportItem item)
	{
		CategoryName =
			item.CategoryName;

		InventoryRows =
			item.InventoryRows;

		TotalItems =
			item.TotalItems;

		TotalStockQuantity =
			item.TotalStockQuantity;

		InventoryValue =
			item.InventoryValue;
	}

	public string CategoryName { get; }

	public int InventoryRows { get; }

	public int TotalItems { get; }

	public int TotalStockQuantity { get; }

	public decimal InventoryValue { get; }
}
