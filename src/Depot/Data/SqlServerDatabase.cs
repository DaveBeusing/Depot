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

		command.CommandText = "SELECT Version FROM DatabaseInfo WHERE Id = 1;";
		var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
		if (version == 8)
		{
			MigrateToWarehouseStructure(command);
			version = DatabaseVersion.CurrentVersion;
		}
		if (version != DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"SQL Server schema version '{version}' is not supported. Expected '{DatabaseVersion.CurrentVersion}'.");
		}

		transaction.Commit();
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

	IF OBJECT_ID(N'Items', N'U') IS NULL
		CREATE TABLE Items
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Items PRIMARY KEY,
			PartNumber nvarchar(200) NOT NULL CONSTRAINT UQ_Items_PartNumber UNIQUE,
			Description nvarchar(500) NOT NULL,
			Manufacturer nvarchar(200) NULL,
			Category nvarchar(200) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Items_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Items_Version DEFAULT 1
		);

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
			MovementType int NOT NULL,
			TimestampUtc nvarchar(40) NOT NULL,
			Quantity int NOT NULL,
			UnitPrice decimal(18,2) NULL,
			Reference nvarchar(200) NULL,
			Notes nvarchar(2000) NULL,
			CONSTRAINT FK_StockMovements_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id)
		);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovements_InventoryId_TimestampUtc')
		CREATE INDEX IX_StockMovements_InventoryId_TimestampUtc ON StockMovements(InventoryId, TimestampUtc);

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
