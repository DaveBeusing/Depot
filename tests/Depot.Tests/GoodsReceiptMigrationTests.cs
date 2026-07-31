// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class GoodsReceiptMigrationTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-goods-receipt-migration-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionSixteenReceiptPreservesLegacyInvoiceDataAndReferences()
	{
		CreateVersionSixteenDatabase();

		new DepotDatabase(new SqliteConnectionFactory(_databasePath)).Initialize();

		using var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
		connection.Open();
		Assert.Equal(DatabaseVersion.CurrentVersion, Scalar(connection, "SELECT Version FROM DatabaseInfo;"));
		Assert.Equal("LEGACY-GR-000042", Text(connection, "SELECT SupplierDeliveryNoteNumber FROM GoodsReceipts WHERE Id = 42;"));
		Assert.Equal(7L, Scalar(connection, "SELECT ReceivedByUserId FROM GoodsReceipts WHERE Id = 42;"));
		Assert.Equal("INV-LEGACY-42", Text(connection, "SELECT InvoiceNumber FROM GoodsReceipts WHERE Id = 42;"));
		Assert.Equal("2026-07-30", Text(connection, "SELECT InvoiceDate FROM GoodsReceipts WHERE Id = 42;"));
		Assert.Equal(@"C:\legacy\invoice-42.pdf", Text(connection, "SELECT InvoiceDocumentPath FROM GoodsReceipts WHERE Id = 42;"));
		Assert.Equal(42L, Scalar(connection, "SELECT GoodsReceiptId FROM GoodsReceiptLines WHERE Id = 50;"));
		Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
	}

	private void CreateVersionSixteenDatabase()
	{
		using var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
			"""
			CREATE TABLE DatabaseInfo (Version INTEGER NOT NULL);
			INSERT INTO DatabaseInfo (Version) VALUES (16);
			CREATE TABLE Users (Id INTEGER PRIMARY KEY, Email TEXT NOT NULL);
			INSERT INTO Users (Id, Email) VALUES (7, 'receiver@depot.test');
			CREATE TABLE PurchaseOrders (Id INTEGER PRIMARY KEY);
			INSERT INTO PurchaseOrders (Id) VALUES (10);
			CREATE TABLE PurchaseOrderLines (Id INTEGER PRIMARY KEY);
			INSERT INTO PurchaseOrderLines (Id) VALUES (20);
			CREATE TABLE Inventories (Id INTEGER PRIMARY KEY);
			INSERT INTO Inventories (Id) VALUES (30);
			CREATE TABLE AuditEntries (Id INTEGER PRIMARY KEY, UserId INTEGER NULL, EntityType TEXT NOT NULL, EntityId INTEGER NOT NULL);
			INSERT INTO AuditEntries (Id, UserId, EntityType, EntityId) VALUES (60, 7, 'GoodsReceipt', 42);
			CREATE TABLE GoodsReceipts
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				ReceiptNumber TEXT NOT NULL UNIQUE,
				PurchaseOrderId INTEGER NOT NULL,
				ReceiptDate TEXT NOT NULL,
				InvoiceNumber TEXT NOT NULL,
				InvoiceDate TEXT NOT NULL,
				InvoiceDocumentPath TEXT NULL,
				Notes TEXT NULL,
				FOREIGN KEY(PurchaseOrderId) REFERENCES PurchaseOrders(Id)
			);
			INSERT INTO GoodsReceipts
				(Id, ReceiptNumber, PurchaseOrderId, ReceiptDate, InvoiceNumber, InvoiceDate, InvoiceDocumentPath, Notes)
			VALUES
				(42, 'GR-000042', 10, '2026-07-29', 'INV-LEGACY-42', '2026-07-30', 'C:\legacy\invoice-42.pdf', 'Legacy receipt');
			CREATE TABLE GoodsReceiptLines
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				GoodsReceiptId INTEGER NOT NULL,
				PurchaseOrderLineId INTEGER NOT NULL,
				InventoryId INTEGER NOT NULL,
				Quantity INTEGER NOT NULL,
				FOREIGN KEY(GoodsReceiptId) REFERENCES GoodsReceipts(Id),
				FOREIGN KEY(PurchaseOrderLineId) REFERENCES PurchaseOrderLines(Id),
				FOREIGN KEY(InventoryId) REFERENCES Inventories(Id)
			);
			INSERT INTO GoodsReceiptLines (Id, GoodsReceiptId, PurchaseOrderLineId, InventoryId, Quantity)
			VALUES (50, 42, 20, 30, 3);
			""";
		command.ExecuteNonQuery();
	}

	private static long Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt64(command.ExecuteScalar());
	}

	private static string Text(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
