// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class ImportPreviewItem
{
	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string? Manufacturer { get; init; }

	public string? Category { get; init; }

	public int Quantity { get; init; }

	public decimal UnitPrice { get; init; }

	public bool ItemAlreadyExists { get; init; }

	public decimal TotalValue { get; init; }
}