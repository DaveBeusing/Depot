// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

internal static class SalesCorrectionSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		foreach (var statement in Statements(connectionFactory.Provider))
		{
			command.CommandText = statement;
			command.Parameters.Clear();
			command.ExecuteNonQuery();
		}
		transaction.Commit();
	}

	private static IReadOnlyList<string> Statements(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => Sqlite,
		DatabaseProvider.SqlServer => SqlServer,
		DatabaseProvider.MySql => MySql,
		_ => throw new NotSupportedException($"Sales correction schema is not supported for provider '{provider}'.")
	};

	private static readonly string[] Sqlite =
	[
		"ALTER TABLE Shipments ADD COLUMN ReversedAtUtc TEXT NULL;",
		"ALTER TABLE Shipments ADD COLUMN ReversedByUserId INTEGER NULL REFERENCES Users(Id);",
		"ALTER TABLE Shipments ADD COLUMN ReversalReason TEXT NULL;",
		"CREATE TABLE IF NOT EXISTS CustomerAddresses (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL REFERENCES Customers(Id), Type INTEGER NOT NULL, Name TEXT NULL, Address TEXT NOT NULL, IsDefault INTEGER NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);",
		"CREATE INDEX IF NOT EXISTS IX_CustomerAddresses_Customer_Type ON CustomerAddresses(CustomerId,Type,IsActive);",
		"CREATE TABLE IF NOT EXISTS CustomerReturns (Id INTEGER PRIMARY KEY AUTOINCREMENT, ReturnNumber TEXT NOT NULL UNIQUE, ShipmentId INTEGER NOT NULL REFERENCES Shipments(Id), SalesOrderId INTEGER NOT NULL REFERENCES SalesOrders(Id), CustomerId INTEGER NOT NULL REFERENCES Customers(Id), ReturnDate TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Reason TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id), PostedByUserId INTEGER NULL REFERENCES Users(Id), PostedAtUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 1);",
		"CREATE TABLE IF NOT EXISTS CustomerReturnLines (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerReturnId INTEGER NOT NULL REFERENCES CustomerReturns(Id), ShipmentLineId INTEGER NOT NULL REFERENCES ShipmentLines(Id), InventoryId INTEGER NOT NULL REFERENCES Inventories(Id), Quantity INTEGER NOT NULL, Version INTEGER NOT NULL DEFAULT 1, UNIQUE(CustomerReturnId,ShipmentLineId));",
		"CREATE INDEX IF NOT EXISTS IX_CustomerReturns_Shipment_Status ON CustomerReturns(ShipmentId,Status);",
		"CREATE TABLE IF NOT EXISTS SalesCreditNotes (Id INTEGER PRIMARY KEY AUTOINCREMENT, CreditNoteNumber TEXT NOT NULL UNIQUE, SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id), CustomerId INTEGER NOT NULL REFERENCES Customers(Id), CreditDate TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Reason TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id), PostedByUserId INTEGER NULL REFERENCES Users(Id), PostedAtUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 1);",
		"CREATE TABLE IF NOT EXISTS SalesCreditNoteLines (Id INTEGER PRIMARY KEY AUTOINCREMENT, SalesCreditNoteId INTEGER NOT NULL REFERENCES SalesCreditNotes(Id), SalesInvoiceLineId INTEGER NOT NULL REFERENCES SalesInvoiceLines(Id), Quantity INTEGER NOT NULL, UnitPrice NUMERIC NOT NULL, DiscountPercent NUMERIC NOT NULL DEFAULT 0, TaxRate NUMERIC NOT NULL DEFAULT 19, Version INTEGER NOT NULL DEFAULT 1, UNIQUE(SalesCreditNoteId,SalesInvoiceLineId));",
		"CREATE INDEX IF NOT EXISTS IX_SalesCreditNotes_Invoice_Status ON SalesCreditNotes(SalesInvoiceId,Status);"
	];

	private static readonly string[] SqlServer =
	[
		"IF COL_LENGTH(N'Shipments',N'ReversedAtUtc') IS NULL ALTER TABLE Shipments ADD ReversedAtUtc nvarchar(40) NULL;",
		"IF COL_LENGTH(N'Shipments',N'ReversedByUserId') IS NULL ALTER TABLE Shipments ADD ReversedByUserId bigint NULL;",
		"IF COL_LENGTH(N'Shipments',N'ReversalReason') IS NULL ALTER TABLE Shipments ADD ReversalReason nvarchar(2000) NULL;",
		"IF OBJECT_ID(N'CustomerAddresses',N'U') IS NULL CREATE TABLE CustomerAddresses (Id bigint IDENTITY(1,1) PRIMARY KEY, CustomerId bigint NOT NULL REFERENCES Customers(Id), Type int NOT NULL, Name nvarchar(250) NULL, Address nvarchar(2000) NOT NULL, IsDefault bit NOT NULL DEFAULT 0, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);",
		"IF OBJECT_ID(N'CustomerReturns',N'U') IS NULL CREATE TABLE CustomerReturns (Id bigint IDENTITY(1,1) PRIMARY KEY, ReturnNumber nvarchar(100) NOT NULL UNIQUE, ShipmentId bigint NOT NULL REFERENCES Shipments(Id), SalesOrderId bigint NOT NULL REFERENCES SalesOrders(Id), CustomerId bigint NOT NULL REFERENCES Customers(Id), ReturnDate date NOT NULL, Status int NOT NULL DEFAULT 1, Reason nvarchar(2000) NOT NULL, CreatedByUserId bigint NOT NULL REFERENCES Users(Id), PostedByUserId bigint NULL REFERENCES Users(Id), PostedAtUtc nvarchar(40) NULL, Version bigint NOT NULL DEFAULT 1);",
		"IF OBJECT_ID(N'CustomerReturnLines',N'U') IS NULL CREATE TABLE CustomerReturnLines (Id bigint IDENTITY(1,1) PRIMARY KEY, CustomerReturnId bigint NOT NULL REFERENCES CustomerReturns(Id), ShipmentLineId bigint NOT NULL REFERENCES ShipmentLines(Id), InventoryId bigint NOT NULL REFERENCES Inventories(Id), Quantity int NOT NULL, Version bigint NOT NULL DEFAULT 1);",
		"IF OBJECT_ID(N'SalesCreditNotes',N'U') IS NULL CREATE TABLE SalesCreditNotes (Id bigint IDENTITY(1,1) PRIMARY KEY, CreditNoteNumber nvarchar(100) NOT NULL UNIQUE, SalesInvoiceId bigint NOT NULL REFERENCES SalesInvoices(Id), CustomerId bigint NOT NULL REFERENCES Customers(Id), CreditDate date NOT NULL, Status int NOT NULL DEFAULT 1, Reason nvarchar(2000) NOT NULL, CreatedByUserId bigint NOT NULL REFERENCES Users(Id), PostedByUserId bigint NULL REFERENCES Users(Id), PostedAtUtc nvarchar(40) NULL, Version bigint NOT NULL DEFAULT 1);",
		"IF OBJECT_ID(N'SalesCreditNoteLines',N'U') IS NULL CREATE TABLE SalesCreditNoteLines (Id bigint IDENTITY(1,1) PRIMARY KEY, SalesCreditNoteId bigint NOT NULL REFERENCES SalesCreditNotes(Id), SalesInvoiceLineId bigint NOT NULL REFERENCES SalesInvoiceLines(Id), Quantity int NOT NULL, UnitPrice decimal(18,4) NOT NULL, DiscountPercent decimal(9,4) NOT NULL DEFAULT 0, TaxRate decimal(9,4) NOT NULL DEFAULT 19, Version bigint NOT NULL DEFAULT 1);"
	];

	private static readonly string[] MySql =
	[
		"ALTER TABLE Shipments ADD COLUMN ReversedAtUtc VARCHAR(40) NULL;",
		"ALTER TABLE Shipments ADD COLUMN ReversedByUserId BIGINT NULL;",
		"ALTER TABLE Shipments ADD COLUMN ReversalReason TEXT NULL;",
		"CREATE TABLE IF NOT EXISTS CustomerAddresses (Id BIGINT AUTO_INCREMENT PRIMARY KEY, CustomerId BIGINT NOT NULL, Type INT NOT NULL, Name VARCHAR(250) NULL, Address TEXT NOT NULL, IsDefault TINYINT(1) NOT NULL DEFAULT 0, IsActive TINYINT(1) NOT NULL DEFAULT 1, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_CustomerAddresses_Customers FOREIGN KEY(CustomerId) REFERENCES Customers(Id));",
		"CREATE TABLE IF NOT EXISTS CustomerReturns (Id BIGINT AUTO_INCREMENT PRIMARY KEY, ReturnNumber VARCHAR(100) NOT NULL UNIQUE, ShipmentId BIGINT NOT NULL, SalesOrderId BIGINT NOT NULL, CustomerId BIGINT NOT NULL, ReturnDate DATE NOT NULL, Status INT NOT NULL DEFAULT 1, Reason TEXT NOT NULL, CreatedByUserId BIGINT NOT NULL, PostedByUserId BIGINT NULL, PostedAtUtc VARCHAR(40) NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_CustomerReturns_Shipments FOREIGN KEY(ShipmentId) REFERENCES Shipments(Id), CONSTRAINT FK_CustomerReturns_Orders FOREIGN KEY(SalesOrderId) REFERENCES SalesOrders(Id), CONSTRAINT FK_CustomerReturns_Customers FOREIGN KEY(CustomerId) REFERENCES Customers(Id));",
		"CREATE TABLE IF NOT EXISTS CustomerReturnLines (Id BIGINT AUTO_INCREMENT PRIMARY KEY, CustomerReturnId BIGINT NOT NULL, ShipmentLineId BIGINT NOT NULL, InventoryId BIGINT NOT NULL, Quantity INT NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_CustomerReturnLines_Returns FOREIGN KEY(CustomerReturnId) REFERENCES CustomerReturns(Id), CONSTRAINT FK_CustomerReturnLines_ShipmentLines FOREIGN KEY(ShipmentLineId) REFERENCES ShipmentLines(Id), CONSTRAINT FK_CustomerReturnLines_Inventories FOREIGN KEY(InventoryId) REFERENCES Inventories(Id));",
		"CREATE TABLE IF NOT EXISTS SalesCreditNotes (Id BIGINT AUTO_INCREMENT PRIMARY KEY, CreditNoteNumber VARCHAR(100) NOT NULL UNIQUE, SalesInvoiceId BIGINT NOT NULL, CustomerId BIGINT NOT NULL, CreditDate DATE NOT NULL, Status INT NOT NULL DEFAULT 1, Reason TEXT NOT NULL, CreatedByUserId BIGINT NOT NULL, PostedByUserId BIGINT NULL, PostedAtUtc VARCHAR(40) NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_SalesCreditNotes_Invoices FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id), CONSTRAINT FK_SalesCreditNotes_Customers FOREIGN KEY(CustomerId) REFERENCES Customers(Id));",
		"CREATE TABLE IF NOT EXISTS SalesCreditNoteLines (Id BIGINT AUTO_INCREMENT PRIMARY KEY, SalesCreditNoteId BIGINT NOT NULL, SalesInvoiceLineId BIGINT NOT NULL, Quantity INT NOT NULL, UnitPrice DECIMAL(18,4) NOT NULL, DiscountPercent DECIMAL(9,4) NOT NULL DEFAULT 0, TaxRate DECIMAL(9,4) NOT NULL DEFAULT 19, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_SalesCreditNoteLines_Notes FOREIGN KEY(SalesCreditNoteId) REFERENCES SalesCreditNotes(Id), CONSTRAINT FK_SalesCreditNoteLines_InvoiceLines FOREIGN KEY(SalesInvoiceLineId) REFERENCES SalesInvoiceLines(Id));"
	];
}
