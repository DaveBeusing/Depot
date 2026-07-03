// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class GroupedInventoryReportItemViewModel
	: BaseViewModel
{
	public GroupedInventoryReportItemViewModel(
		string groupName,
		int inventoryRows,
		int totalItems,
		int totalStockQuantity,
		decimal inventoryValue)
	{
		GroupName =
			groupName;

		InventoryRows =
			inventoryRows;

		TotalItems =
			totalItems;

		TotalStockQuantity =
			totalStockQuantity;

		InventoryValue =
			inventoryValue;
	}

	public string GroupName { get; }

	public int InventoryRows { get; }

	public int TotalItems { get; }

	public int TotalStockQuantity { get; }

	public decimal InventoryValue { get; }
}
