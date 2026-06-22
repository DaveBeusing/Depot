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
		ItemId = item.ItemId;
		PartNumber = item.PartNumber;
		Description = item.Description;
		Manufacturer = item.Manufacturer;
		Category = item.Category;
		CurrentStock = item.CurrentStock;
		AverageCost = item.AverageCost;
		InventoryValue = item.InventoryValue;
	}

	public long ItemId { get; }

	public string PartNumber { get; }

	public string Description { get; }

	public string? Manufacturer { get; }

	public string? Category { get; }

	public int CurrentStock { get; }

	public decimal AverageCost { get; }

	public decimal InventoryValue { get; }
}