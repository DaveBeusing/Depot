// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ImportPreviewItemViewModel
	: BaseViewModel
{
	public ImportPreviewItemViewModel(
		ImportPreviewItem item)
	{
		PartNumber = item.PartNumber;
		Description = item.Description;
		Manufacturer = item.Manufacturer;
		Category = item.Category;
		Purpose = item.Purpose;
		Location = item.Location;
		Quantity = item.Quantity;
		UnitPrice = item.UnitPrice;
		TotalValue = item.TotalValue;
		ItemAlreadyExists = item.ItemAlreadyExists;
	}

	public string PartNumber { get; }

	public string Description { get; }

	public string? Manufacturer { get; }

	public string? Category { get; }

	public string Purpose { get; }

	public string Location { get; }

	public int Quantity { get; }

	public decimal UnitPrice { get; }

	public decimal TotalValue { get; }

	public bool ItemAlreadyExists { get; }
}