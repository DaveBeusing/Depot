// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public sealed class InsufficientStockException : InvalidOperationException
{
	public InsufficientStockException()
		: base("The movement would result in a negative stock level.")
	{
	}
}
