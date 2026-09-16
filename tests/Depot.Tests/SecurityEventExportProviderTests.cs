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
public sealed class SecurityEventExportProviderTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqliteExportsSecurityEventsByStableCursor()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-security-export-provider-{Guid.NewGuid():N}.db");
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
	public async Task SqlServerExportsSecurityEventsByStableCursor()
	{
		var settings = ProcurementProviderConfiguration.GetSqlServerSettings();
		var factory = new SqlServerConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new SqlServerDatabase(factory));
	}

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public async Task MariaDbExportsSecurityEventsByStableCursor()
	{
		var settings = ProcurementProviderConfiguration.GetMariaDbSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public async Task MySqlExportsSecurityEventsByStableCursor()
	{
		var settings = ProcurementProviderConfiguration.GetMySqlSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyProviderContractAsync(IDatabaseConnectionFactory factory, IDatabaseInitializer initializer)
	{
		initializer.Initialize();
		SecurityEventSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var events = new SecurityEventRepository(access);
		var exportRepository = new SecurityEventExportRepository(access);
		var export = new SecurityEventExportService(exportRepository);
		var filter = new SecurityEventExportFilter
		{
			MinimumSeverity = SecurityEventSeverity.Critical,
			EventTypes = [SecurityEventType.AuthenticationBlocked]
		};
		var fingerprint = SecurityEventExportService.ComputeFilterSha256(filter);
		var checkpoint = new SecurityEventExportCheckpoint(await exportRepository.GetLatestIdAsync(CancellationToken.None), fingerprint);
		var nonce = Guid.NewGuid().ToString("N");
		var expectedId = await events.CreateAsync(new SecurityEvent
		{
			TimestampUtc = DateTime.UtcNow,
			EventType = SecurityEventType.AuthenticationBlocked,
			Severity = SecurityEventSeverity.Critical,
			AccountIdentifier = $"provider-{nonce}@test.local",
			Summary = $"Provider export {nonce}",
			Details = "Database-provider export acceptance evidence."
		}, CancellationToken.None);

		var batch = await export.ReadBatchAsync(filter, checkpoint, 10, CancellationToken.None);

		var record = Assert.Single(batch.Events);
		Assert.Equal(expectedId, record.EventId);
		Assert.Equal(SecurityEventType.AuthenticationBlocked, record.EventType);
		Assert.Equal(SecurityEventSeverity.Critical, record.Severity);
		Assert.Equal(expectedId, batch.NextCheckpoint.LastEventId);
		Assert.False(batch.HasMore);
	}
}
