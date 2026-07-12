// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class NavigationItem
{
	public required string Name { get; init; }

	public string IconData { get; init; } = string.Empty;

	public required Enum Section { get; init; }

	public bool IsSeparated { get; init; }
}
