// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class ReasonCode
{
	public long Id { get; set; }

	public string Code { get; init; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string DisplayName => Name;

	public string? Description { get; set; }

	public bool IsSystem { get; init; }

	public bool IsActive { get; set; } = true;

	public long Version { get; set; } = 1;
}
