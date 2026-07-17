// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record PageResult<T>(
	IReadOnlyList<T> Items,
	int PageNumber,
	int PageSize,
	long TotalCount)
{
	public bool HasPreviousPage => PageNumber > 1;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
}
