// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class StorageLocation
{
	public long Id { get; set; }
	public long WarehouseId { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public bool IsActive { get; set; }
	public long Version { get; set; } = 1;
}
