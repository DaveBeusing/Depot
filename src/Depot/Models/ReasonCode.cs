// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class ReasonCode
{
	public long Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public bool IsActive { get; set; } = true;

	public long Version { get; set; } = 1;
}
