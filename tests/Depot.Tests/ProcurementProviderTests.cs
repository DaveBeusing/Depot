// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class ProcurementProviderTests
{
	[SqlServerProcurementFact]
	public async Task SqlServerExecutesAtomicConcurrentGoodsReceiptContract()
	{
		var settings = ProcurementProviderConfiguration.GetSqlServerSettings();
		var factory = new SqlServerConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new SqlServerDatabase(factory));
	}

	[MySqlProcurementFact]
	public async Task MySqlOrMariaDbExecutesAtomicConcurrentGoodsReceiptContract()
	{
		var settings = ProcurementProviderConfiguration.GetMySqlSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyProviderContractAsync(
		IDatabaseConnectionFactory factory,
		IDatabaseInitializer initializer)
	{
		await using var context = await ProcurementTestContext.CreateServerAsync(factory, initializer);
		var order = await context.Orders.SaveDraftAsync(context.NewOrder(quantity: 5));
		Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
		order = await context.Orders.MarkOrderedAsync(order.Id, order.Version);

		var results = await Task.WhenAll(AttemptAsync("A"), AttemptAsync("B"));

		Assert.Single(results, result => result);
		var updated = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.PartiallyReceived, updated.Status);
		Assert.Equal(4, updated.Lines[0].ReceivedQuantity);
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE InventoryId = $InventoryId;",
			new DatabaseParameter("$InventoryId", context.InventoryId)));
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'GoodsReceipt' AND EntityId IN (SELECT Id FROM GoodsReceipts WHERE PurchaseOrderId = $PurchaseOrderId);",
			new DatabaseParameter("$PurchaseOrderId", order.Id)));

		var orderCountBeforeAuditFailure = await context.ScalarAsync(
			"SELECT COUNT(*) FROM PurchaseOrders WHERE SupplierId = $SupplierId;",
			new DatabaseParameter("$SupplierId", context.SupplierId));
		var auditCountBeforeAuditFailure = await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId IN (SELECT Id FROM PurchaseOrders WHERE SupplierId = $SupplierId);",
			new DatabaseParameter("$SupplierId", context.SupplierId));
		context.Authorization.SignIn(new User
		{
			Id = long.MaxValue,
			Email = "missing-provider-audit-user@depot.test",
			DisplayName = "Missing provider audit user",
			IsActive = true
		});
		var auditFailure = await Record.ExceptionAsync(() =>
			context.Orders.SaveDraftAsync(context.NewOrder(quantity: 1)));
		Assert.NotNull(auditFailure);
		Assert.Equal(orderCountBeforeAuditFailure, await context.ScalarAsync(
			"SELECT COUNT(*) FROM PurchaseOrders WHERE SupplierId = $SupplierId;",
			new DatabaseParameter("$SupplierId", context.SupplierId)));
		Assert.Equal(auditCountBeforeAuditFailure, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId IN (SELECT Id FROM PurchaseOrders WHERE SupplierId = $SupplierId);",
			new DatabaseParameter("$SupplierId", context.SupplierId)));

		async Task<bool> AttemptAsync(string suffix)
		{
			try
			{
				var receipt = context.NewReceipt(order, 4);
				receipt.InvoiceNumber = $"INV-{factory.Provider}-{suffix}-{Guid.NewGuid():N}";
				await context.Receipts.PostAsync(receipt);
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}
	}
}

public sealed class SqlServerProcurementFactAttribute : FactAttribute
{
	public SqlServerProcurementFactAttribute()
	{
		Skip = ProcurementProviderConfiguration.GetSqlServerSkipReason();
	}
}

public sealed class MySqlProcurementFactAttribute : FactAttribute
{
	public MySqlProcurementFactAttribute()
	{
		Skip = ProcurementProviderConfiguration.GetMySqlSkipReason();
	}
}

internal static class ProcurementProviderConfiguration
{
	internal const string SqlServerEnvironmentVariable = "DEPOT_TEST_SQLSERVER_CONNECTION_STRING";
	internal const string MySqlEnvironmentVariable = "DEPOT_TEST_MYSQL_CONNECTION_STRING";

	public static string? GetSqlServerSkipReason() => GetSkipReason(SqlServerEnvironmentVariable, GetSqlServerSettings);

