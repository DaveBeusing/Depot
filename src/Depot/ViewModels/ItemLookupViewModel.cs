// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class ItemLookupViewModel
{
	public long Id { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string DisplayName =>
		$"{PartNumber} - {Description}";
}