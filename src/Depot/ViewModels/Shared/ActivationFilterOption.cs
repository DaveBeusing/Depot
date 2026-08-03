// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels.Shared;

public sealed record ActivationFilterOption(string Name, bool? IsActive)
{
	public static IReadOnlyList<ActivationFilterOption> All { get; } =
	[
		new("All statuses", null),
		new("Active", true),
		new("Inactive", false)
	];
}
