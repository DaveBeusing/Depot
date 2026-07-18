// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

namespace Depot.Data;

public sealed class SqlServerDatabase : IDatabaseInitializer
{
	private readonly SqlServerConnectionFactory _connectionFactory;

	public SqlServerDatabase(SqlServerConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public void Initialize()
	{
		EnsureDatabaseExists();
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connection.BeginTransaction();
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = SchemaSql;
		command.Parameters.AddWithValue(
			"@DefaultPasswordHash",
			"pbkdf2-sha256$210000$9vL0kVt/HZBUCpsJYjPW6Q==$B1lZ+NRxxR/E8kwIE5PK0wXR2BPDmFTeLiKYyAEuhaE=");
		command.Parameters.AddWithValue("@CurrentVersion", DatabaseVersion.CurrentVersion);
		command.ExecuteNonQuery();
		command.CommandText = ProcurementSql;
		command.Parameters.Clear();
		command.ExecuteNonQuery();

		command.CommandText = "SELECT Version FROM DatabaseInfo WHERE Id = 1;";
		var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
		if (version == 8)
		{
			MigrateToWarehouseStructure(command);
			version = 9;
		}
		if (version == 9)
		{
			MigrateToReasonCodes(command);
			version = 10;
		}
		if (version == 10)
		{
			MigrateToNormalizedItemMasterData(command);
			version = 11;
		}
		if (version == 11)
		{
			MigrateToSupplierManagement(command);
			version = 12;
		}
		if (version == 12)
		{
			MigrateSupplierAccountFields(command);
			version = 13;
		}
		if (version == 13)
		{
			MigrateSupplierClassification(command);
			version = 14;
		}
		if (version == 14)
		{
			MigrateToProcurement(command);
			version = 15;
		}
		if (version != DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"SQL Server schema version '{version}' is not supported. Expected '{DatabaseVersion.CurrentVersion}'.");
		}

		transaction.Commit();
	}

