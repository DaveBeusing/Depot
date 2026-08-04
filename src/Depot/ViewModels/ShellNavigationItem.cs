// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class ShellNavigationItem
{
	public required string Name { get; init; }
	public required string IconData { get; init; }
	public required BaseViewModel Content { get; init; }
	public required Func<CancellationToken, Task> LoadAsync { get; init; }
	public string HelpTopicId { get; init; } = "getting-started.first-login";
	public bool IsSeparated { get; init; }
}