	public static string? GetMySqlSkipReason() => GetSkipReason(MySqlEnvironmentVariable, GetMySqlSettings);

	public static DatabaseConnectionSettings GetSqlServerSettings()
	{
		var values = ReadConnectionString(SqlServerEnvironmentVariable);
		var (host, port) = ParseEndpoint(GetRequired(values, "Data Source", "Server"), 1433, ',');
		var database = GetRequired(values, "Initial Catalog", "Database");
		EnsureTestDatabase(database, SqlServerEnvironmentVariable);
		return new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.SqlServer,
			SqlServerHost = host,
			SqlServerPort = port,
			SqlServerDatabase = database,
			SqlServerUserName = GetRequired(values, "User ID", "UID"),
			SqlServerPassword = GetRequired(values, "Password", "PWD"),
			EncryptSqlServerConnection = GetBoolean(values, true, "Encrypt"),
			TrustSqlServerCertificate = GetBoolean(values, false, "Trust Server Certificate", "TrustServerCertificate")
		};
	}

	public static DatabaseConnectionSettings GetMySqlSettings()
	{
		var values = ReadConnectionString(MySqlEnvironmentVariable);
		var (host, endpointPort) = ParseEndpoint(GetRequired(values, "Server", "Host"), 3306, ':');
		var port = GetInt32(values, endpointPort, "Port");
		var database = GetRequired(values, "Database", "Initial Catalog");
		EnsureTestDatabase(database, MySqlEnvironmentVariable);
		var sslMode = GetOptional(values, "SSL Mode", "SslMode");
		return new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.MySql,
			MySqlHost = host,
			MySqlPort = port,
			MySqlDatabase = database,
			MySqlUserName = GetRequired(values, "User ID", "UID", "User"),
			MySqlPassword = GetRequired(values, "Password", "PWD"),
			UseMySqlTls = sslMode is null || !sslMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
		};
	}

	private static string? GetSkipReason(
		string environmentVariable,
		Func<DatabaseConnectionSettings> parse)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
			return $"Set {environmentVariable} to run this optional server integration test.";
		try
		{
			_ = parse();
			return null;
		}
		catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
		{
			return $"{environmentVariable} is invalid: {exception.Message}";
		}
	}

	private static DbConnectionStringBuilder ReadConnectionString(string environmentVariable)
	{
		var connectionString = Environment.GetEnvironmentVariable(environmentVariable);
		if (string.IsNullOrWhiteSpace(connectionString))
			throw new InvalidOperationException($"{environmentVariable} is not configured.");
		return new DbConnectionStringBuilder { ConnectionString = connectionString };
	}

	private static string GetRequired(DbConnectionStringBuilder values, params string[] keys) =>
		GetOptional(values, keys) ?? throw new InvalidOperationException($"Connection string key '{keys[0]}' is required.");

	private static string? GetOptional(DbConnectionStringBuilder values, params string[] keys)
	{
		foreach (var key in keys)
		{
			if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
				return Convert.ToString(value, CultureInfo.InvariantCulture);
		}
		return null;
	}

	private static bool GetBoolean(DbConnectionStringBuilder values, bool defaultValue, params string[] keys)
	{
		var value = GetOptional(values, keys);
		return value is null ? defaultValue : bool.Parse(value);
	}

	private static int GetInt32(DbConnectionStringBuilder values, int defaultValue, params string[] keys)
	{
		var value = GetOptional(values, keys);
		return value is null ? defaultValue : int.Parse(value, CultureInfo.InvariantCulture);
	}

	private static (string Host, int Port) ParseEndpoint(string endpoint, int defaultPort, char separator)
	{
		var normalized = endpoint.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? endpoint[4..] : endpoint;
		var separatorIndex = normalized.LastIndexOf(separator);
		if (separatorIndex > 0 && int.TryParse(normalized[(separatorIndex + 1)..], CultureInfo.InvariantCulture, out var port))
			return (normalized[..separatorIndex], port);
		return (normalized, defaultPort);
	}

	private static void EnsureTestDatabase(string database, string environmentVariable)
	{
		if (!database.Contains("test", StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException($"The database configured by {environmentVariable} must contain 'test' in its name.");
	}
}