	private static void MigrateToProcurement(System.Data.Common.DbCommand command)
	{
		command.CommandText = ProcurementSql + " UPDATE DatabaseInfo SET Version = 15 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateSupplierClassification(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF OBJECT_ID(N'SupplierCategories', N'U') IS NULL CREATE TABLE SupplierCategories (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
		IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'IT Hardware') INSERT INTO SupplierCategories (Name) VALUES (N'IT Hardware');
		IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'ProAV') INSERT INTO SupplierCategories (Name) VALUES (N'ProAV');
		IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'Licensing') INSERT INTO SupplierCategories (Name) VALUES (N'Licensing');
		IF COL_LENGTH(N'Suppliers', N'AccountNumber') IS NULL ALTER TABLE Suppliers ADD AccountNumber bigint NULL;
		IF COL_LENGTH(N'Suppliers', N'SupplierCategoryId') IS NULL ALTER TABLE Suppliers ADD SupplierCategoryId bigint NULL;
		IF COL_LENGTH(N'Suppliers', N'SepaMandate') IS NULL ALTER TABLE Suppliers ADD SepaMandate nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'Quality') IS NULL ALTER TABLE Suppliers ADD Quality int NOT NULL CONSTRAINT DF_Suppliers_Quality DEFAULT 100;
		UPDATE Suppliers SET AccountNumber = Id WHERE AccountNumber IS NULL OR AccountNumber <= 0;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Suppliers_AccountNumber' AND object_id = OBJECT_ID(N'Suppliers')) CREATE UNIQUE INDEX UX_Suppliers_AccountNumber ON Suppliers(AccountNumber);
		IF COL_LENGTH(N'Suppliers', N'CategoryId') IS NOT NULL
		BEGIN
			EXEC(N'INSERT INTO SupplierCategories (Name, Description) SELECT DISTINCT c.Name, c.Description FROM Suppliers s INNER JOIN Categories c ON c.Id = s.CategoryId WHERE s.CategoryId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SupplierCategories sc WHERE sc.Name = c.Name);');
			EXEC(N'UPDATE s SET SupplierCategoryId = sc.Id FROM Suppliers s INNER JOIN Categories c ON c.Id = s.CategoryId INNER JOIN SupplierCategories sc ON sc.Name = c.Name WHERE s.SupplierCategoryId IS NULL;');
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Suppliers_SupplierCategories') ALTER TABLE Suppliers ADD CONSTRAINT FK_Suppliers_SupplierCategories FOREIGN KEY (SupplierCategoryId) REFERENCES SupplierCategories(Id);
		UPDATE DatabaseInfo SET Version = 14 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateSupplierAccountFields(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		DECLARE @HadNumericLoyalty bit = CASE WHEN COL_LENGTH(N'Suppliers', N'Loyalty') IS NULL THEN 0 ELSE 1 END;
		IF COL_LENGTH(N'Suppliers', N'CustomerNumber') IS NULL ALTER TABLE Suppliers ADD CustomerNumber nvarchar(100) NULL;
		IF COL_LENGTH(N'Suppliers', N'Loyalty') IS NULL ALTER TABLE Suppliers ADD Loyalty int NOT NULL CONSTRAINT DF_Suppliers_Loyalty DEFAULT 100;
		IF @HadNumericLoyalty = 0 AND COL_LENGTH(N'Suppliers', N'IsLoyal') IS NOT NULL EXEC(N'UPDATE Suppliers SET Loyalty = CASE WHEN IsLoyal = 1 THEN 100 ELSE 0 END;');
		UPDATE DatabaseInfo SET Version = 13 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToSupplierManagement(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'Suppliers', N'SupplierNumber') IS NULL ALTER TABLE Suppliers ADD SupplierNumber nvarchar(50) NULL;
		IF COL_LENGTH(N'Suppliers', N'Contact') IS NULL ALTER TABLE Suppliers ADD Contact nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'Email') IS NULL ALTER TABLE Suppliers ADD Email nvarchar(320) NULL;
		IF COL_LENGTH(N'Suppliers', N'Phone') IS NULL ALTER TABLE Suppliers ADD Phone nvarchar(100) NULL;
		IF COL_LENGTH(N'Suppliers', N'Address') IS NULL ALTER TABLE Suppliers ADD Address nvarchar(1000) NULL;
		IF COL_LENGTH(N'Suppliers', N'RmaTerms') IS NULL ALTER TABLE Suppliers ADD RmaTerms nvarchar(2000) NULL;
		IF COL_LENGTH(N'Suppliers', N'Url') IS NULL ALTER TABLE Suppliers ADD Url nvarchar(500) NULL;
		IF COL_LENGTH(N'Suppliers', N'PaymentTerm') IS NULL ALTER TABLE Suppliers ADD PaymentTerm nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'Iban') IS NULL ALTER TABLE Suppliers ADD Iban nvarchar(34) NULL;
		IF COL_LENGTH(N'Suppliers', N'AccountName') IS NULL ALTER TABLE Suppliers ADD AccountName nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'VatNumber') IS NULL ALTER TABLE Suppliers ADD VatNumber nvarchar(50) NULL;
		IF COL_LENGTH(N'Suppliers', N'CategoryId') IS NULL ALTER TABLE Suppliers ADD CategoryId bigint NULL;
		IF COL_LENGTH(N'Suppliers', N'IsLoyal') IS NULL ALTER TABLE Suppliers ADD IsLoyal bit NOT NULL CONSTRAINT DF_Suppliers_IsLoyal DEFAULT 0;
		IF COL_LENGTH(N'Suppliers', N'Notes') IS NULL ALTER TABLE Suppliers ADD Notes nvarchar(4000) NULL;
		UPDATE Suppliers SET SupplierNumber = N'SUP-' + CONVERT(nvarchar(20), Id) WHERE SupplierNumber IS NULL OR LTRIM(RTRIM(SupplierNumber)) = N'';
		IF COL_LENGTH(N'Suppliers', N'Description') IS NOT NULL EXEC(N'UPDATE Suppliers SET Notes = Description WHERE Notes IS NULL AND Description IS NOT NULL;');
		ALTER TABLE Suppliers ALTER COLUMN SupplierNumber nvarchar(50) NOT NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Suppliers_SupplierNumber' AND object_id = OBJECT_ID(N'Suppliers')) CREATE UNIQUE INDEX UX_Suppliers_SupplierNumber ON Suppliers(SupplierNumber);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Suppliers_Categories') ALTER TABLE Suppliers ADD CONSTRAINT FK_Suppliers_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id);
		IF OBJECT_ID(N'SupplierItems', N'U') IS NULL
		BEGIN
			CREATE TABLE SupplierItems (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierId bigint NOT NULL, ItemId bigint NOT NULL, SupplierPartNumber nvarchar(200) NOT NULL, PurchasePrice decimal(18,2) NOT NULL DEFAULT 0, LeadTimeDays int NOT NULL DEFAULT 0, MinimumOrderQuantity decimal(18,3) NOT NULL DEFAULT 1, IsPreferredSupplier bit NOT NULL DEFAULT 0, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_SupplierItems_Context UNIQUE (SupplierId, ItemId), CONSTRAINT FK_SupplierItems_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_SupplierItems_Items FOREIGN KEY (ItemId) REFERENCES Items(Id));
			CREATE INDEX IX_SupplierItems_SupplierId ON SupplierItems(SupplierId); CREATE INDEX IX_SupplierItems_ItemId ON SupplierItems(ItemId);
		END;
		INSERT INTO SupplierItems (SupplierId, ItemId, SupplierPartNumber, PurchasePrice, LeadTimeDays, MinimumOrderQuantity, IsPreferredSupplier)
		SELECT i.SupplierId, i.Id, i.PartNumber, 0, 0, 1, 1 FROM Items i WHERE i.SupplierId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SupplierItems si WHERE si.SupplierId = i.SupplierId AND si.ItemId = i.Id);
		UPDATE DatabaseInfo SET Version = 12 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToNormalizedItemMasterData(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		INSERT INTO Manufacturers (Name) SELECT DISTINCT LTRIM(RTRIM(i.Manufacturer)) FROM Items i WHERE i.Manufacturer IS NOT NULL AND LTRIM(RTRIM(i.Manufacturer)) <> '' AND NOT EXISTS (SELECT 1 FROM Manufacturers m WHERE m.Name = LTRIM(RTRIM(i.Manufacturer)));
		INSERT INTO Categories (Name) SELECT DISTINCT LTRIM(RTRIM(i.Category)) FROM Items i WHERE i.Category IS NOT NULL AND LTRIM(RTRIM(i.Category)) <> '' AND NOT EXISTS (SELECT 1 FROM Categories c WHERE c.Name = LTRIM(RTRIM(i.Category)));
		IF COL_LENGTH(N'Items', N'ManufacturerId') IS NULL ALTER TABLE Items ADD ManufacturerId bigint NULL;
		IF COL_LENGTH(N'Items', N'CategoryId') IS NULL ALTER TABLE Items ADD CategoryId bigint NULL;
		IF COL_LENGTH(N'Items', N'UnitOfMeasureId') IS NULL ALTER TABLE Items ADD UnitOfMeasureId bigint NULL;
		IF COL_LENGTH(N'Items', N'PackagingId') IS NULL ALTER TABLE Items ADD PackagingId bigint NULL;
		IF COL_LENGTH(N'Items', N'SupplierId') IS NULL ALTER TABLE Items ADD SupplierId bigint NULL;
		UPDATE i SET ManufacturerId = m.Id FROM Items i INNER JOIN Manufacturers m ON m.Name = LTRIM(RTRIM(i.Manufacturer));
		UPDATE i SET CategoryId = c.Id FROM Items i INNER JOIN Categories c ON c.Name = LTRIM(RTRIM(i.Category));
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Manufacturers') ALTER TABLE Items ADD CONSTRAINT FK_Items_Manufacturers FOREIGN KEY (ManufacturerId) REFERENCES Manufacturers(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Categories') ALTER TABLE Items ADD CONSTRAINT FK_Items_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_UnitsOfMeasure') ALTER TABLE Items ADD CONSTRAINT FK_Items_UnitsOfMeasure FOREIGN KEY (UnitOfMeasureId) REFERENCES UnitsOfMeasure(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Packagings') ALTER TABLE Items ADD CONSTRAINT FK_Items_Packagings FOREIGN KEY (PackagingId) REFERENCES Packagings(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Suppliers') ALTER TABLE Items ADD CONSTRAINT FK_Items_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_ManufacturerId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_ManufacturerId ON Items(ManufacturerId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_CategoryId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_CategoryId ON Items(CategoryId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_UnitOfMeasureId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_UnitOfMeasureId ON Items(UnitOfMeasureId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_PackagingId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_PackagingId ON Items(PackagingId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_SupplierId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_SupplierId ON Items(SupplierId);
		UPDATE DatabaseInfo SET Version = 11 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToReasonCodes(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'StockMovements', N'ReasonCodeId') IS NULL
			ALTER TABLE StockMovements ADD ReasonCodeId bigint NULL;

		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockMovements_ReasonCodes')
			ALTER TABLE StockMovements ADD CONSTRAINT FK_StockMovements_ReasonCodes
				FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id);

		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovements_ReasonCodeId' AND object_id = OBJECT_ID(N'StockMovements'))
			CREATE INDEX IX_StockMovements_ReasonCodeId ON StockMovements(ReasonCodeId);

		UPDATE DatabaseInfo SET Version = 10 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToWarehouseStructure(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		DELETE FROM StorageLocations;
		SET IDENTITY_INSERT StorageLocations ON;
		INSERT INTO StorageLocations (Id, WarehouseId, Name, Description, IsActive, Version)
		SELECT l.Id, w.Id, l.Name, l.Description, l.IsActive, l.Version
		FROM Locations l
		CROSS JOIN Warehouses w
		WHERE w.Name = N'Main Warehouse';
		SET IDENTITY_INSERT StorageLocations OFF;

		ALTER TABLE Inventories ADD StorageLocationId bigint NULL;
		UPDATE Inventories SET StorageLocationId = LocationId;
		ALTER TABLE Inventories ALTER COLUMN StorageLocationId bigint NOT NULL;
		ALTER TABLE Inventories DROP CONSTRAINT FK_Inventories_Locations;
		ALTER TABLE Inventories DROP CONSTRAINT UQ_Inventories_Context;
		ALTER TABLE Inventories DROP COLUMN LocationId;
		ALTER TABLE Inventories ADD CONSTRAINT UQ_Inventories_Context UNIQUE (ItemId, PurposeId, StorageLocationId);
		ALTER TABLE Inventories ADD CONSTRAINT FK_Inventories_StorageLocations
			FOREIGN KEY (StorageLocationId) REFERENCES StorageLocations(Id);
		DROP TABLE Locations;
		UPDATE DatabaseInfo SET Version = 9 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private void EnsureDatabaseExists()
	{
		using var connection = _connectionFactory.CreateMasterConnection();
		connection.Open();
		using var existsCommand = connection.CreateCommand();
		existsCommand.CommandText = "SELECT DB_ID(@DatabaseName);";
		existsCommand.Parameters.AddWithValue("@DatabaseName", _connectionFactory.DatabaseName);
		var databaseId = existsCommand.ExecuteScalar();
		if (databaseId is not null and not DBNull)
		{
			return;
		}

		var escapedName = _connectionFactory.DatabaseName.Replace("]", "]]", StringComparison.Ordinal);
		using var createCommand = connection.CreateCommand();
		createCommand.CommandText = $"CREATE DATABASE [{escapedName}];";
		createCommand.ExecuteNonQuery();
	}

	private const string ProcurementSql =
	"""
	IF OBJECT_ID(N'PurchaseOrders', N'U') IS NULL
	BEGIN
		CREATE TABLE PurchaseOrders (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, OrderNumber nvarchar(50) NOT NULL UNIQUE, SupplierId bigint NOT NULL, OrderDate nvarchar(10) NOT NULL, ExpectedDeliveryDate nvarchar(10) NULL, Notes nvarchar(4000) NULL, Status int NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_PurchaseOrders_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id));
		CREATE INDEX IX_PurchaseOrders_SupplierId_Status ON PurchaseOrders(SupplierId, Status); CREATE INDEX IX_PurchaseOrders_OrderDate ON PurchaseOrders(OrderDate);
	END;
	IF OBJECT_ID(N'PurchaseOrderLines', N'U') IS NULL
	BEGIN
		CREATE TABLE PurchaseOrderLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, PurchaseOrderId bigint NOT NULL, LineNumber int NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, UnitPrice decimal(18,2) NOT NULL DEFAULT 0, ReceivedQuantity int NOT NULL DEFAULT 0, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_PurchaseOrderLines_Number UNIQUE (PurchaseOrderId, LineNumber), CONSTRAINT UQ_PurchaseOrderLines_Item UNIQUE (PurchaseOrderId, ItemId), CONSTRAINT CK_PurchaseOrderLines_Quantity CHECK (Quantity > 0 AND ReceivedQuantity >= 0 AND ReceivedQuantity <= Quantity), CONSTRAINT FK_PurchaseOrderLines_Orders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT FK_PurchaseOrderLines_Items FOREIGN KEY (ItemId) REFERENCES Items(Id));
		CREATE INDEX IX_PurchaseOrderLines_ItemId ON PurchaseOrderLines(ItemId);
	END;
	IF OBJECT_ID(N'GoodsReceipts', N'U') IS NULL
	BEGIN
		CREATE TABLE GoodsReceipts (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, ReceiptNumber nvarchar(50) NOT NULL UNIQUE, PurchaseOrderId bigint NOT NULL, ReceiptDate nvarchar(10) NOT NULL, InvoiceNumber nvarchar(100) NOT NULL, InvoiceDate nvarchar(10) NOT NULL, InvoiceDocumentPath nvarchar(1000) NULL, Notes nvarchar(4000) NULL, CONSTRAINT FK_GoodsReceipts_Orders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id));
		CREATE INDEX IX_GoodsReceipts_PurchaseOrderId ON GoodsReceipts(PurchaseOrderId);
	END;
	IF OBJECT_ID(N'GoodsReceiptLines', N'U') IS NULL
	BEGIN
		CREATE TABLE GoodsReceiptLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, GoodsReceiptId bigint NOT NULL, PurchaseOrderLineId bigint NOT NULL, InventoryId bigint NOT NULL, Quantity int NOT NULL, CONSTRAINT UQ_GoodsReceiptLines_OrderLine UNIQUE (GoodsReceiptId, PurchaseOrderLineId), CONSTRAINT CK_GoodsReceiptLines_Quantity CHECK (Quantity > 0), CONSTRAINT FK_GoodsReceiptLines_Receipts FOREIGN KEY (GoodsReceiptId) REFERENCES GoodsReceipts(Id), CONSTRAINT FK_GoodsReceiptLines_OrderLines FOREIGN KEY (PurchaseOrderLineId) REFERENCES PurchaseOrderLines(Id), CONSTRAINT FK_GoodsReceiptLines_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id));
		CREATE INDEX IX_GoodsReceiptLines_InventoryId ON GoodsReceiptLines(InventoryId);
	END;
	""";

	private const string SchemaSql =
	"""
	IF OBJECT_ID(N'DatabaseInfo', N'U') IS NULL
	BEGIN
		CREATE TABLE DatabaseInfo
		(
			Id int NOT NULL CONSTRAINT PK_DatabaseInfo PRIMARY KEY,
			Version int NOT NULL,
			CONSTRAINT CK_DatabaseInfo_SingleRow CHECK (Id = 1)
		);
		INSERT INTO DatabaseInfo (Id, Version) VALUES (1, @CurrentVersion);
	END;

	IF OBJECT_ID(N'Manufacturers', N'U') IS NULL CREATE TABLE Manufacturers (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'Categories', N'U') IS NULL CREATE TABLE Categories (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'UnitsOfMeasure', N'U') IS NULL CREATE TABLE UnitsOfMeasure (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'Packagings', N'U') IS NULL CREATE TABLE Packagings (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'SupplierCategories', N'U') IS NULL CREATE TABLE SupplierCategories (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'IT Hardware') INSERT INTO SupplierCategories (Name) VALUES (N'IT Hardware');
	IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'ProAV') INSERT INTO SupplierCategories (Name) VALUES (N'ProAV');
	IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'Licensing') INSERT INTO SupplierCategories (Name) VALUES (N'Licensing');
	IF OBJECT_ID(N'Suppliers', N'U') IS NULL CREATE TABLE Suppliers (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierNumber nvarchar(50) NOT NULL UNIQUE, AccountNumber bigint NOT NULL UNIQUE, CustomerNumber nvarchar(100) NULL, Name nvarchar(200) NOT NULL UNIQUE, Contact nvarchar(200) NULL, Email nvarchar(320) NULL, Phone nvarchar(100) NULL, Address nvarchar(1000) NULL, RmaTerms nvarchar(2000) NULL, Url nvarchar(500) NULL, PaymentTerm nvarchar(200) NULL, Iban nvarchar(34) NULL, AccountName nvarchar(200) NULL, SepaMandate nvarchar(200) NULL, VatNumber nvarchar(50) NULL, SupplierCategoryId bigint NULL, Loyalty int NOT NULL DEFAULT 100, Quality int NOT NULL DEFAULT 100, Notes nvarchar(4000) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_Suppliers_SupplierCategories FOREIGN KEY (SupplierCategoryId) REFERENCES SupplierCategories(Id));

	IF OBJECT_ID(N'Items', N'U') IS NULL
	BEGIN
		CREATE TABLE Items
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Items PRIMARY KEY,
			PartNumber nvarchar(200) NOT NULL CONSTRAINT UQ_Items_PartNumber UNIQUE,
			Description nvarchar(500) NOT NULL,
			Manufacturer nvarchar(200) NULL,
			Category nvarchar(200) NULL,
			ManufacturerId bigint NULL,
			CategoryId bigint NULL,
			UnitOfMeasureId bigint NULL,
			PackagingId bigint NULL,
			SupplierId bigint NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Items_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Items_Version DEFAULT 1,
			CONSTRAINT FK_Items_Manufacturers FOREIGN KEY (ManufacturerId) REFERENCES Manufacturers(Id),
			CONSTRAINT FK_Items_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id),
			CONSTRAINT FK_Items_UnitsOfMeasure FOREIGN KEY (UnitOfMeasureId) REFERENCES UnitsOfMeasure(Id),
			CONSTRAINT FK_Items_Packagings FOREIGN KEY (PackagingId) REFERENCES Packagings(Id),
			CONSTRAINT FK_Items_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
		);
		CREATE INDEX IX_Items_ManufacturerId ON Items(ManufacturerId);
		CREATE INDEX IX_Items_CategoryId ON Items(CategoryId);
		CREATE INDEX IX_Items_UnitOfMeasureId ON Items(UnitOfMeasureId);
		CREATE INDEX IX_Items_PackagingId ON Items(PackagingId);
		CREATE INDEX IX_Items_SupplierId ON Items(SupplierId);
	END;

	IF OBJECT_ID(N'SupplierItems', N'U') IS NULL
	BEGIN
		CREATE TABLE SupplierItems (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierId bigint NOT NULL, ItemId bigint NOT NULL, SupplierPartNumber nvarchar(200) NOT NULL, PurchasePrice decimal(18,2) NOT NULL DEFAULT 0, LeadTimeDays int NOT NULL DEFAULT 0, MinimumOrderQuantity decimal(18,3) NOT NULL DEFAULT 1, IsPreferredSupplier bit NOT NULL DEFAULT 0, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_SupplierItems_Context UNIQUE (SupplierId, ItemId), CONSTRAINT FK_SupplierItems_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_SupplierItems_Items FOREIGN KEY (ItemId) REFERENCES Items(Id));
		CREATE INDEX IX_SupplierItems_SupplierId ON SupplierItems(SupplierId); CREATE INDEX IX_SupplierItems_ItemId ON SupplierItems(ItemId);
	END;

	IF OBJECT_ID(N'Purposes', N'U') IS NULL
		CREATE TABLE Purposes
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Purposes PRIMARY KEY,
			Name nvarchar(200) NOT NULL CONSTRAINT UQ_Purposes_Name UNIQUE,
			Description nvarchar(500) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Purposes_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Purposes_Version DEFAULT 1
		);

	IF OBJECT_ID(N'Warehouses', N'U') IS NULL
		CREATE TABLE Warehouses
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
			Name nvarchar(200) NOT NULL CONSTRAINT UQ_Warehouses_Name UNIQUE,
			Description nvarchar(500) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Warehouses_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Warehouses_Version DEFAULT 1
		);

	IF OBJECT_ID(N'ReasonCodes', N'U') IS NULL
		CREATE TABLE ReasonCodes
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReasonCodes PRIMARY KEY,
			Name nvarchar(200) NOT NULL CONSTRAINT UQ_ReasonCodes_Name UNIQUE,
			Description nvarchar(500) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_ReasonCodes_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_ReasonCodes_Version DEFAULT 1
		);

	IF OBJECT_ID(N'StorageLocations', N'U') IS NULL
		CREATE TABLE StorageLocations
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StorageLocations PRIMARY KEY,
			WarehouseId bigint NOT NULL,
			Name nvarchar(200) NOT NULL,
			Description nvarchar(500) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_StorageLocations_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_StorageLocations_Version DEFAULT 1,
			CONSTRAINT UQ_StorageLocations_Warehouse_Name UNIQUE (WarehouseId, Name),
			CONSTRAINT FK_StorageLocations_Warehouses FOREIGN KEY (WarehouseId) REFERENCES Warehouses(Id)
		);

	IF OBJECT_ID(N'Inventories', N'U') IS NULL
		CREATE TABLE Inventories
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Inventories PRIMARY KEY,
			ItemId bigint NOT NULL,
			PurposeId bigint NOT NULL,
			StorageLocationId bigint NOT NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Inventories_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Inventories_Version DEFAULT 1,
			CONSTRAINT UQ_Inventories_Context UNIQUE (ItemId, PurposeId, StorageLocationId),
			CONSTRAINT FK_Inventories_Items FOREIGN KEY (ItemId) REFERENCES Items(Id),
			CONSTRAINT FK_Inventories_Purposes FOREIGN KEY (PurposeId) REFERENCES Purposes(Id),
			CONSTRAINT FK_Inventories_StorageLocations FOREIGN KEY (StorageLocationId) REFERENCES StorageLocations(Id)
		);

	IF OBJECT_ID(N'Users', N'U') IS NULL
		CREATE TABLE Users
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
			Email nvarchar(320) NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
			DisplayName nvarchar(200) NOT NULL,
			PasswordHash nvarchar(500) NOT NULL,
			IsAdministrator bit NOT NULL CONSTRAINT DF_Users_IsAdministrator DEFAULT 0,
			IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
			CreatedUtc nvarchar(40) NOT NULL,
			Version bigint NOT NULL CONSTRAINT DF_Users_Version DEFAULT 1
		);

	IF OBJECT_ID(N'StockMovements', N'U') IS NULL
		CREATE TABLE StockMovements
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
			InventoryId bigint NOT NULL,
			ReasonCodeId bigint NULL,
			MovementType int NOT NULL,
			TimestampUtc nvarchar(40) NOT NULL,
			Quantity int NOT NULL,
			UnitPrice decimal(18,2) NULL,
			Reference nvarchar(200) NULL,
			Notes nvarchar(2000) NULL,
			CONSTRAINT FK_StockMovements_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_StockMovements_ReasonCodes FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id)
		);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovements_InventoryId_TimestampUtc')
		CREATE INDEX IX_StockMovements_InventoryId_TimestampUtc ON StockMovements(InventoryId, TimestampUtc);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovements_ReasonCodeId')
		CREATE INDEX IX_StockMovements_ReasonCodeId ON StockMovements(ReasonCodeId);

	IF OBJECT_ID(N'AuditEntries', N'U') IS NULL
		CREATE TABLE AuditEntries
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditEntries PRIMARY KEY,
			TimestampUtc nvarchar(40) NOT NULL,
			UserId bigint NULL,
			UserEmail nvarchar(320) NOT NULL,
			EntityType nvarchar(200) NOT NULL,
			EntityId bigint NOT NULL,
			Action nvarchar(100) NOT NULL,
			BeforeJson nvarchar(max) NULL,
			AfterJson nvarchar(max) NULL,
			CONSTRAINT FK_AuditEntries_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL
		);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditEntries_TimestampUtc')
		CREATE INDEX IX_AuditEntries_TimestampUtc ON AuditEntries(TimestampUtc DESC);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditEntries_Entity')
		CREATE INDEX IX_AuditEntries_Entity ON AuditEntries(EntityType, EntityId, TimestampUtc DESC);

	IF NOT EXISTS (SELECT 1 FROM Purposes WHERE Name = N'Stock')
		INSERT INTO Purposes (Name, Description, IsActive) VALUES (N'Stock', N'Default stock purpose', 1);

	INSERT INTO ReasonCodes (Name, IsActive)
	SELECT defaults.Name, 1
	FROM (VALUES
		(N'Goods Receipt'), (N'Goods Issue'), (N'Inventory Correction'), (N'Damaged'), (N'Lost'),
		(N'Returned'), (N'Consumed'), (N'Demo'), (N'Repair'), (N'Transfer')) defaults(Name)
	WHERE NOT EXISTS (SELECT 1 FROM ReasonCodes existing WHERE existing.Name = defaults.Name);

	IF NOT EXISTS (SELECT 1 FROM Warehouses WHERE Name = N'Main Warehouse')
		INSERT INTO Warehouses (Name, Description, IsActive) VALUES (N'Main Warehouse', N'Default Depot warehouse', 1);

	IF NOT EXISTS (SELECT 1 FROM StorageLocations sl INNER JOIN Warehouses w ON w.Id = sl.WarehouseId WHERE w.Name = N'Main Warehouse' AND sl.Name = N'Default')
		INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive)
		SELECT Id, N'Default', N'Default storage location', 1 FROM Warehouses WHERE Name = N'Main Warehouse';

	IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = N'admin@depot.local')
		INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, IsActive, CreatedUtc)
		VALUES
		(N'admin@depot.local', N'Administrator', @DefaultPasswordHash, 1, 1, CONVERT(nvarchar(40), SYSUTCDATETIME(), 127));
	""";
}
