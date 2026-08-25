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
		IDatabaseConnectionFactory connectionFactory)
	{
		var initializer = connectionFactory switch
		{
			SqliteConnectionFactory sqlite => (IDatabaseInitializer)new DepotDatabase(sqlite),
			SqlServerConnectionFactory sqlServer => new SqlServerDatabase(sqlServer),
			MySqlConnectionFactory mySql => new MySqlDatabase(mySql),
			_ => throw new NotSupportedException("The database initializer is not available.")
		};
		return new ItemMasterDataInitializer(initializer, connectionFactory);
	}

	private sealed class ItemMasterDataInitializer : IDatabaseInitializer
	{
		private readonly IDatabaseInitializer _inner;
		private readonly IDatabaseConnectionFactory _connectionFactory;

		public ItemMasterDataInitializer(IDatabaseInitializer inner, IDatabaseConnectionFactory connectionFactory)
		{
			_inner = inner;
			_connectionFactory = connectionFactory;
		}

		public void Initialize()
		{
			_inner.Initialize();
			ItemMasterDataSchema.Ensure(_connectionFactory);
		}
	}
}
