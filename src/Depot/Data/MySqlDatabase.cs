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

			command.CommandText = "SELECT Version FROM DatabaseInfo WHERE Id = 1;";
			command.Parameters.Clear();
			var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
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

	CREATE TABLE IF NOT EXISTS Items
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		PartNumber varchar(200) NOT NULL UNIQUE,
		Description varchar(500) NOT NULL,
		Manufacturer varchar(200) NULL,
		Category varchar(200) NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Purposes
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		Name varchar(200) NOT NULL UNIQUE,
		Description varchar(500) NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Locations
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		Name varchar(200) NOT NULL UNIQUE,
		Description varchar(500) NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1
	) ENGINE=InnoDB;

	CREATE TABLE IF NOT EXISTS Inventories
	(
		Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
		ItemId bigint NOT NULL,
		PurposeId bigint NOT NULL,
		LocationId bigint NOT NULL,
		IsActive boolean NOT NULL DEFAULT true,
		Version bigint NOT NULL DEFAULT 1,
		CONSTRAINT UQ_Inventories_Context UNIQUE (ItemId, PurposeId, LocationId),
		CONSTRAINT FK_Inventories_Items FOREIGN KEY (ItemId) REFERENCES Items(Id),
		CONSTRAINT FK_Inventories_Purposes FOREIGN KEY (PurposeId) REFERENCES Purposes(Id),
		CONSTRAINT FK_Inventories_Locations FOREIGN KEY (LocationId) REFERENCES Locations(Id)
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
		MovementType int NOT NULL,
		TimestampUtc varchar(40) NOT NULL,
		Quantity int NOT NULL,
		UnitPrice decimal(18,2) NULL,
		Reference varchar(200) NULL,
		Notes text NULL,
		CONSTRAINT FK_StockMovements_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id),
		INDEX IX_StockMovements_InventoryId_TimestampUtc (InventoryId, TimestampUtc)
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

	INSERT INTO Locations (Name, Description, IsActive)
	SELECT 'Warehouse', 'Default warehouse location', true
	WHERE NOT EXISTS (SELECT 1 FROM Locations WHERE Name = 'Warehouse');

	INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, IsActive, CreatedUtc)
	SELECT 'admin@depot.local', 'Administrator', @DefaultPasswordHash, true, true,
	       DATE_FORMAT(UTC_TIMESTAMP(6), '%Y-%m-%dT%H:%i:%s.%fZ')
	WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@depot.local');
	""";
}
