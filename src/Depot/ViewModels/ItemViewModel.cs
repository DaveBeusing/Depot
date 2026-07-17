// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ItemViewModel
	: BaseViewModel
{
	public ItemViewModel(Item item)
	{
		Id = item.Id;
		PartNumber = item.PartNumber;
		Description = item.Description;
		Manufacturer = item.Manufacturer;
		Category = item.Category;
		UnitOfMeasure = item.UnitOfMeasure;
		Packaging = item.Packaging;
		Supplier = item.Supplier;
		ManufacturerId = item.ManufacturerId;
		CategoryId = item.CategoryId;
		UnitOfMeasureId = item.UnitOfMeasureId;
		PackagingId = item.PackagingId;
		SupplierId = item.SupplierId;
		IsActive = item.IsActive;
		Version = item.Version;
	}

	public long Id { get; }

	public string PartNumber { get; }

	public string Description { get; }

	public string? Manufacturer { get; }

	public string? Category { get; }
	public string? UnitOfMeasure { get; }
	public string? Packaging { get; }
	public string? Supplier { get; }
	public long? ManufacturerId { get; }
	public long? CategoryId { get; }
	public long? UnitOfMeasureId { get; }
	public long? PackagingId { get; }
	public long? SupplierId { get; }

	public bool IsActive { get; }

	public long Version { get; }
}
