// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Full")]
public sealed class ItemCostProviderTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerMigratesItemCostSchema() => VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbMigratesItemCostSchema() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlMigratesItemCostSchema() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		IDatabaseInitializer initializer = factory.Provider switch
		{
			DatabaseProvider.SqlServer => new SqlServerDatabase((SqlServerConnectionFactory)factory),
			DatabaseProvider.MySql => new MySqlDatabase((MySqlConnectionFactory)factory),
			_ => throw new NotSupportedException()
		};
		initializer.Initialize();
		SalesSchemaMigration.Migrate(factory);
		var data = new DatabaseAccess(factory);
		var existingProfiles = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM ItemCostProfiles;", CancellationToken.None));
		var itemId = await data.InsertAsync("INSERT INTO Items (PartNumber, Description, IsActive) VALUES ($PartNumber, $Description, 1);",
			CancellationToken.None, new DatabaseParameter("$PartNumber", $"COST-MIGRATION-{Guid.NewGuid():N}"),
			new DatabaseParameter("$Description", "Provider cost migration evidence"));
		try
		{
			var profileId = await data.InsertAsync("INSERT INTO ItemCostProfiles (ItemId, BaseCostSource, Currency, Version) VALUES ($ItemId, 0, 'EUR', 7);",
				CancellationToken.None, new DatabaseParameter("$ItemId", itemId));
			SalesSchemaMigration.Migrate(factory);
			Assert.Equal(existingProfiles + 1, Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM ItemCostProfiles;", CancellationToken.None)));
			var profile = await data.QuerySingleOrDefaultAsync(
				"SELECT Id, ItemId, BaseCostSource, Currency, Version FROM ItemCostProfiles WHERE ItemId = $ItemId;",
				reader => new { Id = reader.GetInt64(0), ItemId = reader.GetInt64(1), BaseCostSource = reader.GetInt32(2), Currency = reader.GetString(3), Version = reader.GetInt64(4) },
				CancellationToken.None, new DatabaseParameter("$ItemId", itemId))
				?? throw new InvalidOperationException("The repeated migration removed its existing cost profile.");
			Assert.Equal(profileId, profile.Id);
			Assert.Equal(itemId, profile.ItemId);
			Assert.Equal(0, profile.BaseCostSource);
			Assert.Equal("EUR", profile.Currency);
			Assert.Equal(7L, profile.Version);
		}
		finally
		{
			await data.ExecuteAsync("DELETE FROM ItemCostProfiles WHERE ItemId = $ItemId;", CancellationToken.None, new DatabaseParameter("$ItemId", itemId));
			await data.ExecuteAsync("DELETE FROM Items WHERE Id = $ItemId;", CancellationToken.None, new DatabaseParameter("$ItemId", itemId));
		}
		Assert.Equal(existingProfiles, Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM ItemCostProfiles;", CancellationToken.None)));
		Assert.Equal(SalesSchemaMigration.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';", CancellationToken.None)));
	}
}
