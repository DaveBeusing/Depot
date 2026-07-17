// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class InventoryLookupViewModel
{
	public long Id { get; init; }

	public long ItemId { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string PurposeName { get; init; } = string.Empty;

	public string WarehouseName { get; init; } = string.Empty;

	public string LocationName { get; init; } = string.Empty;

	public string DisplayName =>
		$"{PartNumber} - {Description} | {PurposeName} | {WarehouseName} / {LocationName}";
}
