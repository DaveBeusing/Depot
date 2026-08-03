// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class SecondaryNavigationItem
{
	public required string Name { get; init; }
	public required BaseViewModel Content { get; init; }
	public required Func<CancellationToken, Task> LoadAsync { get; init; }
	public Action? Activate { get; init; }
}
