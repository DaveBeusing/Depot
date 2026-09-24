// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
public sealed class ProcurementProviderTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public async Task SqlServerExecutesAtomicConcurrentGoodsReceiptContract()
	{
		var settings = ProcurementProviderConfiguration.GetSqlServerSettings();
		var factory = new SqlServerConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new SqlServerDatabase(factory));
	}

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public async Task MariaDbExecutesAtomicConcurrentGoodsReceiptContract()
	{
		var settings = ProcurementProviderConfiguration.GetMariaDbSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public async Task MySqlExecutesAtomicConcurrentGoodsReceiptContract()
	{
		var settings = ProcurementProviderConfiguration.GetMySqlSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyProviderContractAsync(IDatabaseConnectionFactory factory, IDatabaseInitializer initializer)
	{
		await using var context = await ProcurementTestContext.CreateServerAsync(factory, initializer);
		try
		{
			var order = await context.Orders.SaveDraftAsync(context.NewOrder(quantity: 5));
			Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
			order = await context.ApproveAndOrderAsync(order);

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

			var receiptOption = (await context.SupplierReturns.SearchReceiptOptionsAsync(null, context.SupplierId))
				.Single(option => option.PurchaseOrderId == order.Id);
			var returnableLine = Assert.Single(await context.SupplierReturns.GetReturnableLinesAsync(receiptOption.GoodsReceiptId));
			var reasonCodeId = Convert.ToInt64(await context.Data.ExecuteScalarAsync(
				"SELECT Id FROM ReasonCodes WHERE Code = $Code;",
				CancellationToken.None,
				new DatabaseParameter("$Code", ReasonCodeSystemCodes.Returned)));
			var supplierReturn = await context.SupplierReturns.SaveDraftAsync(new SupplierReturn
			{
				SupplierId = context.SupplierId,
				PurchaseOrderId = order.Id,
				GoodsReceiptId = receiptOption.GoodsReceiptId,
				ReturnDate = DateTime.Today,
				Notes = "Provider contract supplier return",
				Lines =
				[
					new SupplierReturnLine
					{
						GoodsReceiptLineId = returnableLine.GoodsReceiptLineId,
						InventoryId = returnableLine.InventoryId,
						ItemId = returnableLine.ItemId,
						Quantity = 1,
						ReasonCodeId = reasonCodeId
					}
				]
			});
			await context.SupplierReturns.PostSupplierReturnAsync(supplierReturn.Id, supplierReturn.Version);
			Assert.Equal(1, await context.ScalarAsync(
				"SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference AND MovementType = $MovementType AND Quantity = -1;",
				new DatabaseParameter("$Reference", $"Supplier Return {supplierReturn.ReturnNumber}"),
				new DatabaseParameter("$MovementType", (int)StockMovementType.SupplierReturn)));

			await VerifySourcingRoundTripAsync(context);

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
			}, PermissionCatalog.All);
			var auditFailure = await Record.ExceptionAsync(() => context.Orders.SaveDraftAsync(context.NewOrder(quantity: 1)));
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
					receipt.SupplierDeliveryNoteNumber = $"DN-{factory.Provider}-{suffix}-{Guid.NewGuid():N}";
					await context.Receipts.PostGoodsReceiptAsync(receipt);
					return true;
				}
				catch (InvalidOperationException)
				{
					return false;
				}
			}
		}
		finally
		{
			await RemoveNotificationReferencesAsync(context);
		}
	}

	private static async Task VerifySourcingRoundTripAsync(ProcurementTestContext context)
	{
		long? requisitionId = null;
		long? rfqId = null;
		try
		{
			var requisition = await context.Sourcing.SaveRequisitionAsync(new PurchaseRequisition
			{
				BusinessJustification = "Provider sourcing acceptance",
				RequiredByDate = DateTime.Today.AddDays(8),
				Lines = [new PurchaseRequisitionLine { ItemId = context.SecondItemId, Quantity = 4 }]
			});
			requisitionId = requisition.Id;
			var submitted = await context.Sourcing.SubmitAsync(requisition.Id, requisition.Version);
			context.SignInApprover();
			var approved = await context.Sourcing.ApproveAsync(submitted.Id, submitted.Version, "Provider acceptance approval");
			context.SignInAdministrator();

			var rfq = await context.Sourcing.CreateRfqAsync(approved.Id, [context.SupplierId], DateTime.Today.AddDays(3));
			rfqId = rfq.Id;
			var details = await context.Sourcing.GetRfqAsync(rfq.Id) ?? throw new InvalidOperationException("Provider RFQ could not be reloaded.");
			var line = Assert.Single(details.Lines);
			var quote = await context.Sourcing.CaptureQuoteResponseAsync(new SupplierQuoteResponse
			{
				RequestForQuotationId = rfq.Id,
				SupplierId = context.SupplierId,
				SupplierReference = $"PROVIDER-{Guid.NewGuid():N}",
				Currency = "EUR",
				ValidUntil = DateTime.Today.AddDays(10),
				Lines =
				[
					new SupplierQuoteResponseLine
					{
						RequestForQuotationLineId = line.Id,
						ItemId = line.ItemId,
						Quantity = line.Quantity,
						UnitPrice = 21.75m,
						MinimumOrderQuantity = 4,
						LeadTimeDays = 6
					}
				]
			});
			var comparison = await context.Sourcing.CompareAsync(rfq.Id);
			var compared = Assert.Single(comparison);
			Assert.Equal(quote.Id, compared.SupplierQuoteResponseId);
			Assert.Equal(21.75m, compared.UnitPrice);
			Assert.False(compared.IsSelected);

			var awarded = await context.Sourcing.SelectQuoteAsync(rfq.Id, details.Version, quote.Id);
			Assert.Equal(RequestForQuotationStatus.Awarded, awarded.Status);
			var purchaseOrderId = await context.Sourcing.ConvertSelectedQuoteToPurchaseOrderAsync(rfq.Id);
			Assert.Equal(purchaseOrderId, await context.Sourcing.ConvertSelectedQuoteToPurchaseOrderAsync(rfq.Id));
			var evidence = await context.Sourcing.GetEvidenceByPurchaseOrderAsync(purchaseOrderId);
			Assert.NotNull(evidence);
			Assert.Equal(requisition.Id, evidence!.PurchaseRequisitionId);
			Assert.Equal(rfq.Id, evidence.RequestForQuotationId);
			Assert.Equal(quote.Id, evidence.SupplierQuoteResponseId);
		}
		finally
		{
			context.SignInAdministrator();
			if (requisitionId is { } reqId)
			{
				await context.Data.ExecuteAsync("DELETE FROM ProcurementSourcingEvidence WHERE PurchaseRequisitionId=$Id;", CancellationToken.None, new DatabaseParameter("$Id", reqId));
			}
			if (rfqId is { } requestId)
			{
				await context.Data.ExecuteAsync("DELETE FROM SupplierQuoteResponseLines WHERE SupplierQuoteResponseId IN (SELECT Id FROM SupplierQuoteResponses WHERE RequestForQuotationId=$Id);", CancellationToken.None, new DatabaseParameter("$Id", requestId));
				await context.Data.ExecuteAsync("DELETE FROM SupplierQuoteResponses WHERE RequestForQuotationId=$Id;", CancellationToken.None, new DatabaseParameter("$Id", requestId));
				await context.Data.ExecuteAsync("DELETE FROM RequestForQuotationSuppliers WHERE RequestForQuotationId=$Id;", CancellationToken.None, new DatabaseParameter("$Id", requestId));
				await context.Data.ExecuteAsync("DELETE FROM RequestForQuotationLines WHERE RequestForQuotationId=$Id;", CancellationToken.None, new DatabaseParameter("$Id", requestId));
				await context.Data.ExecuteAsync("DELETE FROM RequestsForQuotation WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", requestId));
			}
			if (requisitionId is { } requisition)
			{
				await context.Data.ExecuteAsync("DELETE FROM PurchaseRequisitionLines WHERE PurchaseRequisitionId=$Id;", CancellationToken.None, new DatabaseParameter("$Id", requisition));
				await context.Data.ExecuteAsync("DELETE FROM PurchaseRequisitions WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", requisition));
			}
		}
	}

	private static async Task RemoveNotificationReferencesAsync(ProcurementTestContext context)
	{
		var userId = context.ApproverUserId;
		await context.Data.ExecuteAsync(
			"DELETE FROM NotificationRecipients WHERE UserId=$UserId OR NotificationId IN (SELECT Id FROM Notifications WHERE CreatedByUserId=$UserId);",
			CancellationToken.None,
			new DatabaseParameter("$UserId", userId));
		await context.Data.ExecuteAsync(
			"DELETE FROM Notifications WHERE CreatedByUserId=$UserId;",
			CancellationToken.None,
			new DatabaseParameter("$UserId", userId));
	}
}

