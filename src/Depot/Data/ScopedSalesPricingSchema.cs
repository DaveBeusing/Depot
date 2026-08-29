// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

internal static class ScopedSalesPricingSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		var statements = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Scoped sales pricing is not supported for provider '{connectionFactory.Provider}'.")
		};
		foreach (var statement in statements)
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
		transaction.Commit();
	}

	private static readonly string[] Sqlite =
	[
		"CREATE TABLE SalesRegions (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);",
		"ALTER TABLE SalesPriceLists ADD COLUMN Scope INTEGER NOT NULL DEFAULT 2 CHECK(Scope IN (0,1,2));",
		"ALTER TABLE SalesPriceLists ADD COLUMN RegionId INTEGER NULL REFERENCES SalesRegions(Id);",
		"CREATE INDEX IX_SalesPriceLists_ScopeRegionActive ON SalesPriceLists(Scope,RegionId,IsActive);",
		"CREATE TRIGGER TR_SalesPriceLists_Scope_Insert BEFORE INSERT ON SalesPriceLists WHEN NOT ((NEW.Scope=1 AND NEW.RegionId IS NOT NULL) OR (NEW.Scope IN (0,2) AND NEW.RegionId IS NULL)) BEGIN SELECT RAISE(ABORT,'Invalid price-list scope and region combination.'); END;",
		"CREATE TRIGGER TR_SalesPriceLists_Scope_Update BEFORE UPDATE OF Scope,RegionId ON SalesPriceLists WHEN NOT ((NEW.Scope=1 AND NEW.RegionId IS NOT NULL) OR (NEW.Scope IN (0,2) AND NEW.RegionId IS NULL)) BEGIN SELECT RAISE(ABORT,'Invalid price-list scope and region combination.'); END;",
		"ALTER TABLE Customers ADD COLUMN SalesRegionId INTEGER NULL REFERENCES SalesRegions(Id);",
		"CREATE INDEX IX_Customers_SalesRegion ON Customers(SalesRegionId,IsActive);",
		"CREATE INDEX IX_CustomerPriceLists_List ON CustomerPriceLists(SalesPriceListId,CustomerId);",
		"UPDATE SalesPriceLists SET IsActive=0 WHERE Scope=2 AND NOT EXISTS (SELECT 1 FROM CustomerPriceLists cpl WHERE cpl.SalesPriceListId=SalesPriceLists.Id);",
		"ALTER TABLE SalesOrderLines ADD COLUMN PriceSourceListId INTEGER NULL REFERENCES SalesPriceLists(Id);",
		"ALTER TABLE SalesOrderLines ADD COLUMN PriceSourceName TEXT NULL;",
		"ALTER TABLE SalesOrderLines ADD COLUMN PriceSourceScope INTEGER NULL CHECK(PriceSourceScope IS NULL OR PriceSourceScope IN (0,1,2));",
		"ALTER TABLE SalesOrderLines ADD COLUMN PriceSourceCurrency TEXT NULL;",
		"ALTER TABLE SalesQuoteLines ADD COLUMN PriceSourceListId INTEGER NULL REFERENCES SalesPriceLists(Id);",
		"ALTER TABLE SalesQuoteLines ADD COLUMN PriceSourceName TEXT NULL;",
		"ALTER TABLE SalesQuoteLines ADD COLUMN PriceSourceScope INTEGER NULL CHECK(PriceSourceScope IS NULL OR PriceSourceScope IN (0,1,2));",
		"ALTER TABLE SalesQuoteLines ADD COLUMN PriceSourceCurrency TEXT NULL;"
	];

	private static readonly string[] SqlServer =
	[
		"CREATE TABLE SalesRegions (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE, Name nvarchar(200) NOT NULL, IsActive bit NOT NULL CONSTRAINT DF_SalesRegions_IsActive DEFAULT 1, Version bigint NOT NULL CONSTRAINT DF_SalesRegions_Version DEFAULT 1);",
		"ALTER TABLE SalesPriceLists ADD Scope int NOT NULL CONSTRAINT DF_SalesPriceLists_Scope DEFAULT 2, RegionId bigint NULL;",
		"ALTER TABLE SalesPriceLists ADD CONSTRAINT CK_SalesPriceLists_Scope CHECK (Scope IN (0,1,2)), CONSTRAINT CK_SalesPriceLists_ScopeRegion CHECK ((Scope=1 AND RegionId IS NOT NULL) OR (Scope IN (0,2) AND RegionId IS NULL)), CONSTRAINT FK_SalesPriceLists_Region FOREIGN KEY(RegionId) REFERENCES SalesRegions(Id);",
		"CREATE INDEX IX_SalesPriceLists_ScopeRegionActive ON SalesPriceLists(Scope,RegionId,IsActive);",
		"ALTER TABLE Customers ADD SalesRegionId bigint NULL;",
		"ALTER TABLE Customers ADD CONSTRAINT FK_Customers_SalesRegion FOREIGN KEY (SalesRegionId) REFERENCES SalesRegions(Id);",
		"CREATE INDEX IX_Customers_SalesRegion ON Customers(SalesRegionId,IsActive);",
		"CREATE INDEX IX_CustomerPriceLists_List ON CustomerPriceLists(SalesPriceListId,CustomerId);",
		"UPDATE SalesPriceLists SET IsActive=0 WHERE Scope=2 AND NOT EXISTS (SELECT 1 FROM CustomerPriceLists cpl WHERE cpl.SalesPriceListId=SalesPriceLists.Id);",
		"ALTER TABLE SalesOrderLines ADD PriceSourceListId bigint NULL, PriceSourceName nvarchar(250) NULL, PriceSourceScope int NULL, PriceSourceCurrency nvarchar(3) NULL;",
		"ALTER TABLE SalesOrderLines ADD CONSTRAINT FK_SalesOrderLines_PriceSource FOREIGN KEY (PriceSourceListId) REFERENCES SalesPriceLists(Id);",
		"ALTER TABLE SalesOrderLines ADD CONSTRAINT CK_SalesOrderLines_PriceSourceScope CHECK (PriceSourceScope IS NULL OR PriceSourceScope IN (0,1,2));",
		"ALTER TABLE SalesQuoteLines ADD PriceSourceListId bigint NULL, PriceSourceName nvarchar(250) NULL, PriceSourceScope int NULL, PriceSourceCurrency nvarchar(3) NULL;",
		"ALTER TABLE SalesQuoteLines ADD CONSTRAINT FK_SalesQuoteLines_PriceSource FOREIGN KEY (PriceSourceListId) REFERENCES SalesPriceLists(Id);",
		"ALTER TABLE SalesQuoteLines ADD CONSTRAINT CK_SalesQuoteLines_PriceSourceScope CHECK (PriceSourceScope IS NULL OR PriceSourceScope IN (0,1,2));"
	];

	private static readonly string[] MySql =
	[
		"CREATE TABLE SalesRegions (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Code VARCHAR(50) NOT NULL UNIQUE, Name VARCHAR(200) NOT NULL, IsActive BOOLEAN NOT NULL DEFAULT TRUE, Version BIGINT NOT NULL DEFAULT 1) ENGINE=InnoDB;",
		"ALTER TABLE SalesPriceLists ADD COLUMN Scope INT NOT NULL DEFAULT 2, ADD COLUMN RegionId BIGINT NULL, ADD CONSTRAINT CK_SalesPriceLists_Scope CHECK (Scope IN (0,1,2)), ADD CONSTRAINT CK_SalesPriceLists_ScopeRegion CHECK ((Scope=1 AND RegionId IS NOT NULL) OR (Scope IN (0,2) AND RegionId IS NULL)), ADD CONSTRAINT FK_SalesPriceLists_Region FOREIGN KEY(RegionId) REFERENCES SalesRegions(Id), ADD INDEX IX_SalesPriceLists_ScopeRegionActive(Scope,RegionId,IsActive);",
		"ALTER TABLE Customers ADD COLUMN SalesRegionId BIGINT NULL, ADD CONSTRAINT FK_Customers_SalesRegion FOREIGN KEY(SalesRegionId) REFERENCES SalesRegions(Id), ADD INDEX IX_Customers_SalesRegion(SalesRegionId,IsActive);",
		"ALTER TABLE CustomerPriceLists ADD INDEX IX_CustomerPriceLists_List(SalesPriceListId,CustomerId);",
		"UPDATE SalesPriceLists SET IsActive=FALSE WHERE Scope=2 AND NOT EXISTS (SELECT 1 FROM CustomerPriceLists cpl WHERE cpl.SalesPriceListId=SalesPriceLists.Id);",
		"ALTER TABLE SalesOrderLines ADD COLUMN PriceSourceListId BIGINT NULL, ADD COLUMN PriceSourceName VARCHAR(250) NULL, ADD COLUMN PriceSourceScope INT NULL, ADD COLUMN PriceSourceCurrency VARCHAR(3) NULL, ADD CONSTRAINT FK_SalesOrderLines_PriceSource FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id), ADD CONSTRAINT CK_SalesOrderLines_PriceSourceScope CHECK (PriceSourceScope IS NULL OR PriceSourceScope IN (0,1,2));",
		"ALTER TABLE SalesQuoteLines ADD COLUMN PriceSourceListId BIGINT NULL, ADD COLUMN PriceSourceName VARCHAR(250) NULL, ADD COLUMN PriceSourceScope INT NULL, ADD COLUMN PriceSourceCurrency VARCHAR(3) NULL, ADD CONSTRAINT FK_SalesQuoteLines_PriceSource FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id), ADD CONSTRAINT CK_SalesQuoteLines_PriceSourceScope CHECK (PriceSourceScope IS NULL OR PriceSourceScope IN (0,1,2));"
	];
}
