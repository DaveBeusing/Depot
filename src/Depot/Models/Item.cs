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

	public bool IsActive { get; set; } = true;

	public long Version { get; set; } = 1;

}
