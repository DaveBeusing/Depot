// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using Microsoft.Data.Sqlite;

using Xunit;
using Xunit.Abstractions;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
public sealed class ProviderProductionAcceptanceTests
{
	private readonly ITestOutputHelper _output;

	public ProviderProductionAcceptanceTests(ITestOutputHelper output) => _output = output;

	[Fact]
	[Trait("Provider", "SQLite")]
	[Trait("AcceptanceLevel", "Smoke")]
	public async Task SQLiteProductionContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-provider-acceptance-{Guid.NewGuid():N}.db");
		try
		{
			await VerifyCoreAsync(new SqliteConnectionFactory(path), "SQLite", exerciseDeadlock: false);
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	[Trait("AcceptanceLevel", "Smoke")]
	public Task SqlServerProductionContract() => VerifyCoreAsync(
		new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()), "SQL Server", exerciseDeadlock: true);

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	[Trait("AcceptanceLevel", "Smoke")]
	public Task MariaDbProductionContract() => VerifyCoreAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()), "MariaDB", exerciseDeadlock: true);

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	[Trait("AcceptanceLevel", "Smoke")]
	public Task MySqlProductionContract() => VerifyCoreAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()), "MySQL", exerciseDeadlock: true);

	[Fact]
	[Trait("Provider", "SQLite")]
	[Trait("AcceptanceLevel", "Full")]
	public async Task SQLitePerformanceBaseline()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-provider-performance-{Guid.NewGuid():N}.db");
		try { await VerifyPerformanceAsync(new SqliteConnectionFactory(path), "SQLite"); }
		finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	[Trait("AcceptanceLevel", "Full")]
	public Task SqlServerPerformanceBaseline() => VerifyPerformanceAsync(
		new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()), "SQL Server");

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	[Trait("AcceptanceLevel", "Full")]
	public Task MariaDbPerformanceBaseline() => VerifyPerformanceAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()), "MariaDB");

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	[Trait("AcceptanceLevel", "Full")]
	public Task MySqlPerformanceBaseline() => VerifyPerformanceAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()), "MySQL");

	private async Task VerifyCoreAsync(IDatabaseConnectionFactory factory, string providerName, bool exerciseDeadlock)
	{
		DatabaseProvisioningService.Initialize(factory);
		DatabaseProvisioningService.Initialize(factory);
		var data = new DatabaseAccess(factory);

		Assert.Equal(DatabaseVersion.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DatabaseInfo;", CancellationToken.None), CultureInfo.InvariantCulture));
		Assert.Equal(SalesSchemaMigration.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';", CancellationToken.None), CultureInfo.InvariantCulture));
		Assert.Equal(FinanceInventoryAccountingSchemaMigration.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';", CancellationToken.None), CultureInfo.InvariantCulture));
		Assert.True(Convert.ToInt32(await data.ExecuteScalarAsync(ReservationIndexSql(factory.Provider), CancellationToken.None), CultureInfo.InvariantCulture) > 0);

		var serverVersion = Convert.ToString(await data.ExecuteScalarAsync(ServerVersionSql(factory.Provider), CancellationToken.None), CultureInfo.InvariantCulture);
		Assert.False(string.IsNullOrWhiteSpace(serverVersion));
		_output.WriteLine($"Provider={providerName}; ServerVersion={serverVersion}; DatabaseSchema={DatabaseVersion.CurrentVersion}; SalesSchema={SalesSchemaMigration.CurrentVersion}; FinanceSchema={FinanceInventoryAccountingSchemaMigration.CurrentVersion}");

		await CreateProbeAsync(data, factory.Provider);
		await VerifyRoundTripsAndConstraintsAsync(data);
		await VerifyRollbackAndRetryBoundaryAsync(data);
		await VerifyConcurrentMutationAsync(data);
		if (exerciseDeadlock) await VerifyDeadlockRetryAsync(data);
		await VerifySessionPersistenceAsync(data, providerName);
		await VerifyConnectionRecoversAfterFailureAsync(factory);
	}

	private static async Task CreateProbeAsync(DatabaseAccess data, DatabaseProvider provider)
	{
		foreach (var statement in ProbeStatements(provider))
			await data.ExecuteAsync(statement, CancellationToken.None);
	}

	private static async Task VerifyRoundTripsAndConstraintsAsync(DatabaseAccess data)
	{
		var id = Guid.NewGuid();
		var amount = 123456789.123456789m;
		var occurred = new DateTime(2026, 9, 7, 18, 19, 20, 123, DateTimeKind.Utc).AddTicks(4560);
		var businessDate = new DateTime(2026, 9, 7);
		await data.ExecuteAsync(
			"INSERT INTO ProviderAcceptanceProbe (Id,Amount,OccurredAt,BusinessDate,NullableValue,UniqueValue) VALUES ($Id,$Amount,$OccurredAt,$BusinessDate,$NullableValue,$UniqueValue);",
			CancellationToken.None,
			new DatabaseParameter("$Id", id.ToString("D")),
			new DatabaseParameter("$Amount", amount),
			new DatabaseParameter("$OccurredAt", occurred),
			new DatabaseParameter("$BusinessDate", businessDate),
			new DatabaseParameter("$NullableValue", null),
			new DatabaseParameter("$UniqueValue", "unique-1"));

		Assert.Equal(amount, Convert.ToDecimal(await data.ExecuteScalarAsync("SELECT Amount FROM ProviderAcceptanceProbe WHERE UniqueValue='unique-1';", CancellationToken.None), CultureInfo.InvariantCulture));
		var roundTripId = await data.ExecuteScalarAsync("SELECT Id FROM ProviderAcceptanceProbe WHERE UniqueValue='unique-1';", CancellationToken.None);
		Assert.Equal(id, Guid.Parse(Convert.ToString(roundTripId, CultureInfo.InvariantCulture)!));
		var roundTripOccurred = ToDateTime(await data.ExecuteScalarAsync("SELECT OccurredAt FROM ProviderAcceptanceProbe WHERE UniqueValue='unique-1';", CancellationToken.None));
		Assert.True((roundTripOccurred - occurred).Duration() <= TimeSpan.FromMilliseconds(1));
		Assert.Equal(businessDate.Date, ToDateTime(await data.ExecuteScalarAsync("SELECT BusinessDate FROM ProviderAcceptanceProbe WHERE UniqueValue='unique-1';", CancellationToken.None)).Date);
		Assert.Equal(1, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM ProviderAcceptanceProbe WHERE NullableValue IS NULL;", CancellationToken.None), CultureInfo.InvariantCulture));

		var duplicate = await Record.ExceptionAsync(() => data.ExecuteAsync(
			"INSERT INTO ProviderAcceptanceProbe (Id,Amount,OccurredAt,BusinessDate,NullableValue,UniqueValue) VALUES ($Id,1,$OccurredAt,$BusinessDate,NULL,'unique-1');",
			CancellationToken.None,
			new DatabaseParameter("$Id", Guid.NewGuid().ToString("D")),
			new DatabaseParameter("$OccurredAt", occurred),
			new DatabaseParameter("$BusinessDate", businessDate)));
		Assert.NotNull(duplicate);

		var foreignKey = await Record.ExceptionAsync(() => data.ExecuteAsync(
			"INSERT INTO ProviderAcceptanceProbeChild (Id,ProbeId) VALUES (1,$ProbeId);",
			CancellationToken.None,
			new DatabaseParameter("$ProbeId", Guid.NewGuid().ToString("D"))));
		Assert.NotNull(foreignKey);
	}

	private static async Task VerifyRollbackAndRetryBoundaryAsync(DatabaseAccess data)
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() => data.ExecuteInWriteTransactionAsync<int>(
			async (session, token) =>
			{
				await session.ExecuteAsync("INSERT INTO ProviderAcceptanceCounter (Id,Value) VALUES (99,1);", token);
				throw new InvalidOperationException("Injected rollback probe.");
			}, CancellationToken.None));
		Assert.Equal(0, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM ProviderAcceptanceCounter WHERE Id=99;", CancellationToken.None), CultureInfo.InvariantCulture));

		await data.ExecuteAsync("INSERT INTO ProviderAcceptanceUnique (Value) VALUES ('fixed');", CancellationToken.None);
		var attempts = 0;
		await Assert.ThrowsAnyAsync<Exception>(() => data.ExecuteInWriteTransactionAsync<int>(
			async (session, token) =>
			{
				Interlocked.Increment(ref attempts);
				await session.ExecuteAsync("INSERT INTO ProviderAcceptanceUnique (Value) VALUES ('fixed');", token);
				return 0;
			}, CancellationToken.None));
		Assert.Equal(1, attempts);
	}

	private static async Task VerifyConcurrentMutationAsync(DatabaseAccess data)
	{
		await data.ExecuteAsync("INSERT INTO ProviderAcceptanceCounter (Id,Value) VALUES (1,0);", CancellationToken.None);
		var tasks = Enumerable.Range(0, 8).Select(_ => data.ExecuteInWriteTransactionAsync<int>(
			async (session, token) =>
			{
				await session.ExecuteAsync("UPDATE ProviderAcceptanceCounter SET Value=Value+1 WHERE Id=1;", token);
				return 0;
			}, CancellationToken.None));
		await Task.WhenAll(tasks);
		Assert.Equal(8, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Value FROM ProviderAcceptanceCounter WHERE Id=1;", CancellationToken.None), CultureInfo.InvariantCulture));
	}

	private static async Task VerifyDeadlockRetryAsync(DatabaseAccess data)
	{
		await data.ExecuteAsync("INSERT INTO ProviderAcceptanceCounter (Id,Value) VALUES (10,0);", CancellationToken.None);
		await data.ExecuteAsync("INSERT INTO ProviderAcceptanceCounter (Id,Value) VALUES (11,0);", CancellationToken.None);
		using var barrier = new Barrier(2);
		var attemptsA = 0;
		var attemptsB = 0;

		async Task RunAsync(int first, int second, Action countAttempt, Func<int> readAttempts)
		{
			await data.ExecuteInWriteTransactionAsync<int>(async (session, token) =>
			{
				countAttempt();
				await session.ExecuteAsync($"UPDATE ProviderAcceptanceCounter SET Value=Value+1 WHERE Id={first};", token);
				if (readAttempts() == 1 && !barrier.SignalAndWait(TimeSpan.FromSeconds(15)))
					throw new TimeoutException("Deadlock acceptance barrier timed out.");
				await session.ExecuteAsync($"UPDATE ProviderAcceptanceCounter SET Value=Value+1 WHERE Id={second};", token);
				return 0;
			}, CancellationToken.None);
		}

		await Task.WhenAll(
			RunAsync(10, 11, () => Interlocked.Increment(ref attemptsA), () => Volatile.Read(ref attemptsA)),
			RunAsync(11, 10, () => Interlocked.Increment(ref attemptsB), () => Volatile.Read(ref attemptsB)));

		Assert.True(attemptsA + attemptsB >= 3, "The provider did not expose a deadlock victim/retry during the controlled lock inversion.");
		Assert.Equal(2, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Value FROM ProviderAcceptanceCounter WHERE Id=10;", CancellationToken.None), CultureInfo.InvariantCulture));
		Assert.Equal(2, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Value FROM ProviderAcceptanceCounter WHERE Id=11;", CancellationToken.None), CultureInfo.InvariantCulture));
	}

	private static async Task VerifySessionPersistenceAsync(DatabaseAccess data, string providerName)
	{
		var users = new UserRepository(data);
		var sessions = new UserSessionRepository(data);
		var now = new DateTime(2026, 9, 7, 18, 0, 0, DateTimeKind.Utc);
		var userId = await users.CreateAsync(new User
		{
			Email = $"provider-session-{Guid.NewGuid():N}@depot.test",
			DisplayName = $"{providerName} acceptance",
			IsActive = true,
			CreatedUtc = now
		}, "unused", CancellationToken.None);
		var session = new UserSession
		{
			SessionId = Guid.NewGuid(), UserId = userId, StartedUtc = now, LastSeenUtc = now,
			ClientInstanceId = Guid.NewGuid(), MachineName = "ACCEPTANCE", AppVersion = "provider-acceptance"
		};
		await sessions.CreateAsync(session, CancellationToken.None);
		Assert.NotNull(await sessions.GetBySessionIdAsync(session.SessionId, CancellationToken.None));
		Assert.True(await sessions.UpdateHeartbeatAsync(session.SessionId, now.AddSeconds(15), CancellationToken.None));
		Assert.True(await sessions.EndAsync(session.SessionId, now.AddSeconds(30), UserSessionEndReason.LoggedOut, CancellationToken.None));
		Assert.False(await sessions.UpdateHeartbeatAsync(session.SessionId, now.AddMinutes(1), CancellationToken.None));
	}

	private static async Task VerifyConnectionRecoversAfterFailureAsync(IDatabaseConnectionFactory goodFactory)
	{
		IDatabaseConnectionFactory badFactory = goodFactory switch
		{
			SqlServerConnectionFactory => new SqlServerConnectionFactory(BadSqlServerSettings()),
			MySqlConnectionFactory => new MySqlConnectionFactory(BadMySqlSettings()),
			_ => goodFactory
		};
		if (!ReferenceEquals(badFactory, goodFactory))
		{
			await using var bad = badFactory.CreateConnection();
			await Assert.ThrowsAnyAsync<Exception>(() => bad.OpenAsync(CancellationToken.None));
		}
		await using var good = goodFactory.CreateConnection();
		await good.OpenAsync(CancellationToken.None);
		using var command = good.CreateCommand();
		command.CommandText = "SELECT 1;";
		Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture));
	}

	private async Task VerifyPerformanceAsync(IDatabaseConnectionFactory factory, string providerName)
	{
		DatabaseProvisioningService.Initialize(factory);
		var data = new DatabaseAccess(factory);
		foreach (var statement in PerformanceStatements(factory.Provider)) await data.ExecuteAsync(statement, CancellationToken.None);
		var count = Convert.ToInt32(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM ProviderAcceptancePerformance;", CancellationToken.None), CultureInfo.InvariantCulture);
		Assert.Equal(100000, count);
		var stopwatch = Stopwatch.StartNew();
		var value = await data.ExecuteScalarAsync("SELECT Id FROM ProviderAcceptancePerformance WHERE PartNumber='PART-099999';", CancellationToken.None);
		stopwatch.Stop();
		_output.WriteLine($"Provider={providerName}; Records=100000; IndexedLookupMs={stopwatch.Elapsed.TotalMilliseconds:F1}");
		Assert.Equal(99999, Convert.ToInt32(value, CultureInfo.InvariantCulture));
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Indexed 100k lookup took {stopwatch.Elapsed.TotalSeconds:F2}s.");
	}

	private static DateTime ToDateTime(object? value) => value switch
	{
		DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
		_ => DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
	};

	private static string ServerVersionSql(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => "SELECT sqlite_version();",
		DatabaseProvider.SqlServer => "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));",
		DatabaseProvider.MySql => "SELECT VERSION();",
		_ => throw new NotSupportedException()
	};

	private static string ReservationIndexSql(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_InventoryReservations_Active';",
		DatabaseProvider.SqlServer => "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'InventoryReservations') AND name=N'UX_InventoryReservations_Active';",
		DatabaseProvider.MySql => "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='InventoryReservations' AND index_name='UX_InventoryReservations_Active';",
		_ => throw new NotSupportedException()
	};

	private static IReadOnlyList<string> ProbeStatements(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local =>
		[
			"DROP TABLE IF EXISTS ProviderAcceptanceProbeChild;", "DROP TABLE IF EXISTS ProviderAcceptanceProbe;", "DROP TABLE IF EXISTS ProviderAcceptanceCounter;", "DROP TABLE IF EXISTS ProviderAcceptanceUnique;",
			"CREATE TABLE ProviderAcceptanceProbe (Id TEXT PRIMARY KEY, Amount NUMERIC NOT NULL, OccurredAt TEXT NOT NULL, BusinessDate TEXT NOT NULL, NullableValue TEXT NULL, UniqueValue TEXT NOT NULL UNIQUE);",
			"CREATE TABLE ProviderAcceptanceProbeChild (Id INTEGER PRIMARY KEY, ProbeId TEXT NOT NULL REFERENCES ProviderAcceptanceProbe(Id));",
			"CREATE TABLE ProviderAcceptanceCounter (Id INTEGER PRIMARY KEY, Value INTEGER NOT NULL);",
			"CREATE TABLE ProviderAcceptanceUnique (Id INTEGER PRIMARY KEY AUTOINCREMENT, Value TEXT NOT NULL UNIQUE);"
		],
		DatabaseProvider.SqlServer =>
		[
			"IF OBJECT_ID(N'ProviderAcceptanceProbeChild',N'U') IS NOT NULL DROP TABLE ProviderAcceptanceProbeChild; IF OBJECT_ID(N'ProviderAcceptanceProbe',N'U') IS NOT NULL DROP TABLE ProviderAcceptanceProbe; IF OBJECT_ID(N'ProviderAcceptanceCounter',N'U') IS NOT NULL DROP TABLE ProviderAcceptanceCounter; IF OBJECT_ID(N'ProviderAcceptanceUnique',N'U') IS NOT NULL DROP TABLE ProviderAcceptanceUnique;",
			"CREATE TABLE ProviderAcceptanceProbe (Id uniqueidentifier PRIMARY KEY, Amount decimal(28,9) NOT NULL, OccurredAt datetime2(6) NOT NULL, BusinessDate date NOT NULL, NullableValue nvarchar(100) NULL, UniqueValue nvarchar(100) NOT NULL UNIQUE);",
			"CREATE TABLE ProviderAcceptanceProbeChild (Id int PRIMARY KEY, ProbeId uniqueidentifier NOT NULL REFERENCES ProviderAcceptanceProbe(Id));",
			"CREATE TABLE ProviderAcceptanceCounter (Id int PRIMARY KEY, Value int NOT NULL);",
			"CREATE TABLE ProviderAcceptanceUnique (Id bigint IDENTITY(1,1) PRIMARY KEY, Value nvarchar(100) NOT NULL UNIQUE);"
		],
		DatabaseProvider.MySql =>
		[
			"DROP TABLE IF EXISTS ProviderAcceptanceProbeChild;", "DROP TABLE IF EXISTS ProviderAcceptanceProbe;", "DROP TABLE IF EXISTS ProviderAcceptanceCounter;", "DROP TABLE IF EXISTS ProviderAcceptanceUnique;",
			"CREATE TABLE ProviderAcceptanceProbe (Id CHAR(36) PRIMARY KEY, Amount DECIMAL(28,9) NOT NULL, OccurredAt DATETIME(6) NOT NULL, BusinessDate DATE NOT NULL, NullableValue VARCHAR(100) NULL, UniqueValue VARCHAR(100) NOT NULL UNIQUE) ENGINE=InnoDB;",
			"CREATE TABLE ProviderAcceptanceProbeChild (Id INT PRIMARY KEY, ProbeId CHAR(36) NOT NULL, CONSTRAINT FK_ProviderAcceptanceProbeChild FOREIGN KEY(ProbeId) REFERENCES ProviderAcceptanceProbe(Id)) ENGINE=InnoDB;",
			"CREATE TABLE ProviderAcceptanceCounter (Id INT PRIMARY KEY, Value INT NOT NULL) ENGINE=InnoDB;",
			"CREATE TABLE ProviderAcceptanceUnique (Id BIGINT AUTO_INCREMENT PRIMARY KEY, Value VARCHAR(100) NOT NULL UNIQUE) ENGINE=InnoDB;"
		],
		_ => throw new NotSupportedException()
	};

	private static IReadOnlyList<string> PerformanceStatements(DatabaseProvider provider)
	{
		const string digits = "(SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9)";
		return provider switch
		{
			DatabaseProvider.Local =>
			[
				"DROP TABLE IF EXISTS ProviderAcceptancePerformance;",
				"CREATE TABLE ProviderAcceptancePerformance (Id INTEGER PRIMARY KEY, PartNumber TEXT NOT NULL UNIQUE, Amount NUMERIC NOT NULL);",
				$"INSERT INTO ProviderAcceptancePerformance(Id,PartNumber,Amount) SELECT n, printf('PART-%06d',n), n/100.0 FROM (SELECT a.n + b.n*10 + c.n*100 + d.n*1000 + e.n*10000 + 1 AS n FROM {digits} a CROSS JOIN {digits} b CROSS JOIN {digits} c CROSS JOIN {digits} d CROSS JOIN {digits} e);"
			],
			DatabaseProvider.SqlServer =>
			[
				"IF OBJECT_ID(N'ProviderAcceptancePerformance',N'U') IS NOT NULL DROP TABLE ProviderAcceptancePerformance;",
				"CREATE TABLE ProviderAcceptancePerformance (Id int PRIMARY KEY, PartNumber varchar(20) NOT NULL UNIQUE, Amount decimal(18,2) NOT NULL);",
				$"INSERT INTO ProviderAcceptancePerformance(Id,PartNumber,Amount) SELECT n, CONCAT('PART-',RIGHT('000000'+CAST(n AS varchar(6)),6)), CAST(n AS decimal(18,2))/100 FROM (SELECT a.n + b.n*10 + c.n*100 + d.n*1000 + e.n*10000 + 1 AS n FROM {digits} a CROSS JOIN {digits} b CROSS JOIN {digits} c CROSS JOIN {digits} d CROSS JOIN {digits} e) numbers;"
			],
			DatabaseProvider.MySql =>
			[
				"DROP TABLE IF EXISTS ProviderAcceptancePerformance;",
				"CREATE TABLE ProviderAcceptancePerformance (Id INT PRIMARY KEY, PartNumber VARCHAR(20) NOT NULL UNIQUE, Amount DECIMAL(18,2) NOT NULL) ENGINE=InnoDB;",
				$"INSERT INTO ProviderAcceptancePerformance(Id,PartNumber,Amount) SELECT n, CONCAT('PART-',LPAD(n,6,'0')), n/100 FROM (SELECT a.n + b.n*10 + c.n*100 + d.n*1000 + e.n*10000 + 1 AS n FROM {digits} a CROSS JOIN {digits} b CROSS JOIN {digits} c CROSS JOIN {digits} d CROSS JOIN {digits} e) numbers;"
			],
			_ => throw new NotSupportedException()
		};
	}

	private static DatabaseConnectionSettings BadSqlServerSettings() => new()
	{
		Provider = DatabaseProvider.SqlServer, SqlServerHost = "127.0.0.1", SqlServerPort = 1, SqlServerDatabase = "depot_test_unavailable",
		SqlServerUserName = "unavailable", SqlServerPassword = "unavailable", EncryptSqlServerConnection = false, TrustSqlServerCertificate = true
	};

	private static DatabaseConnectionSettings BadMySqlSettings() => new()
	{
		Provider = DatabaseProvider.MySql, MySqlHost = "127.0.0.1", MySqlPort = 1, MySqlDatabase = "depot_test_unavailable",
		MySqlUserName = "unavailable", MySqlPassword = "unavailable", UseMySqlTls = false
	};
}
