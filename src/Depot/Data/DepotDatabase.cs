// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Depot.Diagnostics;
using Depot.Models;

namespace Depot.Data;

public sealed class DepotDatabase : IDatabaseInitializer
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public DepotDatabase(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public void Initialize()
	{
		using var connection =
			_connectionFactory.CreateConnection();

		DatabaseDiagnostics.ConnectionOpening(DatabaseProvider.Local, "local SQLite schema");
		try
		{
			connection.Open();
			DatabaseDiagnostics.ConnectionOpened(DatabaseProvider.Local, "local SQLite schema");
		}
		catch (Exception exception)
		{
			DatabaseDiagnostics.ConnectionFailed(DatabaseProvider.Local, "local SQLite schema", exception);
			throw;
		}

		CreateDatabaseInfoTable(
			connection);

		var version =
			GetDatabaseVersion(
				connection);

		if (version == 0)
		{
			CreateCurrentSchema(
				connection);

			SetDatabaseVersion(
				connection,
				DatabaseVersion.CurrentVersion);

			return;
		}

		ApplyMigrations(
			connection,
			version);
	}

	private static void CreateDatabaseInfoTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS DatabaseInfo
		(
			Version INTEGER NOT NULL
		);
		""";

		command.ExecuteNonQuery();
	}

	private static int GetDatabaseVersion(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		SELECT Version
		FROM DatabaseInfo
		LIMIT 1;
		""";

		var result =
			command.ExecuteScalar();

		if (result is null)
		{
			return 0;
		}

		return Convert.ToInt32(
			result);
	}

	private static void SetDatabaseVersion(
		SqliteConnection connection,
		int version)
	{
		using var deleteCommand =
			connection.CreateCommand();

		deleteCommand.CommandText =
		"""
		DELETE FROM DatabaseInfo;
		""";

		deleteCommand.ExecuteNonQuery();

		using var insertCommand =
			connection.CreateCommand();

		insertCommand.CommandText =
		"""
		INSERT INTO DatabaseInfo
		(
			Version
		)
		VALUES
		(
			$Version
		);
		""";

		insertCommand.Parameters.AddWithValue(
			"$Version",
			version);

		insertCommand.ExecuteNonQuery();
	}

	private static void CreateCurrentSchema(
		SqliteConnection connection)
	{
		CreateItemReferenceDataTables(connection);

		CreateItemsTable(
			connection);

		CreateItemReferenceIndexes(connection);

		CreateSupplierItemsTable(connection);

		CreatePurposesTable(
			connection);

		CreateWarehousesTable(
			connection);

		CreateStorageLocationsTable(
			connection);

		CreateInventoriesTable(
			connection);

		CreateProcurementTables(connection);

		CreateReasonCodesTable(
			connection);

		CreateStockMovementsTable(
			connection);

		CreateStockMovementIndexes(
			connection);

		CreateUsersTable(
			connection);

		CreateStockTransferTables(connection);

		CreateInventoryCountTables(connection);

		CreateAuditEntriesTable(
			connection);

		CreateDefaultPurpose(
			connection);

		CreateDefaultWarehouseStructure(
			connection);

		CreateDefaultReasonCodes(
			connection);

		CreateDefaultAdministrator(
			connection);
	}

	private static void CreateItemsTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Items
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			PartNumber      TEXT NOT NULL UNIQUE,
			Description     TEXT NOT NULL,
			Manufacturer    TEXT,
			Category        TEXT,
			ManufacturerId  INTEGER NULL REFERENCES Manufacturers(Id),
			CategoryId      INTEGER NULL REFERENCES Categories(Id),
			UnitOfMeasureId INTEGER NULL REFERENCES UnitsOfMeasure(Id),
			PackagingId     INTEGER NULL REFERENCES Packagings(Id),
			SupplierId      INTEGER NULL REFERENCES Suppliers(Id),
			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreatePurposesTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Purposes
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			Name            TEXT NOT NULL UNIQUE,
			Description     TEXT,
			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateInventoriesTable(SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Inventories
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,

			ItemId          INTEGER NOT NULL,

			PurposeId       INTEGER NOT NULL,

			StorageLocationId INTEGER NOT NULL,

			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1,

			UNIQUE
			(
				ItemId,
				PurposeId,
				StorageLocationId
			),

			FOREIGN KEY(ItemId)
				REFERENCES Items(Id),

			FOREIGN KEY(PurposeId)
				REFERENCES Purposes(Id),

			FOREIGN KEY(StorageLocationId)
				REFERENCES StorageLocations(Id)
		);
		""";

		command.ExecuteNonQuery();
	}


	private static void CreateLocationsTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Locations
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			Name            TEXT NOT NULL UNIQUE,
			Description     TEXT,
			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateDefaultLocation(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT OR IGNORE INTO Locations
		(
			Name,
			Description,
			IsActive
		)
		VALUES
		(
			'Warehouse',
			'Default warehouse location',
			1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateStockMovementsTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS StockMovements
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,

			InventoryId         INTEGER NOT NULL,

			ReasonCodeId        INTEGER NULL,

			MovementType        INTEGER NOT NULL,

			TimestampUtc        TEXT NOT NULL,

			Quantity            INTEGER NOT NULL,

			UnitPrice           REAL NULL,

			Reference           TEXT NULL,

			Notes               TEXT NULL,

			ReversalOfMovementId INTEGER NULL,

			ReversalReason       TEXT NULL,

			ReversedAtUtc        TEXT NULL,

			ReversedByUserId     INTEGER NULL,

			FOREIGN KEY(InventoryId)
				REFERENCES Inventories(Id),

			FOREIGN KEY(ReasonCodeId)
				REFERENCES ReasonCodes(Id),

			FOREIGN KEY(ReversalOfMovementId)
				REFERENCES StockMovements(Id),

			FOREIGN KEY(ReversedByUserId)
				REFERENCES Users(Id)
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateProcurementTables(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS PurchaseOrders
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderNumber TEXT NOT NULL UNIQUE, SupplierId INTEGER NOT NULL,
			OrderDate TEXT NOT NULL, ExpectedDeliveryDate TEXT, Notes TEXT, Status INTEGER NOT NULL DEFAULT 1,
			CreatedByUserId INTEGER NULL, SubmittedByUserId INTEGER NULL, SubmittedAtUtc TEXT NULL,
			ApprovalDecisionByUserId INTEGER NULL, ApprovalDecisionAtUtc TEXT NULL, ApprovalComment TEXT NULL,
			ClosedByUserId INTEGER NULL, ClosedAtUtc TEXT NULL, CloseReason TEXT NULL,
			Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id),
			FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id), FOREIGN KEY(SubmittedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(ApprovalDecisionByUserId) REFERENCES Users(Id), FOREIGN KEY(ClosedByUserId) REFERENCES Users(Id)
		);
		CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_SupplierId_Status ON PurchaseOrders(SupplierId, Status);
		CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_OrderDate ON PurchaseOrders(OrderDate);
		CREATE TABLE IF NOT EXISTS PurchaseOrderLines
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT, PurchaseOrderId INTEGER NOT NULL, LineNumber INTEGER NOT NULL,
			ItemId INTEGER NOT NULL, Quantity INTEGER NOT NULL, UnitPrice NUMERIC NOT NULL DEFAULT 0,
			ReceivedQuantity INTEGER NOT NULL DEFAULT 0, Version INTEGER NOT NULL DEFAULT 1,
			UNIQUE(PurchaseOrderId, LineNumber), UNIQUE(PurchaseOrderId, ItemId),
			FOREIGN KEY(PurchaseOrderId) REFERENCES PurchaseOrders(Id), FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CHECK(Quantity > 0), CHECK(ReceivedQuantity >= 0 AND ReceivedQuantity <= Quantity)
		);
		CREATE INDEX IF NOT EXISTS IX_PurchaseOrderLines_ItemId ON PurchaseOrderLines(ItemId);
		CREATE TABLE IF NOT EXISTS GoodsReceipts
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT, ReceiptNumber TEXT NOT NULL UNIQUE, PurchaseOrderId INTEGER NOT NULL,
			ReceiptDate TEXT NOT NULL, SupplierDeliveryNoteNumber TEXT NOT NULL, ReceivedByUserId INTEGER NOT NULL,
			InvoiceNumber TEXT NULL, InvoiceDate TEXT NULL, InvoiceDocumentPath TEXT NULL, Notes TEXT NULL,
			ReversedAtUtc TEXT NULL, ReversedByUserId INTEGER NULL, ReversalReason TEXT NULL, Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(PurchaseOrderId) REFERENCES PurchaseOrders(Id), FOREIGN KEY(ReceivedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(ReversedByUserId) REFERENCES Users(Id)
		);
		CREATE INDEX IF NOT EXISTS IX_GoodsReceipts_PurchaseOrderId ON GoodsReceipts(PurchaseOrderId);
		CREATE INDEX IF NOT EXISTS IX_GoodsReceipts_ReceivedByUserId ON GoodsReceipts(ReceivedByUserId);
		CREATE TABLE IF NOT EXISTS GoodsReceiptLines
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT, GoodsReceiptId INTEGER NOT NULL, PurchaseOrderLineId INTEGER NOT NULL,
			InventoryId INTEGER NOT NULL, Quantity INTEGER NOT NULL CHECK(Quantity > 0),
			UNIQUE(GoodsReceiptId, PurchaseOrderLineId), FOREIGN KEY(GoodsReceiptId) REFERENCES GoodsReceipts(Id),
			FOREIGN KEY(PurchaseOrderLineId) REFERENCES PurchaseOrderLines(Id), FOREIGN KEY(InventoryId) REFERENCES Inventories(Id)
		);
		CREATE INDEX IF NOT EXISTS IX_GoodsReceiptLines_InventoryId ON GoodsReceiptLines(InventoryId);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateStockTransferTables(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS StockTransfers
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			TransferNumber TEXT NOT NULL UNIQUE,
			SourceWarehouseId INTEGER NOT NULL,
			DestinationWarehouseId INTEGER NOT NULL,
			TransferDate TEXT NOT NULL,
			Status INTEGER NOT NULL DEFAULT 1,
			CreatedByUserId INTEGER NOT NULL,
			PostedByUserId INTEGER NULL,
			Notes TEXT NULL,
			ReversedAtUtc TEXT NULL,
			ReversedByUserId INTEGER NULL,
			ReversalReason TEXT NULL,
			Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(SourceWarehouseId) REFERENCES Warehouses(Id),
			FOREIGN KEY(DestinationWarehouseId) REFERENCES Warehouses(Id),
			FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(PostedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(ReversedByUserId) REFERENCES Users(Id),
			CHECK(SourceWarehouseId <> DestinationWarehouseId),
			CHECK(Status IN (1, 2, 3))
		);
		CREATE INDEX IF NOT EXISTS IX_StockTransfers_SourceWarehouseId_Status ON StockTransfers(SourceWarehouseId, Status);
		CREATE INDEX IF NOT EXISTS IX_StockTransfers_DestinationWarehouseId_Status ON StockTransfers(DestinationWarehouseId, Status);
		CREATE INDEX IF NOT EXISTS IX_StockTransfers_TransferDate ON StockTransfers(TransferDate);

		CREATE TABLE IF NOT EXISTS StockTransferLines
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			StockTransferId INTEGER NOT NULL,
			LineNumber INTEGER NOT NULL,
			SourceInventoryId INTEGER NOT NULL,
			DestinationInventoryId INTEGER NOT NULL,
			Quantity INTEGER NOT NULL,
			Version INTEGER NOT NULL DEFAULT 1,
			UNIQUE(StockTransferId, LineNumber),
			UNIQUE(StockTransferId, SourceInventoryId, DestinationInventoryId),
			FOREIGN KEY(StockTransferId) REFERENCES StockTransfers(Id),
			FOREIGN KEY(SourceInventoryId) REFERENCES Inventories(Id),
			FOREIGN KEY(DestinationInventoryId) REFERENCES Inventories(Id),
			CHECK(Quantity > 0),
			CHECK(SourceInventoryId <> DestinationInventoryId)
		);
		CREATE INDEX IF NOT EXISTS IX_StockTransferLines_SourceInventoryId ON StockTransferLines(SourceInventoryId);
		CREATE INDEX IF NOT EXISTS IX_StockTransferLines_DestinationInventoryId ON StockTransferLines(DestinationInventoryId);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateInventoryCountTables(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS InventoryCounts
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			CountNumber TEXT NOT NULL UNIQUE,
			WarehouseId INTEGER NOT NULL,
			Status INTEGER NOT NULL DEFAULT 1,
			CreatedAtUtc TEXT NOT NULL,
			StartedAtUtc TEXT NULL,
			CompletedAtUtc TEXT NULL,
			CreatedByUserId INTEGER NOT NULL,
			PostedByUserId INTEGER NULL,
			Notes TEXT NULL,
			ReversedAtUtc TEXT NULL,
			ReversedByUserId INTEGER NULL,
			ReversalReason TEXT NULL,
			Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id),
			FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(PostedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(ReversedByUserId) REFERENCES Users(Id),
			CHECK(Status IN (1, 2, 3, 4, 5))
		);
		CREATE INDEX IF NOT EXISTS IX_InventoryCounts_WarehouseId_Status ON InventoryCounts(WarehouseId, Status);
		CREATE INDEX IF NOT EXISTS IX_InventoryCounts_CreatedAtUtc ON InventoryCounts(CreatedAtUtc);

		CREATE TABLE IF NOT EXISTS InventoryCountLines
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			InventoryCountId INTEGER NOT NULL,
			InventoryId INTEGER NOT NULL,
			ExpectedQuantity INTEGER NOT NULL,
			CountedQuantity INTEGER NULL,
			CountedByUserId INTEGER NULL,
			CountedAtUtc TEXT NULL,
			Version INTEGER NOT NULL DEFAULT 1,
			UNIQUE(InventoryCountId, InventoryId),
			FOREIGN KEY(InventoryCountId) REFERENCES InventoryCounts(Id),
			FOREIGN KEY(InventoryId) REFERENCES Inventories(Id),
			FOREIGN KEY(CountedByUserId) REFERENCES Users(Id),
			CHECK(CountedQuantity IS NULL OR CountedQuantity >= 0)
		);
		CREATE INDEX IF NOT EXISTS IX_InventoryCountLines_InventoryId ON InventoryCountLines(InventoryId);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateItemReferenceDataTables(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Manufacturers (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Description TEXT, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);
		CREATE TABLE IF NOT EXISTS Categories (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Description TEXT, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);
		CREATE TABLE IF NOT EXISTS UnitsOfMeasure (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Description TEXT, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);
		CREATE TABLE IF NOT EXISTS Packagings (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Description TEXT, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);
		CREATE TABLE IF NOT EXISTS SupplierCategories (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Description TEXT, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);
		INSERT OR IGNORE INTO SupplierCategories (Name) VALUES ('IT Hardware'), ('ProAV'), ('Licensing');
		CREATE TABLE IF NOT EXISTS Suppliers
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT, SupplierNumber TEXT NOT NULL UNIQUE, AccountNumber INTEGER NOT NULL UNIQUE, CustomerNumber TEXT, Name TEXT NOT NULL UNIQUE,
			Contact TEXT, Email TEXT, Phone TEXT, Address TEXT, RmaTerms TEXT, Url TEXT, PaymentTerm TEXT,
			Iban TEXT, AccountName TEXT, SepaMandate TEXT, VatNumber TEXT, SupplierCategoryId INTEGER NULL REFERENCES SupplierCategories(Id),
			Loyalty INTEGER NOT NULL DEFAULT 100, Quality INTEGER NOT NULL DEFAULT 100, Notes TEXT, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1
		);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateSupplierItemsTable(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS SupplierItems
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT, SupplierId INTEGER NOT NULL REFERENCES Suppliers(Id), ItemId INTEGER NOT NULL REFERENCES Items(Id),
			SupplierPartNumber TEXT NOT NULL, PurchasePrice NUMERIC NOT NULL DEFAULT 0, LeadTimeDays INTEGER NOT NULL DEFAULT 0,
			MinimumOrderQuantity NUMERIC NOT NULL DEFAULT 1, IsPreferredSupplier INTEGER NOT NULL DEFAULT 0,
			IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1, UNIQUE (SupplierId, ItemId)
		);
		CREATE INDEX IF NOT EXISTS IX_SupplierItems_SupplierId ON SupplierItems(SupplierId);
		CREATE INDEX IF NOT EXISTS IX_SupplierItems_ItemId ON SupplierItems(ItemId);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateItemReferenceIndexes(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE INDEX IF NOT EXISTS IX_Items_ManufacturerId ON Items(ManufacturerId);
		CREATE INDEX IF NOT EXISTS IX_Items_CategoryId ON Items(CategoryId);
		CREATE INDEX IF NOT EXISTS IX_Items_UnitOfMeasureId ON Items(UnitOfMeasureId);
		CREATE INDEX IF NOT EXISTS IX_Items_PackagingId ON Items(PackagingId);
		CREATE INDEX IF NOT EXISTS IX_Items_SupplierId ON Items(SupplierId);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateReasonCodesTable(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS ReasonCodes
		(
			Id          INTEGER PRIMARY KEY AUTOINCREMENT,
			Code        TEXT NOT NULL UNIQUE,
			Name        TEXT NOT NULL UNIQUE,
			Description TEXT NULL,
			IsSystem    INTEGER NOT NULL DEFAULT 0,
			IsActive    INTEGER NOT NULL DEFAULT 1,
			Version     INTEGER NOT NULL DEFAULT 1
		);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateDefaultReasonCodes(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('GOODS_RECEIPT', 'Goods Receipt', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('GOODS_ISSUE', 'Goods Issue', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('INVENTORY_CORRECTION', 'Inventory Correction', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('DAMAGED', 'Damaged', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('LOST', 'Lost', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('RETURNED', 'Returned', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('CONSUMED', 'Consumed', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('DEMO', 'Demo', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('REPAIR', 'Repair', 1, 1);
		INSERT OR IGNORE INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES ('TRANSFER', 'Transfer', 1, 1);
		UPDATE ReasonCodes SET IsSystem = 1, IsActive = 1 WHERE Code = 'GOODS_RECEIPT';
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateWarehousesTable(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Warehouses
		(
			Id          INTEGER PRIMARY KEY AUTOINCREMENT,
			Name        TEXT NOT NULL UNIQUE,
			Description TEXT NULL,
			IsActive    INTEGER NOT NULL DEFAULT 1,
			Version     INTEGER NOT NULL DEFAULT 1
		);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateStorageLocationsTable(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS StorageLocations
		(
			Id          INTEGER PRIMARY KEY AUTOINCREMENT,
			WarehouseId INTEGER NOT NULL,
			Name        TEXT NOT NULL,
			Description TEXT NULL,
			IsActive    INTEGER NOT NULL DEFAULT 1,
			Version     INTEGER NOT NULL DEFAULT 1,
			UNIQUE(WarehouseId, Name),
			FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id)
		);
		CREATE INDEX IF NOT EXISTS IX_StorageLocations_WarehouseId_Name
			ON StorageLocations(WarehouseId, Name);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateDefaultWarehouseStructure(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		INSERT OR IGNORE INTO Warehouses (Name, Description, IsActive)
		VALUES ('Main Warehouse', 'Default Depot warehouse', 1);

		INSERT OR IGNORE INTO StorageLocations (WarehouseId, Name, Description, IsActive)
		SELECT Id, 'Default', 'Default storage location', 1
		FROM Warehouses
		WHERE Name = 'Main Warehouse';
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateStockMovementIndexes(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE INDEX IF NOT EXISTS IX_StockMovements_InventoryId_TimestampUtc
			ON StockMovements
			(
				InventoryId,
				TimestampUtc
			);

		CREATE INDEX IF NOT EXISTS IX_StockMovements_ReasonCodeId
			ON StockMovements(ReasonCodeId);

		CREATE UNIQUE INDEX IF NOT EXISTS UX_StockMovements_ReversalOfMovementId
			ON StockMovements(ReversalOfMovementId)
			WHERE ReversalOfMovementId IS NOT NULL;
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateUsersTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Users
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			Email               TEXT NOT NULL COLLATE NOCASE UNIQUE,
			DisplayName         TEXT NOT NULL,
			PasswordHash        TEXT NOT NULL,
			IsAdministrator     INTEGER NOT NULL DEFAULT 0,
			CanApprovePurchaseOrders INTEGER NOT NULL DEFAULT 0,
			IsActive            INTEGER NOT NULL DEFAULT 1,
			CreatedUtc          TEXT NOT NULL,
			Version             INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateAuditEntriesTable(
		SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS AuditEntries
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			TimestampUtc    TEXT NOT NULL,
			UserId          INTEGER NULL,
			UserEmail       TEXT NOT NULL,
			EntityType      TEXT NOT NULL,
			EntityId        INTEGER NOT NULL,
			Action          TEXT NOT NULL,
			BeforeJson      TEXT NULL,
			AfterJson       TEXT NULL,
			FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE SET NULL
		);

		CREATE INDEX IF NOT EXISTS IX_AuditEntries_TimestampUtc
			ON AuditEntries(TimestampUtc DESC);

		CREATE INDEX IF NOT EXISTS IX_AuditEntries_Entity
			ON AuditEntries(EntityType, EntityId, TimestampUtc DESC);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateDefaultPurpose(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT OR IGNORE INTO Purposes
		(
			Name,
			Description,
			IsActive
		)
		VALUES
		(
			'Stock',
			'Default stock purpose',
			1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateDefaultAdministrator(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT OR IGNORE INTO Users
		(
			Email,
			DisplayName,
			PasswordHash,
			IsAdministrator,
			IsActive,
			CreatedUtc
		)
		VALUES
		(
			'admin@depot.local',
			'Administrator',
			'pbkdf2-sha256$210000$9vL0kVt/HZBUCpsJYjPW6Q==$B1lZ+NRxxR/E8kwIE5PK0wXR2BPDmFTeLiKYyAEuhaE=',
			1,
			1,
			strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void ApplyMigrations(
		SqliteConnection connection,
		int version)
	{
		var migratedVersion =
			version;

		if (migratedVersion < 3)
		{
			throw new InvalidOperationException(
				$"Database version '{version}' is older than the supported migration baseline '3'. Delete depot.db and import the Excel file again.");
		}

		if (migratedVersion is 3 or 4 or 5)
		{
			CreateUsersTable(
				connection);

			if (TableHasColumn(
				connection,
				"Users",
				"UserName"))
			{
				MigrateUsersToEmailAuthentication(
					connection);
			}

			CreateDefaultAdministrator(
				connection);

			SetDatabaseVersion(
				connection,
				6);

			migratedVersion =
				6;
		}

		if (migratedVersion == 6)
		{
			MigrateStockMovementsToInventory(connection);

			SetDatabaseVersion(
				connection,
				7);

			migratedVersion =
				7;
		}

		if (migratedVersion == 7)
		{
			MigrateToAuditAndConcurrency(connection);
			SetDatabaseVersion(connection, 8);
			migratedVersion = 8;
		}

		if (migratedVersion == 8)
		{
			MigrateToWarehouseStructure(connection);
			SetDatabaseVersion(connection, 9);
			migratedVersion = 9;
		}

		if (migratedVersion == 9)
		{
			MigrateToReasonCodes(connection);
			SetDatabaseVersion(connection, 10);
			migratedVersion = 10;
		}

		if (migratedVersion == 10)
		{
			MigrateToNormalizedItemMasterData(connection);
			SetDatabaseVersion(connection, 11);
			migratedVersion = 11;
		}

		if (migratedVersion == 11)
		{
			MigrateToSupplierManagement(connection);
			SetDatabaseVersion(connection, 12);
			migratedVersion = 12;
		}

		if (migratedVersion == 12)
		{
			MigrateSupplierAccountFields(connection);
			SetDatabaseVersion(connection, 13);
			migratedVersion = 13;
		}

		if (migratedVersion == 13)
		{
			MigrateSupplierClassification(connection);
			SetDatabaseVersion(connection, 14);
			migratedVersion = 14;
		}

		if (migratedVersion == 14)
		{
			CreateProcurementTables(connection);
			SetDatabaseVersion(connection, 15);
			migratedVersion = 15;
		}

		if (migratedVersion == 15)
		{
			MigrateReasonCodesToTechnicalKeys(connection);
			SetDatabaseVersion(connection, 16);
			migratedVersion = 16;
		}

		if (migratedVersion == 16)
		{
			MigrateGoodsReceiptsToDeliveryDocuments(connection);
			SetDatabaseVersion(connection, 17);
			migratedVersion = 17;
		}

		if (migratedVersion == 17)
		{
			CreateStockTransferTables(connection);
			SetDatabaseVersion(connection, 18);
			migratedVersion = 18;
		}

		if (migratedVersion == 18)
		{
			CreateInventoryCountTables(connection);
			SetDatabaseVersion(connection, 19);
			migratedVersion = 19;
		}

		if (migratedVersion == 19)
		{
			MigrateToMovementReversals(connection);
			SetDatabaseVersion(connection, 20);
			migratedVersion = 20;
		}

		if (migratedVersion == 20)
		{
			MigrateToPurchaseOrderApproval(connection);
			SetDatabaseVersion(connection, 21);
			migratedVersion = 21;
		}

		if (migratedVersion == 21)
		{
			MigrateToPurchaseOrderClosure(connection);
			SetDatabaseVersion(connection, 22);
			migratedVersion = 22;
		}

		if (migratedVersion < DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"Database version '{migratedVersion}' is older than the current schema version '{DatabaseVersion.CurrentVersion}'. Delete depot.db and import the Excel file again.");
		}

		if (migratedVersion > DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"Database version '{version}' is newer than the supported schema version '{DatabaseVersion.CurrentVersion}'.");
		}
	}

	private static void MigrateToPurchaseOrderApproval(SqliteConnection connection)
	{
		AddColumn("Users", "CanApprovePurchaseOrders", "INTEGER NOT NULL DEFAULT 0");
		AddColumn("PurchaseOrders", "CreatedByUserId", "INTEGER NULL REFERENCES Users(Id)");
		AddColumn("PurchaseOrders", "SubmittedByUserId", "INTEGER NULL REFERENCES Users(Id)");
		AddColumn("PurchaseOrders", "SubmittedAtUtc", "TEXT NULL");
		AddColumn("PurchaseOrders", "ApprovalDecisionByUserId", "INTEGER NULL REFERENCES Users(Id)");
		AddColumn("PurchaseOrders", "ApprovalDecisionAtUtc", "TEXT NULL");
		AddColumn("PurchaseOrders", "ApprovalComment", "TEXT NULL");

		void AddColumn(string table, string column, string definition)
		{
			if (!TableExists(connection, table) || TableHasColumn(connection, table, column)) return;
			using var command = connection.CreateCommand();
			command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
			command.ExecuteNonQuery();
		}
	}

	private static void MigrateToPurchaseOrderClosure(SqliteConnection connection)
	{
		AddColumn("ClosedByUserId", "INTEGER NULL REFERENCES Users(Id)");
		AddColumn("ClosedAtUtc", "TEXT NULL");
		AddColumn("CloseReason", "TEXT NULL");

		void AddColumn(string column, string definition)
		{
			if (!TableExists(connection, "PurchaseOrders") || TableHasColumn(connection, "PurchaseOrders", column)) return;
			using var command = connection.CreateCommand();
			command.CommandText = $"ALTER TABLE PurchaseOrders ADD COLUMN {column} {definition};";
			command.ExecuteNonQuery();
		}
	}

	private static void MigrateToMovementReversals(SqliteConnection connection)
	{
		AddColumnIfMissing("StockMovements", "ReversalOfMovementId", "INTEGER NULL REFERENCES StockMovements(Id)");
		AddColumnIfMissing("StockMovements", "ReversalReason", "TEXT NULL");
		AddColumnIfMissing("StockMovements", "ReversedAtUtc", "TEXT NULL");
		AddColumnIfMissing("StockMovements", "ReversedByUserId", "INTEGER NULL REFERENCES Users(Id)");
		if (TableExists(connection, "StockMovements"))
		{
			using var indexCommand = connection.CreateCommand();
			indexCommand.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS UX_StockMovements_ReversalOfMovementId ON StockMovements(ReversalOfMovementId) WHERE ReversalOfMovementId IS NOT NULL;";
			indexCommand.ExecuteNonQuery();
		}

		AddColumnIfMissing("GoodsReceipts", "ReversedAtUtc", "TEXT NULL");
		AddColumnIfMissing("GoodsReceipts", "ReversedByUserId", "INTEGER NULL REFERENCES Users(Id)");
		AddColumnIfMissing("GoodsReceipts", "ReversalReason", "TEXT NULL");
		AddColumnIfMissing("GoodsReceipts", "Version", "INTEGER NOT NULL DEFAULT 1");
		AddColumnIfMissing("StockTransfers", "ReversedAtUtc", "TEXT NULL");
		AddColumnIfMissing("StockTransfers", "ReversedByUserId", "INTEGER NULL REFERENCES Users(Id)");
		AddColumnIfMissing("StockTransfers", "ReversalReason", "TEXT NULL");
		AddColumnIfMissing("InventoryCounts", "ReversedAtUtc", "TEXT NULL");
		AddColumnIfMissing("InventoryCounts", "ReversedByUserId", "INTEGER NULL REFERENCES Users(Id)");
		AddColumnIfMissing("InventoryCounts", "ReversalReason", "TEXT NULL");

		void AddColumnIfMissing(string table, string column, string definition)
		{
			if (!TableExists(connection, table) || TableHasColumn(connection, table, column)) return;
			using var command = connection.CreateCommand();
			command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
			command.ExecuteNonQuery();
		}
	}

	private static void MigrateGoodsReceiptsToDeliveryDocuments(SqliteConnection connection)
	{
		if (!TableExists(connection, "GoodsReceipts"))
		{
			CreateProcurementTables(connection);
			return;
		}
		if (TableHasColumn(connection, "GoodsReceipts", "SupplierDeliveryNoteNumber")) return;

		using (var disableForeignKeys = connection.CreateCommand())
		{
			disableForeignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
			disableForeignKeys.ExecuteNonQuery();
		}

		try
		{
			using var transaction = connection.BeginTransaction();
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText =
				"""
				ALTER TABLE GoodsReceiptLines RENAME TO GoodsReceiptLinesInvoiceMigration;
				ALTER TABLE GoodsReceipts RENAME TO GoodsReceiptsInvoiceMigration;

				CREATE TABLE GoodsReceipts
				(
					Id INTEGER PRIMARY KEY AUTOINCREMENT, ReceiptNumber TEXT NOT NULL UNIQUE, PurchaseOrderId INTEGER NOT NULL,
					ReceiptDate TEXT NOT NULL, SupplierDeliveryNoteNumber TEXT NOT NULL, ReceivedByUserId INTEGER NOT NULL,
					InvoiceNumber TEXT NULL, InvoiceDate TEXT NULL, InvoiceDocumentPath TEXT NULL, Notes TEXT NULL,
					FOREIGN KEY(PurchaseOrderId) REFERENCES PurchaseOrders(Id), FOREIGN KEY(ReceivedByUserId) REFERENCES Users(Id)
				);

				INSERT INTO GoodsReceipts
					(Id, ReceiptNumber, PurchaseOrderId, ReceiptDate, SupplierDeliveryNoteNumber, ReceivedByUserId, InvoiceNumber, InvoiceDate, InvoiceDocumentPath, Notes)
				SELECT
					gr.Id, gr.ReceiptNumber, gr.PurchaseOrderId, gr.ReceiptDate, 'LEGACY-' || gr.ReceiptNumber,
					COALESCE(
						(SELECT ae.UserId FROM AuditEntries ae INNER JOIN Users auditUser ON auditUser.Id = ae.UserId WHERE ae.EntityType = 'GoodsReceipt' AND ae.EntityId = gr.Id AND ae.UserId IS NOT NULL ORDER BY ae.Id LIMIT 1),
						(SELECT Id FROM Users WHERE Email = 'admin@depot.local' LIMIT 1),
						(SELECT MIN(Id) FROM Users)),
					gr.InvoiceNumber, gr.InvoiceDate, gr.InvoiceDocumentPath, gr.Notes
				FROM GoodsReceiptsInvoiceMigration gr;

				CREATE TABLE GoodsReceiptLines
				(
					Id INTEGER PRIMARY KEY AUTOINCREMENT, GoodsReceiptId INTEGER NOT NULL, PurchaseOrderLineId INTEGER NOT NULL,
					InventoryId INTEGER NOT NULL, Quantity INTEGER NOT NULL CHECK(Quantity > 0),
					UNIQUE(GoodsReceiptId, PurchaseOrderLineId), FOREIGN KEY(GoodsReceiptId) REFERENCES GoodsReceipts(Id),
					FOREIGN KEY(PurchaseOrderLineId) REFERENCES PurchaseOrderLines(Id), FOREIGN KEY(InventoryId) REFERENCES Inventories(Id)
				);
				INSERT INTO GoodsReceiptLines (Id, GoodsReceiptId, PurchaseOrderLineId, InventoryId, Quantity)
				SELECT Id, GoodsReceiptId, PurchaseOrderLineId, InventoryId, Quantity FROM GoodsReceiptLinesInvoiceMigration;

				DROP TABLE GoodsReceiptLinesInvoiceMigration;
				DROP TABLE GoodsReceiptsInvoiceMigration;
				CREATE INDEX IX_GoodsReceipts_PurchaseOrderId ON GoodsReceipts(PurchaseOrderId);
				CREATE INDEX IX_GoodsReceipts_ReceivedByUserId ON GoodsReceipts(ReceivedByUserId);
				CREATE INDEX IX_GoodsReceiptLines_InventoryId ON GoodsReceiptLines(InventoryId);
				""";
			command.ExecuteNonQuery();
			transaction.Commit();
		}
		finally
		{
			using var enableForeignKeys = connection.CreateCommand();
			enableForeignKeys.CommandText = "PRAGMA foreign_keys = ON;";
			enableForeignKeys.ExecuteNonQuery();
		}

		using var integrityCommand = connection.CreateCommand();
		integrityCommand.CommandText = "PRAGMA foreign_key_check;";
		using var violations = integrityCommand.ExecuteReader();
		if (violations.Read()) throw new InvalidOperationException("Goods receipt migration produced invalid foreign-key references.");
	}

	private static void MigrateReasonCodesToTechnicalKeys(SqliteConnection connection)
	{
		var hasCode = TableHasColumn(connection, "ReasonCodes", "Code");
		var hasIsSystem = TableHasColumn(connection, "ReasonCodes", "IsSystem");
		using var transaction = connection.BeginTransaction();
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		if (!hasCode)
		{
			command.CommandText = "ALTER TABLE ReasonCodes ADD COLUMN Code TEXT NOT NULL DEFAULT '';";
			command.ExecuteNonQuery();
		}
		if (!hasIsSystem)
		{
			command.CommandText = "ALTER TABLE ReasonCodes ADD COLUMN IsSystem INTEGER NOT NULL DEFAULT 0;";
			command.ExecuteNonQuery();
		}
		command.CommandText =
			"""
			UPDATE ReasonCodes
			SET Code = CASE Name
				WHEN 'Goods Receipt' THEN 'GOODS_RECEIPT'
				WHEN 'Goods Issue' THEN 'GOODS_ISSUE'
				WHEN 'Inventory Correction' THEN 'INVENTORY_CORRECTION'
				WHEN 'Damaged' THEN 'DAMAGED'
				WHEN 'Lost' THEN 'LOST'
				WHEN 'Returned' THEN 'RETURNED'
				WHEN 'Consumed' THEN 'CONSUMED'
				WHEN 'Demo' THEN 'DEMO'
				WHEN 'Repair' THEN 'REPAIR'
				WHEN 'Transfer' THEN 'TRANSFER'
				ELSE 'LEGACY_' || printf('%06d', Id)
			END,
			IsSystem = CASE WHEN Name IN
			(
				'Goods Receipt', 'Goods Issue', 'Inventory Correction', 'Damaged', 'Lost',
				'Returned', 'Consumed', 'Demo', 'Repair', 'Transfer'
			) THEN 1 ELSE IsSystem END
			WHERE Code = '';
			CREATE UNIQUE INDEX UX_ReasonCodes_Code ON ReasonCodes(Code);
			""";
		command.ExecuteNonQuery();
		transaction.Commit();
		CreateDefaultReasonCodes(connection);
	}

	private static void MigrateSupplierClassification(SqliteConnection connection)
	{
		using var transaction = connection.BeginTransaction();
		using (var create = connection.CreateCommand())
		{
			create.Transaction = transaction;
			create.CommandText = "CREATE TABLE IF NOT EXISTS SupplierCategories (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Description TEXT, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1); INSERT OR IGNORE INTO SupplierCategories (Name) VALUES ('IT Hardware'), ('ProAV'), ('Licensing');";
			create.ExecuteNonQuery();
		}
		AddColumn("AccountNumber", "INTEGER");
		AddColumn("SupplierCategoryId", "INTEGER NULL REFERENCES SupplierCategories(Id)");
		AddColumn("SepaMandate", "TEXT");
		AddColumn("Quality", "INTEGER NOT NULL DEFAULT 100");
		using (var migrate = connection.CreateCommand())
		{
			migrate.Transaction = transaction;
			migrate.CommandText =
				"""
				UPDATE Suppliers SET AccountNumber = Id WHERE AccountNumber IS NULL OR AccountNumber <= 0;
				CREATE UNIQUE INDEX IF NOT EXISTS UX_Suppliers_AccountNumber ON Suppliers(AccountNumber);
				""";
			migrate.ExecuteNonQuery();
		}
		if (TableHasColumn(connection, "Suppliers", "CategoryId"))
		{
			using var categories = connection.CreateCommand();
			categories.Transaction = transaction;
			categories.CommandText =
				"""
				INSERT OR IGNORE INTO SupplierCategories (Name, Description)
				SELECT DISTINCT c.Name, c.Description FROM Suppliers s INNER JOIN Categories c ON c.Id = s.CategoryId WHERE s.CategoryId IS NOT NULL;
				UPDATE Suppliers SET SupplierCategoryId = (SELECT sc.Id FROM Categories c INNER JOIN SupplierCategories sc ON sc.Name = c.Name WHERE c.Id = Suppliers.CategoryId) WHERE CategoryId IS NOT NULL AND SupplierCategoryId IS NULL;
				""";
			categories.ExecuteNonQuery();
		}
		transaction.Commit();

		void AddColumn(string name, string definition)
		{
			if (TableHasColumn(connection, "Suppliers", name)) return;
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = $"ALTER TABLE Suppliers ADD COLUMN {name} {definition};";
			command.ExecuteNonQuery();
		}
	}

	private static void MigrateSupplierAccountFields(SqliteConnection connection)
	{
		var hasLegacyLoyalty = TableHasColumn(connection, "Suppliers", "IsLoyal");
		var hasNumericLoyalty = TableHasColumn(connection, "Suppliers", "Loyalty");
		using var transaction = connection.BeginTransaction();
		AddColumn("CustomerNumber", "TEXT");
		AddColumn("Loyalty", "INTEGER NOT NULL DEFAULT 100");
		if (hasLegacyLoyalty && !hasNumericLoyalty)
		{
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "UPDATE Suppliers SET Loyalty = CASE WHEN IsLoyal = 1 THEN 100 ELSE 0 END;";
			command.ExecuteNonQuery();
		}
		transaction.Commit();

		void AddColumn(string name, string definition)
		{
			if (TableHasColumn(connection, "Suppliers", name)) return;
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = $"ALTER TABLE Suppliers ADD COLUMN {name} {definition};";
			command.ExecuteNonQuery();
		}
	}

	private static void MigrateToSupplierManagement(SqliteConnection connection)
	{
		var hasLegacyDescription = TableHasColumn(connection, "Suppliers", "Description");
		using var transaction = connection.BeginTransaction();
		AddColumn("SupplierNumber", "TEXT"); AddColumn("Contact", "TEXT"); AddColumn("Email", "TEXT");
		AddColumn("Phone", "TEXT"); AddColumn("Address", "TEXT"); AddColumn("RmaTerms", "TEXT");
		AddColumn("Url", "TEXT"); AddColumn("PaymentTerm", "TEXT"); AddColumn("Iban", "TEXT");
		AddColumn("AccountName", "TEXT"); AddColumn("VatNumber", "TEXT"); AddColumn("CategoryId", "INTEGER NULL REFERENCES Categories(Id)");
		AddColumn("IsLoyal", "INTEGER NOT NULL DEFAULT 0"); AddColumn("Notes", "TEXT");
		using (var command = connection.CreateCommand())
		{
			command.Transaction = transaction;
			command.CommandText = "UPDATE Suppliers SET SupplierNumber = 'SUP-' || printf('%05d', Id) WHERE SupplierNumber IS NULL OR TRIM(SupplierNumber) = ''; CREATE UNIQUE INDEX IF NOT EXISTS UX_Suppliers_SupplierNumber ON Suppliers(SupplierNumber);";
			command.ExecuteNonQuery();
		}
		if (hasLegacyDescription)
		{
			using var preserve = connection.CreateCommand();
			preserve.Transaction = transaction;
			preserve.CommandText = "UPDATE Suppliers SET Notes = Description WHERE Notes IS NULL AND Description IS NOT NULL;";
			preserve.ExecuteNonQuery();
		}
		transaction.Commit();
		CreateSupplierItemsTable(connection);
		using var migration = connection.CreateCommand();
		migration.CommandText =
		"""
		INSERT OR IGNORE INTO SupplierItems (SupplierId, ItemId, SupplierPartNumber, PurchasePrice, LeadTimeDays, MinimumOrderQuantity, IsPreferredSupplier)
		SELECT SupplierId, Id, PartNumber, 0, 0, 1, 1 FROM Items WHERE SupplierId IS NOT NULL;
		""";
		migration.ExecuteNonQuery();

		void AddColumn(string name, string definition)
		{
			if (TableHasColumn(connection, "Suppliers", name)) return;
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = $"ALTER TABLE Suppliers ADD COLUMN {name} {definition};";
			command.ExecuteNonQuery();
		}
	}

	private static void MigrateToNormalizedItemMasterData(SqliteConnection connection)
	{
		CreateItemReferenceDataTables(connection);
		using var transaction = connection.BeginTransaction();
		AddColumnIfMissing("ManufacturerId", "Manufacturers");
		AddColumnIfMissing("CategoryId", "Categories");
		AddColumnIfMissing("UnitOfMeasureId", "UnitsOfMeasure");
		AddColumnIfMissing("PackagingId", "Packagings");
		AddColumnIfMissing("SupplierId", "Suppliers");
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
		"""
		INSERT OR IGNORE INTO Manufacturers (Name) SELECT DISTINCT TRIM(Manufacturer) FROM Items WHERE Manufacturer IS NOT NULL AND TRIM(Manufacturer) <> '';
		INSERT OR IGNORE INTO Categories (Name) SELECT DISTINCT TRIM(Category) FROM Items WHERE Category IS NOT NULL AND TRIM(Category) <> '';
		UPDATE Items SET ManufacturerId = (SELECT Id FROM Manufacturers WHERE Name = TRIM(Items.Manufacturer)) WHERE Manufacturer IS NOT NULL AND TRIM(Manufacturer) <> '';
		UPDATE Items SET CategoryId = (SELECT Id FROM Categories WHERE Name = TRIM(Items.Category)) WHERE Category IS NOT NULL AND TRIM(Category) <> '';
		CREATE INDEX IF NOT EXISTS IX_Items_ManufacturerId ON Items(ManufacturerId);
		CREATE INDEX IF NOT EXISTS IX_Items_CategoryId ON Items(CategoryId);
		CREATE INDEX IF NOT EXISTS IX_Items_UnitOfMeasureId ON Items(UnitOfMeasureId);
		CREATE INDEX IF NOT EXISTS IX_Items_PackagingId ON Items(PackagingId);
		CREATE INDEX IF NOT EXISTS IX_Items_SupplierId ON Items(SupplierId);
		""";
		command.ExecuteNonQuery();
		transaction.Commit();

		void AddColumnIfMissing(string columnName, string referenceTable)
		{
			if (TableHasColumn(connection, "Items", columnName)) return;
			using var alterCommand = connection.CreateCommand();
			alterCommand.Transaction = transaction;
			alterCommand.CommandText = $"ALTER TABLE Items ADD COLUMN {columnName} INTEGER NULL REFERENCES {referenceTable}(Id);";
			alterCommand.ExecuteNonQuery();
		}
	}

	private static void MigrateToReasonCodes(SqliteConnection connection)
	{
		CreateReasonCodesTable(connection);
		CreateDefaultReasonCodes(connection);
		using var transaction = connection.BeginTransaction();
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
		"""
		ALTER TABLE StockMovements
			ADD COLUMN ReasonCodeId INTEGER NULL REFERENCES ReasonCodes(Id);
		CREATE INDEX IF NOT EXISTS IX_StockMovements_ReasonCodeId
			ON StockMovements(ReasonCodeId);
		""";
		command.ExecuteNonQuery();
		transaction.Commit();
	}

	private static void MigrateToWarehouseStructure(SqliteConnection connection)
	{
		using (var disableForeignKeys = connection.CreateCommand())
		{
			disableForeignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
			disableForeignKeys.ExecuteNonQuery();
		}

		try
		{
			using var transaction = connection.BeginTransaction();
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText =
			"""
			CREATE TABLE Warehouses
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				Name TEXT NOT NULL UNIQUE,
				Description TEXT NULL,
				IsActive INTEGER NOT NULL DEFAULT 1,
				Version INTEGER NOT NULL DEFAULT 1
			);

			INSERT INTO Warehouses (Name, Description, IsActive)
			VALUES ('Main Warehouse', 'Migrated default warehouse', 1);

			CREATE TABLE StorageLocations
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				WarehouseId INTEGER NOT NULL,
				Name TEXT NOT NULL,
				Description TEXT NULL,
				IsActive INTEGER NOT NULL DEFAULT 1,
				Version INTEGER NOT NULL DEFAULT 1,
				UNIQUE(WarehouseId, Name),
				FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id)
			);

			INSERT INTO StorageLocations (Id, WarehouseId, Name, Description, IsActive, Version)
			SELECT l.Id, w.Id, l.Name, l.Description, l.IsActive, l.Version
			FROM Locations l
			CROSS JOIN Warehouses w
			WHERE w.Name = 'Main Warehouse';

			ALTER TABLE StockMovements RENAME TO StockMovementsWarehouseMigration;
			ALTER TABLE Inventories RENAME TO InventoriesWarehouseMigration;

			CREATE TABLE Inventories
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				ItemId INTEGER NOT NULL,
				PurposeId INTEGER NOT NULL,
				StorageLocationId INTEGER NOT NULL,
				IsActive INTEGER NOT NULL DEFAULT 1,
				Version INTEGER NOT NULL DEFAULT 1,
				UNIQUE(ItemId, PurposeId, StorageLocationId),
				FOREIGN KEY(ItemId) REFERENCES Items(Id),
				FOREIGN KEY(PurposeId) REFERENCES Purposes(Id),
				FOREIGN KEY(StorageLocationId) REFERENCES StorageLocations(Id)
			);

			INSERT INTO Inventories (Id, ItemId, PurposeId, StorageLocationId, IsActive, Version)
			SELECT Id, ItemId, PurposeId, LocationId, IsActive, Version
			FROM InventoriesWarehouseMigration;

			CREATE TABLE StockMovements
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				InventoryId INTEGER NOT NULL,
				MovementType INTEGER NOT NULL,
				TimestampUtc TEXT NOT NULL,
				Quantity INTEGER NOT NULL,
				UnitPrice REAL NULL,
				Reference TEXT NULL,
				Notes TEXT NULL,
				FOREIGN KEY(InventoryId) REFERENCES Inventories(Id)
			);

			INSERT INTO StockMovements
				(Id, InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes)
			SELECT Id, InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes
			FROM StockMovementsWarehouseMigration;

			DROP TABLE StockMovementsWarehouseMigration;
			DROP TABLE InventoriesWarehouseMigration;
			DROP TABLE Locations;

			CREATE INDEX IX_StorageLocations_WarehouseId_Name
				ON StorageLocations(WarehouseId, Name);
			CREATE INDEX IX_StockMovements_InventoryId_TimestampUtc
				ON StockMovements(InventoryId, TimestampUtc);
			""";
			command.ExecuteNonQuery();
			transaction.Commit();
		}
		finally
		{
			using var enableForeignKeys = connection.CreateCommand();
			enableForeignKeys.CommandText = "PRAGMA foreign_keys = ON;";
			enableForeignKeys.ExecuteNonQuery();
		}
	}

	private static void MigrateUsersToEmailAuthentication(SqliteConnection connection)
	{
		using var transaction = connection.BeginTransaction();
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
		"""
		ALTER TABLE Users RENAME TO UsersLegacy;

		CREATE TABLE Users
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			Email               TEXT NOT NULL COLLATE NOCASE UNIQUE,
			DisplayName         TEXT NOT NULL,
			PasswordHash        TEXT NOT NULL,
			IsAdministrator     INTEGER NOT NULL DEFAULT 0,
			IsActive            INTEGER NOT NULL DEFAULT 1,
			CreatedUtc          TEXT NOT NULL
		);

		INSERT INTO Users
		(
			Id,
			Email,
			DisplayName,
			PasswordHash,
			IsAdministrator,
			IsActive,
			CreatedUtc
		)
		SELECT
			Id,
			CASE
				WHEN instr(UserName, '@') > 0 THEN lower(trim(UserName))
				ELSE lower(trim(UserName)) || '@depot.local'
			END,
			DisplayName,
			'pbkdf2-sha256$210000$9vL0kVt/HZBUCpsJYjPW6Q==$B1lZ+NRxxR/E8kwIE5PK0wXR2BPDmFTeLiKYyAEuhaE=',
			IsAdministrator,
			IsActive,
			CreatedUtc
		FROM UsersLegacy;

		DROP TABLE UsersLegacy;
		""";
		command.ExecuteNonQuery();
		transaction.Commit();
	}

	private static void MigrateStockMovementsToInventory(
		SqliteConnection connection)
	{
		var hasLegacyItemId =
			TableHasColumn(
				connection,
				"StockMovements",
				"ItemId");

		using var transaction =
			connection.BeginTransaction();

		using var command =
			connection.CreateCommand();

		command.Transaction =
			transaction;

		command.CommandText =
			hasLegacyItemId
				? GetLegacyStockMovementMigrationSql()
				: GetCurrentStockMovementMigrationSql();

		command.ExecuteNonQuery();
		transaction.Commit();

		CreateStockMovementIndexes(
			connection);
	}

	private static void MigrateToAuditAndConcurrency(
		SqliteConnection connection)
	{
		var tables =
			new[]
			{
				"Items",
				"Purposes",
				"Locations",
				"Inventories",
				"Users"
			};

		var tablesWithoutVersion =
			tables
				.Where(table => !TableHasColumn(connection, table, "Version"))
				.ToList();

		using var transaction = connection.BeginTransaction();

		foreach (var table in tablesWithoutVersion)
		{
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText =
				$"ALTER TABLE {table} ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;";
			command.ExecuteNonQuery();
		}

		transaction.Commit();
		CreateAuditEntriesTable(connection);
	}

	private static bool TableHasColumn(
		SqliteConnection connection,
		string tableName,
		string columnName)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
			$"PRAGMA table_info({tableName});";

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			if (string.Equals(
				reader.GetString(1),
				columnName,
				StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static bool TableExists(SqliteConnection connection, string tableName)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $Name;";
		command.Parameters.AddWithValue("$Name", tableName);
		return Convert.ToInt32(command.ExecuteScalar()) == 1;
	}

	private static string GetLegacyStockMovementMigrationSql()
	{
		return
		"""
		INSERT OR IGNORE INTO Purposes (Name, Description, IsActive)
		VALUES ('Stock', 'Default stock purpose', 1);

		INSERT OR IGNORE INTO Locations (Name, Description, IsActive)
		VALUES ('Warehouse', 'Default warehouse location', 1);

		INSERT OR IGNORE INTO Inventories
		(
			ItemId,
			PurposeId,
			LocationId,
			IsActive
		)
		SELECT DISTINCT
			sm.ItemId,
			p.Id,
			l.Id,
			1
		FROM StockMovements sm
		INNER JOIN Purposes p
			ON p.Name = 'Stock'
		INNER JOIN Locations l
			ON l.Name = 'Warehouse'
		WHERE
			sm.InventoryId IS NULL
			AND NOT EXISTS
			(
				SELECT 1
				FROM Inventories inv
				WHERE inv.ItemId = sm.ItemId
			);

		ALTER TABLE StockMovements RENAME TO StockMovementsLegacy;

		CREATE TABLE StockMovements
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			InventoryId         INTEGER NOT NULL,
			MovementType        INTEGER NOT NULL,
			TimestampUtc        TEXT NOT NULL,
			Quantity            INTEGER NOT NULL,
			UnitPrice           REAL NULL,
			Reference           TEXT NULL,
			Notes               TEXT NULL,
			FOREIGN KEY(InventoryId)
				REFERENCES Inventories(Id)
		);

		INSERT INTO StockMovements
		(
			Id,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		)
		SELECT
			sm.Id,
			COALESCE
			(
				(
					SELECT inv.Id
					FROM Inventories inv
					WHERE
						inv.Id = sm.InventoryId
						AND inv.ItemId = sm.ItemId
				),
				(
					SELECT inv.Id
					FROM Inventories inv
					LEFT JOIN Purposes p
						ON p.Id = inv.PurposeId
					LEFT JOIN Locations l
						ON l.Id = inv.LocationId
					WHERE inv.ItemId = sm.ItemId
					ORDER BY
						CASE
							WHEN p.Name = 'Stock' AND l.Name = 'Warehouse' THEN 0
							ELSE 1
						END,
						inv.Id
					LIMIT 1
				)
			),
			sm.MovementType,
			sm.TimestampUtc,
			sm.Quantity,
			sm.UnitPrice,
			sm.Reference,
			sm.Notes
		FROM StockMovementsLegacy sm;

		DROP TABLE StockMovementsLegacy;
		""";
	}

	private static string GetCurrentStockMovementMigrationSql()
	{
		return
		"""
		ALTER TABLE StockMovements RENAME TO StockMovementsLegacy;

		CREATE TABLE StockMovements
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			InventoryId         INTEGER NOT NULL,
			MovementType        INTEGER NOT NULL,
			TimestampUtc        TEXT NOT NULL,
			Quantity            INTEGER NOT NULL,
			UnitPrice           REAL NULL,
			Reference           TEXT NULL,
			Notes               TEXT NULL,
			FOREIGN KEY(InventoryId)
				REFERENCES Inventories(Id)
		);

		INSERT INTO StockMovements
		(
			Id,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		)
		SELECT
			Id,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		FROM StockMovementsLegacy;

		DROP TABLE StockMovementsLegacy;
		""";
	}
}
