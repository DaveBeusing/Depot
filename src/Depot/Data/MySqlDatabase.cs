// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

namespace Depot.Data;

public sealed class MySqlDatabase : IDatabaseInitializer
{
	private readonly MySqlConnectionFactory _connectionFactory;

	public MySqlDatabase(MySqlConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public void Initialize()
	{
		EnsureDatabaseExists();
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var lockCommand = connection.CreateCommand();
		lockCommand.CommandText = "SELECT GET_LOCK('Depot.SchemaMigration', 30);";
		if (Convert.ToInt32(lockCommand.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
		{
			throw new InvalidOperationException("The MySQL/MariaDB schema migration lock could not be acquired.");
		}

		try
		{
			using var command = connection.CreateCommand();
			command.CommandText = SchemaSql;
			command.Parameters.AddWithValue("@CurrentVersion", DatabaseVersion.CurrentVersion);
			command.Parameters.AddWithValue(
				"@DefaultPasswordHash",
				"pbkdf2-sha256$210000$9vL0kVt/HZBUCpsJYjPW6Q==$B1lZ+NRxxR/E8kwIE5PK0wXR2BPDmFTeLiKYyAEuhaE=");
			command.ExecuteNonQuery();
			command.CommandText = ProcurementSql;
			command.Parameters.Clear();
			command.ExecuteNonQuery();

			command.CommandText = "SELECT Version FROM DatabaseInfo WHERE Id = 1;";
			command.Parameters.Clear();
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
					$"MySQL/MariaDB schema version '{version}' is not supported. Expected '{DatabaseVersion.CurrentVersion}'.");
			}
		}
		finally
		{
			using var releaseCommand = connection.CreateCommand();
			releaseCommand.CommandText = "SELECT RELEASE_LOCK('Depot.SchemaMigration');";
			releaseCommand.ExecuteScalar();
		}
	}

	private static void MigrateToProcurement(System.Data.Common.DbCommand command)
	{
		Execute(command, ProcurementSql);
		Execute(command, "UPDATE DatabaseInfo SET Version = 15 WHERE Id = 1;");
	}

	private static void MigrateSupplierClassification(System.Data.Common.DbCommand command)
	{
		Execute(command, "CREATE TABLE IF NOT EXISTS SupplierCategories (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Name varchar(200) NOT NULL UNIQUE, Description varchar(500) NULL, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;");
		Execute(command, "INSERT IGNORE INTO SupplierCategories (Name) VALUES ('IT Hardware'), ('ProAV'), ('Licensing');");
		if (!ColumnExists(command, "Suppliers", "AccountNumber")) Execute(command, "ALTER TABLE Suppliers ADD COLUMN AccountNumber bigint NULL;");
		if (!ColumnExists(command, "Suppliers", "SupplierCategoryId")) Execute(command, "ALTER TABLE Suppliers ADD COLUMN SupplierCategoryId bigint NULL;");
		if (!ColumnExists(command, "Suppliers", "SepaMandate")) Execute(command, "ALTER TABLE Suppliers ADD COLUMN SepaMandate varchar(200) NULL;");
		if (!ColumnExists(command, "Suppliers", "Quality")) Execute(command, "ALTER TABLE Suppliers ADD COLUMN Quality int NOT NULL DEFAULT 100;");
		Execute(command, "UPDATE Suppliers SET AccountNumber = Id WHERE AccountNumber IS NULL OR AccountNumber <= 0;");
		if (!IndexExists(command, "Suppliers", "UX_Suppliers_AccountNumber")) Execute(command, "ALTER TABLE Suppliers ADD UNIQUE INDEX UX_Suppliers_AccountNumber (AccountNumber);");
		if (ColumnExists(command, "Suppliers", "CategoryId"))
		{
			Execute(command, "INSERT IGNORE INTO SupplierCategories (Name, Description) SELECT DISTINCT c.Name, c.Description FROM Suppliers s INNER JOIN Categories c ON c.Id = s.CategoryId WHERE s.CategoryId IS NOT NULL;");
			Execute(command, "UPDATE Suppliers s INNER JOIN Categories c ON c.Id = s.CategoryId INNER JOIN SupplierCategories sc ON sc.Name = c.Name SET s.SupplierCategoryId = sc.Id WHERE s.SupplierCategoryId IS NULL;");
		}
		if (!ConstraintExists(command, "Suppliers", "FK_Suppliers_SupplierCategories")) Execute(command, "ALTER TABLE Suppliers ADD CONSTRAINT FK_Suppliers_SupplierCategories FOREIGN KEY (SupplierCategoryId) REFERENCES SupplierCategories(Id);");
		Execute(command, "UPDATE DatabaseInfo SET Version = 14 WHERE Id = 1;");
	}

	private static void MigrateSupplierAccountFields(System.Data.Common.DbCommand command)
	{
		var hadNumericLoyalty = ColumnExists(command, "Suppliers", "Loyalty");
		if (!ColumnExists(command, "Suppliers", "CustomerNumber")) Execute(command, "ALTER TABLE Suppliers ADD COLUMN CustomerNumber varchar(100) NULL;");
		if (!hadNumericLoyalty) Execute(command, "ALTER TABLE Suppliers ADD COLUMN Loyalty int NOT NULL DEFAULT 100;");
		if (!hadNumericLoyalty && ColumnExists(command, "Suppliers", "IsLoyal")) Execute(command, "UPDATE Suppliers SET Loyalty = CASE WHEN IsLoyal = true THEN 100 ELSE 0 END;");
		Execute(command, "UPDATE DatabaseInfo SET Version = 13 WHERE Id = 1;");
	}

	private static void MigrateToSupplierManagement(System.Data.Common.DbCommand command)
	{
		Add("SupplierNumber", "varchar(50) NULL"); Add("Contact", "varchar(200) NULL"); Add("Email", "varchar(320) NULL");
		Add("Phone", "varchar(100) NULL"); Add("Address", "varchar(1000) NULL"); Add("RmaTerms", "text NULL");
		Add("Url", "varchar(500) NULL"); Add("PaymentTerm", "varchar(200) NULL"); Add("Iban", "varchar(34) NULL");
		Add("AccountName", "varchar(200) NULL"); Add("VatNumber", "varchar(50) NULL"); Add("CategoryId", "bigint NULL");
		Add("IsLoyal", "boolean NOT NULL DEFAULT false"); Add("Notes", "text NULL");
		Execute(command, "UPDATE Suppliers SET SupplierNumber = CONCAT('SUP-', Id) WHERE SupplierNumber IS NULL OR TRIM(SupplierNumber) = '';");
		if (ColumnExists(command, "Suppliers", "Description")) Execute(command, "UPDATE Suppliers SET Notes = Description WHERE Notes IS NULL AND Description IS NOT NULL;");
		Execute(command, "ALTER TABLE Suppliers MODIFY COLUMN SupplierNumber varchar(50) NOT NULL;");
		if (!IndexExists(command, "Suppliers", "UX_Suppliers_SupplierNumber")) Execute(command, "ALTER TABLE Suppliers ADD UNIQUE INDEX UX_Suppliers_SupplierNumber (SupplierNumber);");
		if (!ConstraintExists(command, "Suppliers", "FK_Suppliers_Categories")) Execute(command, "ALTER TABLE Suppliers ADD CONSTRAINT FK_Suppliers_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id);");
		Execute(command, "CREATE TABLE IF NOT EXISTS SupplierItems (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, SupplierId bigint NOT NULL, ItemId bigint NOT NULL, SupplierPartNumber varchar(200) NOT NULL, PurchasePrice decimal(18,2) NOT NULL DEFAULT 0, LeadTimeDays int NOT NULL DEFAULT 0, MinimumOrderQuantity decimal(18,3) NOT NULL DEFAULT 1, IsPreferredSupplier boolean NOT NULL DEFAULT false, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1, UNIQUE KEY UQ_SupplierItems_Context (SupplierId, ItemId), INDEX IX_SupplierItems_SupplierId (SupplierId), INDEX IX_SupplierItems_ItemId (ItemId), CONSTRAINT FK_SupplierItems_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_SupplierItems_Items FOREIGN KEY (ItemId) REFERENCES Items(Id)) ENGINE=InnoDB;");
		Execute(command, "INSERT IGNORE INTO SupplierItems (SupplierId, ItemId, SupplierPartNumber, PurchasePrice, LeadTimeDays, MinimumOrderQuantity, IsPreferredSupplier) SELECT SupplierId, Id, PartNumber, 0, 0, 1, true FROM Items WHERE SupplierId IS NOT NULL;");
		Execute(command, "UPDATE DatabaseInfo SET Version = 12 WHERE Id = 1;");

		void Add(string column, string definition) { if (!ColumnExists(command, "Suppliers", column)) Execute(command, $"ALTER TABLE Suppliers ADD COLUMN {column} {definition};"); }
	}

	private static void MigrateToNormalizedItemMasterData(System.Data.Common.DbCommand command)
	{
		Execute(command, "INSERT IGNORE INTO Manufacturers (Name) SELECT DISTINCT TRIM(Manufacturer) FROM Items WHERE Manufacturer IS NOT NULL AND TRIM(Manufacturer) <> ''; ");
		Execute(command, "INSERT IGNORE INTO Categories (Name) SELECT DISTINCT TRIM(Category) FROM Items WHERE Category IS NOT NULL AND TRIM(Category) <> ''; ");
		AddColumn("ManufacturerId", "Manufacturers");
		AddColumn("CategoryId", "Categories");
		AddColumn("UnitOfMeasureId", "UnitsOfMeasure");
		AddColumn("PackagingId", "Packagings");
		AddColumn("SupplierId", "Suppliers");
		Execute(command, "UPDATE Items i INNER JOIN Manufacturers m ON m.Name = TRIM(i.Manufacturer) SET i.ManufacturerId = m.Id;");
		Execute(command, "UPDATE Items i INNER JOIN Categories c ON c.Name = TRIM(i.Category) SET i.CategoryId = c.Id;");
		Execute(command, "UPDATE DatabaseInfo SET Version = 11 WHERE Id = 1;");

		void AddColumn(string column, string table)
		{
			if (!ColumnExists(command, "Items", column)) Execute(command, $"ALTER TABLE Items ADD COLUMN {column} bigint NULL;");
			EnsureIndex(command, "Items", $"IX_Items_{column}", column);
			var constraint = $"FK_Items_{table}";
			if (!ConstraintExists(command, "Items", constraint))
				Execute(command, $"ALTER TABLE Items ADD CONSTRAINT {constraint} FOREIGN KEY ({column}) REFERENCES {table}(Id);");
		}
	}

	private static void MigrateToReasonCodes(System.Data.Common.DbCommand command)
	{
		if (!ColumnExists(command, "StockMovements", "ReasonCodeId"))
		{
			Execute(command, "ALTER TABLE StockMovements ADD COLUMN ReasonCodeId bigint NULL;");
		}

		EnsureIndex(command, "StockMovements", "IX_StockMovements_ReasonCodeId", "ReasonCodeId");
		if (!ConstraintExists(command, "StockMovements", "FK_StockMovements_ReasonCodes"))
		{
			Execute(
				command,
				"""
				ALTER TABLE StockMovements ADD CONSTRAINT FK_StockMovements_ReasonCodes
					FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id);
				""");
		}

		Execute(command, "UPDATE DatabaseInfo SET Version = 10 WHERE Id = 1;");
	}

	private static void MigrateToWarehouseStructure(System.Data.Common.DbCommand command)
	{
		command.Parameters.Clear();
		var hasLocations = TableExists(command, "Locations");
		var hasLegacyLocationId = ColumnExists(command, "Inventories", "LocationId");
		var hasStorageLocationId = ColumnExists(command, "Inventories", "StorageLocationId");

		if (hasLocations && !hasStorageLocationId)
		{
			Execute(
				command,
				"""
				DELETE FROM StorageLocations;
				INSERT INTO StorageLocations (Id, WarehouseId, Name, Description, IsActive, Version)
				SELECT l.Id, w.Id, l.Name, l.Description, l.IsActive, l.Version
				FROM Locations l
				CROSS JOIN Warehouses w
				WHERE w.Name = 'Main Warehouse';
				""");
		}
		else if (hasLocations)
		{
			Execute(
				command,
				"""
				DELETE sl
				FROM StorageLocations sl
				INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
				WHERE w.Name = 'Main Warehouse'
				  AND sl.Name = 'Default'
				  AND NOT EXISTS (SELECT 1 FROM Locations l WHERE l.Name = 'Default');

				INSERT IGNORE INTO StorageLocations (Id, WarehouseId, Name, Description, IsActive, Version)
				SELECT l.Id, w.Id, l.Name, l.Description, l.IsActive, l.Version
				FROM Locations l
				CROSS JOIN Warehouses w
				WHERE w.Name = 'Main Warehouse';
				""");
		}

		if (!hasStorageLocationId)
		{
			Execute(command, "ALTER TABLE Inventories ADD COLUMN StorageLocationId bigint NULL;");
			hasStorageLocationId = true;
		}

		if (hasLegacyLocationId && hasStorageLocationId)
		{
			Execute(
				command,
				"UPDATE Inventories SET StorageLocationId = LocationId WHERE StorageLocationId IS NULL;");
			Execute(command, "ALTER TABLE Inventories MODIFY StorageLocationId bigint NOT NULL;");

			if (ConstraintExists(command, "Inventories", "FK_Inventories_Locations"))
			{
				Execute(command, "ALTER TABLE Inventories DROP FOREIGN KEY FK_Inventories_Locations;");
			}

			EnsureIndex(command, "Inventories", "IX_Inventories_ItemId", "ItemId");
			EnsureIndex(command, "Inventories", "IX_Inventories_PurposeId", "PurposeId");

			if (IndexExists(command, "Inventories", "UQ_Inventories_Context"))
			{
				Execute(command, "ALTER TABLE Inventories DROP INDEX UQ_Inventories_Context;");
			}

			Execute(command, "ALTER TABLE Inventories DROP COLUMN LocationId;");
		}

		if (!IndexExists(command, "Inventories", "UQ_Inventories_Context"))
		{
			Execute(
				command,
				"ALTER TABLE Inventories ADD CONSTRAINT UQ_Inventories_Context UNIQUE (ItemId, PurposeId, StorageLocationId);");
		}

		if (!ConstraintExists(command, "Inventories", "FK_Inventories_StorageLocations"))
		{
			EnsureIndex(
				command,
				"Inventories",
				"IX_Inventories_StorageLocationId",
				"StorageLocationId");
			Execute(
				command,
				"""
				ALTER TABLE Inventories ADD CONSTRAINT FK_Inventories_StorageLocations
					FOREIGN KEY (StorageLocationId) REFERENCES StorageLocations(Id);
				""");
		}

		if (hasLocations)
		{
			Execute(command, "DROP TABLE Locations;");
		}

		Execute(command, "UPDATE DatabaseInfo SET Version = 9 WHERE Id = 1;");
	}

	private static bool TableExists(System.Data.Common.DbCommand command, string tableName) =>
		MetadataExists(
			command,
			"""
			SELECT COUNT(*)
			FROM information_schema.TABLES
			WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Name;
			""",
			tableName);

	private static bool ColumnExists(
		System.Data.Common.DbCommand command,
		string tableName,
		string columnName) =>
		MetadataExists(
			command,
			"""
			SELECT COUNT(*)
			FROM information_schema.COLUMNS
			WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName AND COLUMN_NAME = @Name;
			""",
			columnName,
			tableName);

	private static bool ConstraintExists(
		System.Data.Common.DbCommand command,
		string tableName,
		string constraintName) =>
		MetadataExists(
			command,
			"""
			SELECT COUNT(*)
			FROM information_schema.TABLE_CONSTRAINTS
			WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = @TableName AND CONSTRAINT_NAME = @Name;
			""",
			constraintName,
			tableName);

	private static bool IndexExists(
		System.Data.Common.DbCommand command,
		string tableName,
		string indexName) =>
		MetadataExists(
			command,
			"""
			SELECT COUNT(*)
			FROM information_schema.STATISTICS
			WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName AND INDEX_NAME = @Name;
			""",
			indexName,
			tableName);

	private static void EnsureIndex(
		System.Data.Common.DbCommand command,
		string tableName,
		string indexName,
		string columnName)
	{
		if (!IndexExists(command, tableName, indexName))
		{
			Execute(command, $"ALTER TABLE {tableName} ADD INDEX {indexName} ({columnName});");
		}
	}

	private static bool MetadataExists(
		System.Data.Common.DbCommand command,
		string commandText,
		string name,
		string? tableName = null)
	{
		command.CommandText = commandText;
		command.Parameters.Clear();
		command.Parameters.Add(CreateParameter(command, "@Name", name));
		if (tableName is not null)
		{
			command.Parameters.Add(CreateParameter(command, "@TableName", tableName));
		}

		return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	}

	private static System.Data.Common.DbParameter CreateParameter(
		System.Data.Common.DbCommand command,
		string name,
		object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		return parameter;
	}

	private static void Execute(System.Data.Common.DbCommand command, string commandText)
	{
		command.CommandText = commandText;
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private void EnsureDatabaseExists()
	{
		using var connection = _connectionFactory.CreateServerConnection();
		connection.Open();
		using var existsCommand = connection.CreateCommand();
		existsCommand.CommandText =
			"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @DatabaseName;";
		existsCommand.Parameters.AddWithValue("@DatabaseName", _connectionFactory.DatabaseName);
		if (existsCommand.ExecuteScalar() is not null)
		{
			return;
		}

		var escapedName = _connectionFactory.DatabaseName.Replace("`", "``", StringComparison.Ordinal);
		using var createCommand = connection.CreateCommand();
		createCommand.CommandText =
			$"CREATE DATABASE `{escapedName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
		createCommand.ExecuteNonQuery();
	}

	private const string ProcurementSql =
	"""
	CREATE TABLE IF NOT EXISTS PurchaseOrders (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, OrderNumber varchar(50) NOT NULL UNIQUE, SupplierId bigint NOT NULL, OrderDate varchar(10) NOT NULL, ExpectedDeliveryDate varchar(10) NULL, Notes text NULL, Status int NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, INDEX IX_PurchaseOrders_SupplierId_Status (SupplierId, Status), INDEX IX_PurchaseOrders_OrderDate (OrderDate), CONSTRAINT FK_PurchaseOrders_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)) ENGINE=InnoDB;
	CREATE TABLE IF NOT EXISTS PurchaseOrderLines (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, PurchaseOrderId bigint NOT NULL, LineNumber int NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, UnitPrice decimal(18,2) NOT NULL DEFAULT 0, ReceivedQuantity int NOT NULL DEFAULT 0, Version bigint NOT NULL DEFAULT 1, UNIQUE KEY UQ_PurchaseOrderLines_Number (PurchaseOrderId, LineNumber), UNIQUE KEY UQ_PurchaseOrderLines_Item (PurchaseOrderId, ItemId), INDEX IX_PurchaseOrderLines_ItemId (ItemId), CONSTRAINT CK_PurchaseOrderLines_Quantity CHECK (Quantity > 0 AND ReceivedQuantity >= 0 AND ReceivedQuantity <= Quantity), CONSTRAINT FK_PurchaseOrderLines_Orders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT FK_PurchaseOrderLines_Items FOREIGN KEY (ItemId) REFERENCES Items(Id)) ENGINE=InnoDB;
	CREATE TABLE IF NOT EXISTS GoodsReceipts (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ReceiptNumber varchar(50) NOT NULL UNIQUE, PurchaseOrderId bigint NOT NULL, ReceiptDate varchar(10) NOT NULL, InvoiceNumber varchar(100) NOT NULL, InvoiceDate varchar(10) NOT NULL, InvoiceDocumentPath varchar(1000) NULL, Notes text NULL, INDEX IX_GoodsReceipts_PurchaseOrderId (PurchaseOrderId), CONSTRAINT FK_GoodsReceipts_Orders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id)) ENGINE=InnoDB;
	CREATE TABLE IF NOT EXISTS GoodsReceiptLines (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, GoodsReceiptId bigint NOT NULL, PurchaseOrderLineId bigint NOT NULL, InventoryId bigint NOT NULL, Quantity int NOT NULL, UNIQUE KEY UQ_GoodsReceiptLines_OrderLine (GoodsReceiptId, PurchaseOrderLineId), INDEX IX_GoodsReceiptLines_InventoryId (InventoryId), CONSTRAINT CK_GoodsReceiptLines_Quantity CHECK (Quantity > 0), CONSTRAINT FK_GoodsReceiptLines_Receipts FOREIGN KEY (GoodsReceiptId) REFERENCES GoodsReceipts(Id), CONSTRAINT FK_GoodsReceiptLines_OrderLines FOREIGN KEY (PurchaseOrderLineId) REFERENCES PurchaseOrderLines(Id), CONSTRAINT FK_GoodsReceiptLines_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id)) ENGINE=InnoDB;
	""";

	private const string SchemaSql =
	"""
	CREATE TABLE IF NOT EXISTS DatabaseInfo
	(
		Id int NOT NULL PRIMARY KEY,
		Version int NOT NULL,
		CONSTRAINT CK_DatabaseInfo_SingleRow CHECK (Id = 1)
	) ENGINE=InnoDB;

	INSERT INTO DatabaseInfo (Id, Version)
	SELECT 1, @CurrentVersion WHERE NOT EXISTS (SELECT 1 FROM DatabaseInfo WHERE Id = 1);

	CREATE TABLE IF NOT EXISTS Manufacturers (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Name varchar(200) NOT NULL UNIQUE, Description varchar(500) NULL, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;
	CREATE TABLE IF NOT EXISTS Categories (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Name varchar(200) NOT NULL UNIQUE, Description varchar(500) NULL, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;
	CREATE TABLE IF NOT EXISTS UnitsOfMeasure (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Name varchar(200) NOT NULL UNIQUE, Description varchar(500) NULL, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;
	CREATE TABLE IF NOT EXISTS Packagings (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Name varchar(200) NOT NULL UNIQUE, Description varchar(500) NULL, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;
	CREATE TABLE IF NOT EXISTS SupplierCategories (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Name varchar(200) NOT NULL UNIQUE, Description varchar(500) NULL, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;
	INSERT IGNORE INTO SupplierCategories (Name) VALUES ('IT Hardware'), ('ProAV'), ('Licensing');
	CREATE TABLE IF NOT EXISTS Suppliers (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, SupplierNumber varchar(50) NOT NULL UNIQUE, AccountNumber bigint NOT NULL UNIQUE, CustomerNumber varchar(100) NULL, Name varchar(200) NOT NULL UNIQUE, Contact varchar(200) NULL, Email varchar(320) NULL, Phone varchar(100) NULL, Address varchar(1000) NULL, RmaTerms text NULL, Url varchar(500) NULL, PaymentTerm varchar(200) NULL, Iban varchar(34) NULL, AccountName varchar(200) NULL, SepaMandate varchar(200) NULL, VatNumber varchar(50) NULL, SupplierCategoryId bigint NULL, Loyalty int NOT NULL DEFAULT 100, Quality int NOT NULL DEFAULT 100, Notes text NULL, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_Suppliers_SupplierCategories FOREIGN KEY (SupplierCategoryId) REFERENCES SupplierCategories(Id)) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Items
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		PartNumber varchar(200) NOT NULL UNIQUE,
		Description varchar(500) NOT NULL,
		Manufacturer varchar(200) NULL,
		Category varchar(200) NULL,
		ManufacturerId bigint NULL,
		CategoryId bigint NULL,
		UnitOfMeasureId bigint NULL,
		PackagingId bigint NULL,
		SupplierId bigint NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1,
		INDEX IX_Items_ManufacturerId (ManufacturerId),
		INDEX IX_Items_CategoryId (CategoryId),
		INDEX IX_Items_UnitOfMeasureId (UnitOfMeasureId),
		INDEX IX_Items_PackagingId (PackagingId),
		INDEX IX_Items_SupplierId (SupplierId),
		CONSTRAINT FK_Items_Manufacturers FOREIGN KEY (ManufacturerId) REFERENCES Manufacturers(Id),
		CONSTRAINT FK_Items_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id),
		CONSTRAINT FK_Items_UnitsOfMeasure FOREIGN KEY (UnitOfMeasureId) REFERENCES UnitsOfMeasure(Id),
		CONSTRAINT FK_Items_Packagings FOREIGN KEY (PackagingId) REFERENCES Packagings(Id),
		CONSTRAINT FK_Items_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS SupplierItems (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, SupplierId bigint NOT NULL, ItemId bigint NOT NULL, SupplierPartNumber varchar(200) NOT NULL, PurchasePrice decimal(18,2) NOT NULL DEFAULT 0, LeadTimeDays int NOT NULL DEFAULT 0, MinimumOrderQuantity decimal(18,3) NOT NULL DEFAULT 1, IsPreferredSupplier boolean NOT NULL DEFAULT false, IsActive boolean NOT NULL DEFAULT true, Version bigint NOT NULL DEFAULT 1, UNIQUE KEY UQ_SupplierItems_Context (SupplierId, ItemId), INDEX IX_SupplierItems_SupplierId (SupplierId), INDEX IX_SupplierItems_ItemId (ItemId), CONSTRAINT FK_SupplierItems_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_SupplierItems_Items FOREIGN KEY (ItemId) REFERENCES Items(Id)) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Purposes
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		Name varchar(200) NOT NULL UNIQUE,
		Description varchar(500) NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Warehouses
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		Name varchar(200) NOT NULL UNIQUE,
		Description varchar(500) NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS ReasonCodes
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		Name varchar(200) NOT NULL UNIQUE,
		Description varchar(500) NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS StorageLocations
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		WarehouseId bigint NOT NULL,
		Name varchar(200) NOT NULL,
		Description varchar(500) NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1,
		CONSTRAINT UQ_StorageLocations_Warehouse_Name UNIQUE (WarehouseId, Name),
		CONSTRAINT FK_StorageLocations_Warehouses FOREIGN KEY (WarehouseId) REFERENCES Warehouses(Id),
		INDEX IX_StorageLocations_WarehouseId_Name (WarehouseId, Name)
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Inventories
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		ItemId bigint NOT NULL,
		PurposeId bigint NOT NULL,
		StorageLocationId bigint NOT NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1,
		CONSTRAINT UQ_Inventories_Context UNIQUE (ItemId, PurposeId, StorageLocationId),
		INDEX IX_Inventories_ItemId (ItemId),
		INDEX IX_Inventories_PurposeId (PurposeId),
		INDEX IX_Inventories_StorageLocationId (StorageLocationId),
		CONSTRAINT FK_Inventories_Items FOREIGN KEY (ItemId) REFERENCES Items(Id),
		CONSTRAINT FK_Inventories_Purposes FOREIGN KEY (PurposeId) REFERENCES Purposes(Id),
		CONSTRAINT FK_Inventories_StorageLocations FOREIGN KEY (StorageLocationId) REFERENCES StorageLocations(Id)
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Users
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		Email varchar(320) NOT NULL UNIQUE,
		DisplayName varchar(200) NOT NULL,
		PasswordHash varchar(500) NOT NULL,
		IsAdministrator boolean NOT NULL DEFAULT false,
		IsActive boolean NOT NULL DEFAULT true,
		CreatedUtc varchar(40) NOT NULL,
		Version bigint NOT NULL DEFAULT 1
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS StockMovements
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		InventoryId bigint NOT NULL,
		ReasonCodeId bigint NULL,
		MovementType int NOT NULL,
		TimestampUtc varchar(40) NOT NULL,
		Quantity int NOT NULL,
		UnitPrice decimal(18,2) NULL,
		Reference varchar(200) NULL,
		Notes text NULL,
		CONSTRAINT FK_StockMovements_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id),
		CONSTRAINT FK_StockMovements_ReasonCodes FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id),
		INDEX IX_StockMovements_InventoryId_TimestampUtc (InventoryId, TimestampUtc),
		INDEX IX_StockMovements_ReasonCodeId (ReasonCodeId)
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS AuditEntries
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		TimestampUtc varchar(40) NOT NULL,
		UserId bigint NULL,
		UserEmail varchar(320) NOT NULL,
		EntityType varchar(200) NOT NULL,
		EntityId bigint NOT NULL,
		Action varchar(100) NOT NULL,
		BeforeJson longtext NULL,
		AfterJson longtext NULL,
		CONSTRAINT FK_AuditEntries_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL,
		INDEX IX_AuditEntries_TimestampUtc (TimestampUtc),
		INDEX IX_AuditEntries_Entity (EntityType, EntityId, TimestampUtc)
	) ENGINE=InnoDB;

	INSERT INTO Purposes (Name, Description, IsActive)
	SELECT 'Stock', 'Default stock purpose', true
	WHERE NOT EXISTS (SELECT 1 FROM Purposes WHERE Name = 'Stock');

	INSERT INTO ReasonCodes (Name, IsActive)
	SELECT defaults.Name, true
	FROM
	(
		SELECT 'Goods Receipt' AS Name UNION ALL SELECT 'Goods Issue' UNION ALL
		SELECT 'Inventory Correction' UNION ALL SELECT 'Damaged' UNION ALL SELECT 'Lost' UNION ALL
		SELECT 'Returned' UNION ALL SELECT 'Consumed' UNION ALL SELECT 'Demo' UNION ALL
		SELECT 'Repair' UNION ALL SELECT 'Transfer'
	) defaults
	WHERE NOT EXISTS (SELECT 1 FROM ReasonCodes existing WHERE existing.Name = defaults.Name);

	INSERT INTO Warehouses (Name, Description, IsActive)
	SELECT 'Main Warehouse', 'Default Depot warehouse', true
	WHERE NOT EXISTS (SELECT 1 FROM Warehouses WHERE Name = 'Main Warehouse');

	INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive)
	SELECT Id, 'Default', 'Default storage location', true
	FROM Warehouses w
	WHERE w.Name = 'Main Warehouse'
	  AND NOT EXISTS (SELECT 1 FROM StorageLocations sl WHERE sl.WarehouseId = w.Id AND sl.Name = 'Default');

	INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, IsActive, CreatedUtc)
	SELECT 'admin@depot.local', 'Administrator', @DefaultPasswordHash, true, true,
	       DATE_FORMAT(UTC_TIMESTAMP(6), '%Y-%m-%dT%H:%i:%s.%fZ')
	WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@depot.local');
	""";
}
