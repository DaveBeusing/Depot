// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public sealed class ConcurrencyConflictException : InvalidOperationException
{
	public ConcurrencyConflictException(string entityName)
		: base($"The {entityName} was changed by another session. Reload the data and try again.")
	{
	}
}
