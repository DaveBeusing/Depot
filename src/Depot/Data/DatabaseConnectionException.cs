// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

public sealed class DatabaseConnectionException : InvalidOperationException
{
	public DatabaseConnectionException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
