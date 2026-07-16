// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

namespace Depot.Repositories;

public abstract class DatabaseRepository
{
	protected DatabaseRepository(DatabaseAccess database)
	{
		Database = database;
	}

	protected DatabaseAccess Database { get; }

	protected static DatabaseParameter Parameter(string name, object? value) =>
		new(name, value);
}
