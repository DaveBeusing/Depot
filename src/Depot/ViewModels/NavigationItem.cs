// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class NavigationItem
{
	public string Name { get; init; }
		= string.Empty;

	public string? Icon { get; init; }

	public Enum Section { get; init; } = null!;
}