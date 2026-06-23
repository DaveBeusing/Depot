// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class ImportWarning
{
	public int RowNumber { get; init; }

	public string Message { get; init; } = string.Empty;
}