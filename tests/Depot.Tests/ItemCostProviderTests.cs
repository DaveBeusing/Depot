// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
public sealed class ItemCostProviderTests
{
	[SqlServerProcurementFact]
	public Task SqlServerMigratesItemCostSchema()=>VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MySqlProcurementFact]
	public Task MySqlOrMariaDbMigratesItemCostSchema()=>VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		IDatabaseInitializer initializer=factory.Provider switch{DatabaseProvider.SqlServer=>new SqlServerDatabase((SqlServerConnectionFactory)factory),DatabaseProvider.MySql=>new MySqlDatabase((MySqlConnectionFactory)factory),_=>throw new NotSupportedException()};
		initializer.Initialize();SalesSchemaMigration.Migrate(factory);var data=new DatabaseAccess(factory);Assert.Equal(0L,Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM ItemCostProfiles;",CancellationToken.None)));Assert.Equal(SalesSchemaMigration.CurrentVersion,Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';",CancellationToken.None)));
	}
}
