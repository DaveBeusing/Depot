// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class Inventory
{
	public long Id { get; set; }

	public long ItemId { get; set; }

	public long PurposeId { get; set; }

	public bool IsActive { get; set; }
}