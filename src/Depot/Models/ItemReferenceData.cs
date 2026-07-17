// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public abstract class ItemReferenceData
{
	public long Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public bool IsActive { get; set; } = true;

	public long Version { get; set; } = 1;
}

public sealed class Manufacturer : ItemReferenceData;

public sealed class Category : ItemReferenceData;

public sealed class UnitOfMeasure : ItemReferenceData;

public sealed class Packaging : ItemReferenceData;

public sealed class SupplierCategory : ItemReferenceData;