public sealed class SqlServerProcurementFactAttribute : FactAttribute
{
	public SqlServerProcurementFactAttribute() => Skip = ProcurementProviderConfiguration.GetSqlServerSkipReason();
}

public sealed class MariaDbProcurementFactAttribute : FactAttribute
{
	public MariaDbProcurementFactAttribute() => Skip = ProcurementProviderConfiguration.GetMariaDbSkipReason();
}

public sealed class MySqlProcurementFactAttribute : FactAttribute
{
	public MySqlProcurementFactAttribute() => Skip = ProcurementProviderConfiguration.GetMySqlSkipReason();
}

internal static class ProcurementProviderConfiguration
{
	internal const string SqlServerEnvironmentVariable = "DEPOT_TEST_SQLSERVER_CONNECTION_STRING";
	internal const string MariaDbEnvironmentVariable = "DEPOT_TEST_MARIADB_CONNECTION_STRING";
	internal const string MySqlEnvironmentVariable = "DEPOT_TEST_MYSQL_CONNECTION_STRING";

	public static string? GetSqlServerSkipReason() => GetSkipReason(SqlServerEnvironmentVariable, GetSqlServerSettings);
	public static string? GetMariaDbSkipReason() => GetSkipReason(MariaDbEnvironmentVariable, GetMariaDbSettings);
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

	public static DatabaseConnectionSettings GetMariaDbSettings() => GetMySqlFamilySettings(MariaDbEnvironmentVariable);
	public static DatabaseConnectionSettings GetMySqlSettings() => GetMySqlFamilySettings(MySqlEnvironmentVariable);

	private static DatabaseConnectionSettings GetMySqlFamilySettings(string environmentVariable)
	{
		var values = ReadConnectionString(environmentVariable);
		var (host, endpointPort) = ParseEndpoint(GetRequired(values, "Server", "Host"), 3306, ':');
		var port = GetInt32(values, endpointPort, "Port");
		var database = GetRequired(values, "Database", "Initial Catalog");
		EnsureTestDatabase(database, environmentVariable);
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

	private static string? GetSkipReason(string environmentVariable, Func<DatabaseConnectionSettings> parse)
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
