// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class ImportResult
{
	public int ImportedItems { get; init; }

	public int ImportedMovements { get; init; }

	public int SkippedItems { get; init; }
}