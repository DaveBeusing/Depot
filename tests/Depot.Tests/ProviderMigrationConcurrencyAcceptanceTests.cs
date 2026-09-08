// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Full")]
public sealed class ProviderMigrationConcurrencyAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SQLiteMigrationAndConcurrencyContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-provider-migration-{Guid.NewGuid():N}.db");
		try { await VerifyAsync(new SqliteConnectionFactory(path)); }
		finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerMigrationAndConcurrencyContract() => VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbMigrationAndConcurrencyContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlMigrationAndConcurrencyContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		var data = new DatabaseAccess(factory);

		await RemoveReservationInvariantAsync(data, factory.Provider);
		await data.ExecuteAsync("UPDATE DepotFeatureVersions SET Version=10 WHERE Name='Sales';", CancellationToken.None);
		SalesSchemaMigration.Migrate(factory);
		Assert.Equal(SalesSchemaMigration.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';", CancellationToken.None), CultureInfo.InvariantCulture));
		Assert.True(Convert.ToInt32(await data.ExecuteScalarAsync(ReservationIndexSql(factory.Provider), CancellationToken.None), CultureInfo.InvariantCulture) > 0);

		await data.ExecuteAsync("UPDATE DatabaseInfo SET Version=29;", CancellationToken.None);
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();
		Assert.Equal(DatabaseVersion.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DatabaseInfo;", CancellationToken.None), CultureInfo.InvariantCulture));

		await Task.WhenAll(
			Task.Run(() => DatabaseProvisioningService.Initialize(factory)),
			Task.Run(() => DatabaseProvisioningService.Initialize(factory)));
		Assert.Equal(DatabaseVersion.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DatabaseInfo;", CancellationToken.None), CultureInfo.InvariantCulture));
		Assert.Equal(SalesSchemaMigration.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';", CancellationToken.None), CultureInfo.InvariantCulture));
	}

	private static async Task RemoveReservationInvariantAsync(DatabaseAccess data, DatabaseProvider provider)
	{
		switch (provider)
		{
			case DatabaseProvider.Local:
				await data.ExecuteAsync("DROP INDEX IF EXISTS UX_InventoryReservations_Active;", CancellationToken.None);
				break;
			case DatabaseProvider.SqlServer:
				await data.ExecuteAsync("IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'InventoryReservations') AND name=N'UX_InventoryReservations_Active') DROP INDEX UX_InventoryReservations_Active ON InventoryReservations;", CancellationToken.None);
				break;
			case DatabaseProvider.MySql:
				if (Convert.ToInt32(await data.ExecuteScalarAsync(
					"SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='InventoryReservations' AND index_name='IX_InventoryReservations_SalesOrderLineId';",
					CancellationToken.None), CultureInfo.InvariantCulture) == 0)
				{
					await data.ExecuteAsync("CREATE INDEX IX_InventoryReservations_SalesOrderLineId ON InventoryReservations(SalesOrderLineId);", CancellationToken.None);
				}
				if (Convert.ToInt32(await data.ExecuteScalarAsync(ReservationIndexSql(provider), CancellationToken.None), CultureInfo.InvariantCulture) > 0)
					await data.ExecuteAsync("DROP INDEX UX_InventoryReservations_Active ON InventoryReservations;", CancellationToken.None);
				if (Convert.ToInt32(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='InventoryReservations' AND column_name='ActiveInventoryId';", CancellationToken.None), CultureInfo.InvariantCulture) > 0)
					await data.ExecuteAsync("ALTER TABLE InventoryReservations DROP COLUMN ActiveInventoryId;", CancellationToken.None);
				break;
			default:
				throw new NotSupportedException();
		}
	}

	private static string ReservationIndexSql(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_InventoryReservations_Active';",
		DatabaseProvider.SqlServer => "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'InventoryReservations') AND name=N'UX_InventoryReservations_Active';",
		DatabaseProvider.MySql => "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='InventoryReservations' AND index_name='UX_InventoryReservations_Active';",
		_ => throw new NotSupportedException()
	};
}