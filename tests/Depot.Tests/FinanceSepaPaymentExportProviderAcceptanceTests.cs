// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;
using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance","DatabaseProvider")]
[Trait("AcceptanceLevel","Full")]
public sealed class FinanceSepaPaymentExportProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider","SQLite")]
	public Task SQLiteSepaPaymentExportContract()=>VerifyAsync(new SqliteConnectionFactory(Path.Combine(Path.GetTempPath(),$"depot-sepa-provider-{Guid.NewGuid():N}.db")),cleanupLocal:true);

	[SqlServerProcurementFact]
	[Trait("Provider","SqlServer")]
	public Task SqlServerSepaPaymentExportContract()=>VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider","MariaDB")]
	public Task MariaDbSepaPaymentExportContract()=>VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider","MySQL")]
	public Task MySqlSepaPaymentExportContract()=>VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory,bool cleanupLocal=false)
	{
		string? localPath=cleanupLocal&&factory is SqliteConnectionFactory?ExtractSqlitePath(factory):null;
		try
		{
			var fixture=await FinanceSepaPaymentExportTests.CreateFixtureAsync(factory);
			var export=await fixture.Service.GenerateAsync(fixture.PaymentRunId);
			Assert.True(export.Id>0);
			Assert.Equal(FinanceSepaPaymentExportService.MessageVersion,export.MessageVersion);
			Assert.Equal(FinanceSepaPaymentExportStatus.Generated,export.CurrentStatus);
			var page=await fixture.Service.SearchExportsAsync(fixture.PaymentRunId,1,20);
			Assert.Contains(page.Items,value=>value.Id==export.Id&&value.XmlSha256==export.XmlSha256);
			var reloaded=await fixture.Service.GetExportAsync(export.Id);
			Assert.Equal(export.XmlPayload,reloaded!.XmlPayload);
			Assert.Contains(reloaded.StatusHistory,value=>value.Status==FinanceSepaPaymentExportStatus.Generated);
		}
		finally
		{
			if(localPath is not null)
			{
				Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
				try{File.Delete(localPath);}catch(IOException){}
			}
		}
	}

	private static string? ExtractSqlitePath(IDatabaseConnectionFactory factory)
	{
		using var connection=factory.CreateConnection();
		var builder=new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection.ConnectionString);
		return builder.DataSource;
	}
}
