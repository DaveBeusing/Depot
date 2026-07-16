// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class DatabaseProviderFactory
{
	public static IDatabaseConnectionFactory CreateConnectionFactory(
		DatabaseConnectionSettings settings) =>
		settings.Provider switch
		{
			DatabaseProvider.Local => new SqliteConnectionFactory(settings.LocalDatabasePath),
			DatabaseProvider.SqlServer => new SqlServerConnectionFactory(settings),
			DatabaseProvider.MySql => new MySqlConnectionFactory(settings),
			_ => throw new NotSupportedException($"Database provider '{settings.Provider}' is not supported.")
		};

	public static IDatabaseInitializer CreateInitializer(
		IDatabaseConnectionFactory connectionFactory) =>
		connectionFactory switch
		{
			SqliteConnectionFactory sqlite => new DepotDatabase(sqlite),
			SqlServerConnectionFactory sqlServer => new SqlServerDatabase(sqlServer),
			MySqlConnectionFactory mySql => new MySqlDatabase(mySql),
			_ => throw new NotSupportedException("The database initializer is not available.")
		};
}
