// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class InventoryOverviewItemViewModel
	: BaseViewModel
{
	public InventoryOverviewItemViewModel(
		InventoryOverviewItem item)
	{
		InventoryId = item.InventoryId;
		ItemId = item.ItemId;
		PartNumber = item.PartNumber;
		Description = item.Description;
		Manufacturer = item.Manufacturer;
		Category = item.Category;
		PurposeName = item.PurposeName;
		LocationName = item.LocationName;
		CurrentStock = item.CurrentStock;
		AverageCost = item.AverageCost;
		InventoryValue = item.InventoryValue;
	}

	public long InventoryId { get; }

	public long ItemId { get; }

	public string PartNumber { get; }

	public string Description { get; }

	public string? Manufacturer { get; }

	public string? Category { get; }

	public string PurposeName { get; }

	public string LocationName { get; }

	public int CurrentStock { get; }

	public decimal AverageCost { get; }

	public decimal InventoryValue { get; }
}
