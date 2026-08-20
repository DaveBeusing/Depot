// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SalesFeatureTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-sales-{Guid.NewGuid():N}.db");

	[Fact]
	public void SalesMigrationCreatesCurrentSchemaAndIsIdempotent()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		SalesSchemaMigration.Migrate(factory);

		using var connection = new SqliteConnection($"Data Source={_databasePath}");
		connection.Open();
		Assert.Equal((long)SalesSchemaMigration.CurrentVersion, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';"));
		foreach (var table in new[] { "Customers", "CustomerAddresses", "CustomerContacts", "SalesOrders", "SalesOrderLines", "InventoryReservations", "Shipments", "ShipmentLines", "SalesInvoices", "SalesInvoiceLines", "CustomerReturns", "CustomerReturnLines", "SalesCreditNotes", "SalesCreditNoteLines", "SalesPriceLists", "SalesPriceListItems", "CustomerPriceLists", "SalesQuotes", "SalesQuoteLines" })
			Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('Shipments') WHERE name='ReversedAtUtc';"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('Shipments') WHERE name='PackingStatus';"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('Shipments') WHERE name='PackedAtUtc';"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('CustomerAddresses') WHERE name='IsDefault';"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('SalesOrders') WHERE name='BillingAddress';"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('SalesOrders') WHERE name='ShippingAddress';"));
	}

	[Fact]
	public void SalesPermissionsHaveStableCodes()
	{
		Assert.Equal("Shipments.Reverse", PermissionCatalog.Code(ApplicationPermission.ShipmentsReverse));
		Assert.Equal("CustomerReturns.Post", PermissionCatalog.Code(ApplicationPermission.CustomerReturnsPost));
		Assert.Equal("CreditNotes.Post", PermissionCatalog.Code(ApplicationPermission.CreditNotesPost));
		Assert.Equal("SalesQuotes.Convert", PermissionCatalog.Code(ApplicationPermission.SalesQuotesConvert));
		Assert.Equal("SalesPricing.Manage", PermissionCatalog.Code(ApplicationPermission.SalesPricingManage));
	}

	[Fact]
	public void SalesRolesContainCommercialAndCorrectionPermissions()
	{
		var warehouse = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.WarehouseOperatorCode);
		var sales = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.SalesUserCode);
		var salesManager = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.SalesManagerCode);
		var finance = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.FinanceCode);
		Assert.Contains(ApplicationPermission.ShipmentsReverse, warehouse.Permissions);
		Assert.Contains(ApplicationPermission.CustomerReturnsPost, warehouse.Permissions);
		Assert.Contains(ApplicationPermission.SalesQuotesCreate, sales.Permissions);
		Assert.Contains(ApplicationPermission.SalesQuotesConvert, salesManager.Permissions);
		Assert.Contains(ApplicationPermission.SalesPricingManage, salesManager.Permissions);
		Assert.Contains(ApplicationPermission.CreditNotesCreate, finance.Permissions);
		Assert.Contains(ApplicationPermission.CreditNotesPost, finance.Permissions);
	}

	[Fact]
	public void SalesOrderLineCalculatesOpenAndBackorderedQuantities()
	{
		var line = new SalesOrderLine { Quantity = 100, ShippedQuantity = 25, ReservedQuantity = 50 };
		Assert.Equal(75, line.OpenQuantity);
		Assert.Equal(25, line.BackorderedQuantity);
	}

	[Fact]
	public void CreditNoteLineCalculatesAmountsFromSnapshotPricing()
	{
		var line = new SalesCreditNoteLine { Quantity = 2, UnitPrice = 100m, DiscountPercent = 10m, TaxRate = 19m };
		Assert.Equal(180m, line.NetAmount);
		Assert.Equal(34.20m, line.TaxAmount);
		Assert.Equal(214.20m, line.GrossAmount);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}

	private static long Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt64(command.ExecuteScalar());
	}
}
