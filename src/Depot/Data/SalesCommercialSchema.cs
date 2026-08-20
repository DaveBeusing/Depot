// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

internal static class SalesCommercialSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		var statements = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Sales commercial schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		foreach (var statement in statements)
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
	}

	private static readonly string[] Sqlite =
	[
		"CREATE TABLE IF NOT EXISTS CustomerContacts (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL, Name TEXT NOT NULL, Role INTEGER NOT NULL, Department TEXT NULL, Email TEXT NULL, Phone TEXT NULL, Mobile TEXT NULL, IsPrimary INTEGER NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(CustomerId) REFERENCES Customers(Id));",
		"CREATE INDEX IF NOT EXISTS IX_CustomerContacts_Customer ON CustomerContacts(CustomerId,IsActive,IsPrimary);",
		"CREATE TABLE IF NOT EXISTS SalesPriceLists (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, Currency TEXT NOT NULL, ValidFrom TEXT NULL, ValidTo TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);",
		"CREATE TABLE IF NOT EXISTS SalesPriceListItems (Id INTEGER PRIMARY KEY AUTOINCREMENT, SalesPriceListId INTEGER NOT NULL, ItemId INTEGER NOT NULL, UnitPrice REAL NOT NULL, DiscountPercent REAL NOT NULL DEFAULT 0, Version INTEGER NOT NULL DEFAULT 1, UNIQUE(SalesPriceListId,ItemId), FOREIGN KEY(SalesPriceListId) REFERENCES SalesPriceLists(Id), FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"CREATE TABLE IF NOT EXISTS CustomerPriceLists (CustomerId INTEGER PRIMARY KEY, SalesPriceListId INTEGER NOT NULL, FOREIGN KEY(CustomerId) REFERENCES Customers(Id), FOREIGN KEY(SalesPriceListId) REFERENCES SalesPriceLists(Id));",
		"CREATE TABLE IF NOT EXISTS SalesQuotes (Id INTEGER PRIMARY KEY AUTOINCREMENT, QuoteNumber TEXT NOT NULL UNIQUE, CustomerId INTEGER NOT NULL, BillingAddress TEXT NULL, ShippingAddress TEXT NULL, ContactId INTEGER NULL, ContactName TEXT NULL, QuoteDate TEXT NOT NULL, ValidUntil TEXT NOT NULL, Currency TEXT NOT NULL, CustomerReference TEXT NULL, Notes TEXT NULL, Status INTEGER NOT NULL, CreatedByUserId INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, ConvertedSalesOrderId INTEGER NULL, ConvertedAtUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(CustomerId) REFERENCES Customers(Id), FOREIGN KEY(ContactId) REFERENCES CustomerContacts(Id), FOREIGN KEY(ConvertedSalesOrderId) REFERENCES SalesOrders(Id));",
		"CREATE TABLE IF NOT EXISTS SalesQuoteLines (Id INTEGER PRIMARY KEY AUTOINCREMENT, SalesQuoteId INTEGER NOT NULL, LineNumber INTEGER NOT NULL, ItemId INTEGER NOT NULL, PartNumber TEXT NOT NULL, Description TEXT NOT NULL, Quantity INTEGER NOT NULL, UnitPrice REAL NOT NULL, DiscountPercent REAL NOT NULL DEFAULT 0, TaxRate REAL NOT NULL DEFAULT 19, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(SalesQuoteId) REFERENCES SalesQuotes(Id), FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"CREATE INDEX IF NOT EXISTS IX_SalesQuotes_CustomerStatus ON SalesQuotes(CustomerId,Status,QuoteDate);",
		"ALTER TABLE Shipments ADD COLUMN PackingStatus INTEGER NOT NULL DEFAULT 1;",
		"ALTER TABLE Shipments ADD COLUMN PackedAtUtc TEXT NULL;",
		"ALTER TABLE Shipments ADD COLUMN PackedByUserId INTEGER NULL;"
	];

	private static readonly string[] SqlServer =
	[
		"IF OBJECT_ID(N'CustomerContacts',N'U') IS NULL CREATE TABLE CustomerContacts (Id bigint IDENTITY(1,1) PRIMARY KEY, CustomerId bigint NOT NULL, Name nvarchar(250) NOT NULL, Role int NOT NULL, Department nvarchar(250) NULL, Email nvarchar(250) NULL, Phone nvarchar(100) NULL, Mobile nvarchar(100) NULL, IsPrimary bit NOT NULL DEFAULT 0, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_CustomerContacts_Customers FOREIGN KEY(CustomerId) REFERENCES Customers(Id));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CustomerContacts_Customer') CREATE INDEX IX_CustomerContacts_Customer ON CustomerContacts(CustomerId,IsActive,IsPrimary);",
		"IF OBJECT_ID(N'SalesPriceLists',N'U') IS NULL CREATE TABLE SalesPriceLists (Id bigint IDENTITY(1,1) PRIMARY KEY, Code nvarchar(100) NOT NULL UNIQUE, Name nvarchar(250) NOT NULL, Currency nvarchar(3) NOT NULL, ValidFrom date NULL, ValidTo date NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);",
		"IF OBJECT_ID(N'SalesPriceListItems',N'U') IS NULL CREATE TABLE SalesPriceListItems (Id bigint IDENTITY(1,1) PRIMARY KEY, SalesPriceListId bigint NOT NULL, ItemId bigint NOT NULL, UnitPrice decimal(18,4) NOT NULL, DiscountPercent decimal(9,4) NOT NULL DEFAULT 0, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_SalesPriceListItems UNIQUE(SalesPriceListId,ItemId), CONSTRAINT FK_SalesPriceListItems_List FOREIGN KEY(SalesPriceListId) REFERENCES SalesPriceLists(Id), CONSTRAINT FK_SalesPriceListItems_Item FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"IF OBJECT_ID(N'CustomerPriceLists',N'U') IS NULL CREATE TABLE CustomerPriceLists (CustomerId bigint NOT NULL PRIMARY KEY, SalesPriceListId bigint NOT NULL, CONSTRAINT FK_CustomerPriceLists_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id), CONSTRAINT FK_CustomerPriceLists_List FOREIGN KEY(SalesPriceListId) REFERENCES SalesPriceLists(Id));",
		"IF OBJECT_ID(N'SalesQuotes',N'U') IS NULL CREATE TABLE SalesQuotes (Id bigint IDENTITY(1,1) PRIMARY KEY, QuoteNumber nvarchar(100) NOT NULL UNIQUE, CustomerId bigint NOT NULL, BillingAddress nvarchar(2000) NULL, ShippingAddress nvarchar(2000) NULL, ContactId bigint NULL, ContactName nvarchar(250) NULL, QuoteDate date NOT NULL, ValidUntil date NOT NULL, Currency nvarchar(3) NOT NULL, CustomerReference nvarchar(250) NULL, Notes nvarchar(max) NULL, Status int NOT NULL, CreatedByUserId bigint NOT NULL, CreatedAtUtc nvarchar(40) NOT NULL, ConvertedSalesOrderId bigint NULL, ConvertedAtUtc nvarchar(40) NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_SalesQuotes_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id), CONSTRAINT FK_SalesQuotes_Contact FOREIGN KEY(ContactId) REFERENCES CustomerContacts(Id), CONSTRAINT FK_SalesQuotes_Order FOREIGN KEY(ConvertedSalesOrderId) REFERENCES SalesOrders(Id));",
		"IF OBJECT_ID(N'SalesQuoteLines',N'U') IS NULL CREATE TABLE SalesQuoteLines (Id bigint IDENTITY(1,1) PRIMARY KEY, SalesQuoteId bigint NOT NULL, LineNumber int NOT NULL, ItemId bigint NOT NULL, PartNumber nvarchar(100) NOT NULL, Description nvarchar(1000) NOT NULL, Quantity int NOT NULL, UnitPrice decimal(18,4) NOT NULL, DiscountPercent decimal(9,4) NOT NULL DEFAULT 0, TaxRate decimal(9,4) NOT NULL DEFAULT 19, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_SalesQuoteLines_Quote FOREIGN KEY(SalesQuoteId) REFERENCES SalesQuotes(Id), CONSTRAINT FK_SalesQuoteLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"IF COL_LENGTH('Shipments','PackingStatus') IS NULL ALTER TABLE Shipments ADD PackingStatus int NOT NULL CONSTRAINT DF_Shipments_PackingStatus DEFAULT 1;",
		"IF COL_LENGTH('Shipments','PackedAtUtc') IS NULL ALTER TABLE Shipments ADD PackedAtUtc nvarchar(40) NULL;",
		"IF COL_LENGTH('Shipments','PackedByUserId') IS NULL ALTER TABLE Shipments ADD PackedByUserId bigint NULL;"
	];

	private static readonly string[] MySql =
	[
		"CREATE TABLE IF NOT EXISTS CustomerContacts (Id BIGINT AUTO_INCREMENT PRIMARY KEY, CustomerId BIGINT NOT NULL, Name VARCHAR(250) NOT NULL, Role INT NOT NULL, Department VARCHAR(250) NULL, Email VARCHAR(250) NULL, Phone VARCHAR(100) NULL, Mobile VARCHAR(100) NULL, IsPrimary BOOLEAN NOT NULL DEFAULT FALSE, IsActive BOOLEAN NOT NULL DEFAULT TRUE, Version BIGINT NOT NULL DEFAULT 1, INDEX IX_CustomerContacts_Customer(CustomerId,IsActive,IsPrimary), FOREIGN KEY(CustomerId) REFERENCES Customers(Id));",
		"CREATE TABLE IF NOT EXISTS SalesPriceLists (Id BIGINT AUTO_INCREMENT PRIMARY KEY, Code VARCHAR(100) NOT NULL UNIQUE, Name VARCHAR(250) NOT NULL, Currency VARCHAR(3) NOT NULL, ValidFrom DATE NULL, ValidTo DATE NULL, IsActive BOOLEAN NOT NULL DEFAULT TRUE, Version BIGINT NOT NULL DEFAULT 1);",
		"CREATE TABLE IF NOT EXISTS SalesPriceListItems (Id BIGINT AUTO_INCREMENT PRIMARY KEY, SalesPriceListId BIGINT NOT NULL, ItemId BIGINT NOT NULL, UnitPrice DECIMAL(18,4) NOT NULL, DiscountPercent DECIMAL(9,4) NOT NULL DEFAULT 0, Version BIGINT NOT NULL DEFAULT 1, UNIQUE KEY UQ_SalesPriceListItems(SalesPriceListId,ItemId), FOREIGN KEY(SalesPriceListId) REFERENCES SalesPriceLists(Id), FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"CREATE TABLE IF NOT EXISTS CustomerPriceLists (CustomerId BIGINT PRIMARY KEY, SalesPriceListId BIGINT NOT NULL, FOREIGN KEY(CustomerId) REFERENCES Customers(Id), FOREIGN KEY(SalesPriceListId) REFERENCES SalesPriceLists(Id));",
		"CREATE TABLE IF NOT EXISTS SalesQuotes (Id BIGINT AUTO_INCREMENT PRIMARY KEY, QuoteNumber VARCHAR(100) NOT NULL UNIQUE, CustomerId BIGINT NOT NULL, BillingAddress TEXT NULL, ShippingAddress TEXT NULL, ContactId BIGINT NULL, ContactName VARCHAR(250) NULL, QuoteDate DATE NOT NULL, ValidUntil DATE NOT NULL, Currency VARCHAR(3) NOT NULL, CustomerReference VARCHAR(250) NULL, Notes TEXT NULL, Status INT NOT NULL, CreatedByUserId BIGINT NOT NULL, CreatedAtUtc VARCHAR(40) NOT NULL, ConvertedSalesOrderId BIGINT NULL, ConvertedAtUtc VARCHAR(40) NULL, Version BIGINT NOT NULL DEFAULT 1, FOREIGN KEY(CustomerId) REFERENCES Customers(Id), FOREIGN KEY(ContactId) REFERENCES CustomerContacts(Id), FOREIGN KEY(ConvertedSalesOrderId) REFERENCES SalesOrders(Id));",
		"CREATE TABLE IF NOT EXISTS SalesQuoteLines (Id BIGINT AUTO_INCREMENT PRIMARY KEY, SalesQuoteId BIGINT NOT NULL, LineNumber INT NOT NULL, ItemId BIGINT NOT NULL, PartNumber VARCHAR(100) NOT NULL, Description VARCHAR(1000) NOT NULL, Quantity INT NOT NULL, UnitPrice DECIMAL(18,4) NOT NULL, DiscountPercent DECIMAL(9,4) NOT NULL DEFAULT 0, TaxRate DECIMAL(9,4) NOT NULL DEFAULT 19, Version BIGINT NOT NULL DEFAULT 1, FOREIGN KEY(SalesQuoteId) REFERENCES SalesQuotes(Id), FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"ALTER TABLE Shipments ADD COLUMN IF NOT EXISTS PackingStatus INT NOT NULL DEFAULT 1, ADD COLUMN IF NOT EXISTS PackedAtUtc VARCHAR(40) NULL, ADD COLUMN IF NOT EXISTS PackedByUserId BIGINT NULL;"
	];
}
