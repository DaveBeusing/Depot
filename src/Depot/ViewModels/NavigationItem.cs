// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

using System.Windows;

public sealed class NavigationItem
{
	public string Name { get; init; } = string.Empty;

	public string? Icon { get; init; }

	public Enum Section { get; init; } = null!;

	public Thickness Margin { get; init; } = new Thickness(8, 4, 8, 4);
}