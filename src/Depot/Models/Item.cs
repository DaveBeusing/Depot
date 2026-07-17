// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class Item
{
	public long Id { get; set; }

	public string PartNumber { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string? Manufacturer { get; set; }

	public string? Category { get; set; }

	public string? UnitOfMeasure { get; set; }

	public string? Packaging { get; set; }

	public string? Supplier { get; set; }

	public long? ManufacturerId { get; set; }

	public long? CategoryId { get; set; }

	public long? UnitOfMeasureId { get; set; }

	public long? PackagingId { get; set; }

	public long? SupplierId { get; set; }

	public bool IsActive { get; set; } = true;

	public long Version { get; set; } = 1;

}
