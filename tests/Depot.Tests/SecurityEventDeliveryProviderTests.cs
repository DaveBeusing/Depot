// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class SecurityEventDeliveryProviderTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqlitePersistsDeliveryCheckpointContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-security-delivery-provider-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			await VerifyProviderContractAsync(factory, new DepotDatabase(factory));
		}
		finally
		{
			Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public async Task SqlServerPersistsDeliveryCheckpointContract()
	{
		var settings = ProcurementProviderConfiguration.GetSqlServerSettings();
		var factory = new SqlServerConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new SqlServerDatabase(factory));
	}

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public async Task MariaDbPersistsDeliveryCheckpointContract()
	{
		var settings = ProcurementProviderConfiguration.GetMariaDbSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public async Task MySqlPersistsDeliveryCheckpointContract()
	{
		var settings = ProcurementProviderConfiguration.GetMySqlSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyProviderContractAsync(IDatabaseConnectionFactory factory, IDatabaseInitializer initializer)
	{
		initializer.Initialize();
		SecurityEventSchemaMigration.Migrate(factory);
		SecurityEventSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var version = await access.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='SecurityEvents';", CancellationToken.None);
		Assert.Equal(3, Convert.ToInt32(version, System.Globalization.CultureInfo.InvariantCulture));

		var transactions = new DatabaseTransactionRunner(access);
		var repository = new SecurityEventDeliveryRepository(access);
		var nonce = Guid.NewGuid().ToString("N");
		var now = DateTime.UtcNow;
		var target = new SecurityEventExportTarget
		{
			Code = $"PROVIDER_{nonce}",
			SinkCode = HttpJsonSecurityEventExportSinkFactory.Code,
			EndpointUri = $"https://security.example.test/{nonce}",
			MinimumSeverity = SecurityEventSeverity.Warning,
			EventTypes = [SecurityEventType.AuthenticationFailed, SecurityEventType.AuthenticationBlocked],
			BatchSize = 125,
			IsEnabled = true,
			CreatedUtc = now,
			UpdatedUtc = now,
			Version = 1
		};
		var hash = SecurityEventExportService.ComputeFilterSha256(target.Filter);
		var id = await transactions.ExecuteAsync((transaction, token) => SecurityEventDeliveryRepository.CreateAsync(transaction, target, hash, token), CancellationToken.None);
		var status = await repository.GetStatusAsync(id, CancellationToken.None);

		Assert.NotNull(status);
		Assert.Equal(target.Code, status!.Target.Code);
		Assert.Equal(125, status.Target.BatchSize);
		Assert.Equal(hash, status.State.FilterSha256);
		Assert.Equal(0, status.State.LastEventId);
		Assert.False(status.State.IsSuspended);
	}
}
