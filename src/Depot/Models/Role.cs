// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class Role
{
	public long Id { get; set; }
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public bool IsSystem { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
	public IReadOnlyList<ApplicationPermission> Permissions { get; set; } = [];
}
