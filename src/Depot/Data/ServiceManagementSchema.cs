// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

public static class ServiceManagementSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Service-management persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private const string Sqlite = """
		CREATE TABLE IF NOT EXISTS ServiceCases (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			Version INTEGER NOT NULL DEFAULT 1,
			CaseNumber TEXT NOT NULL UNIQUE,
			CustomerId INTEGER NOT NULL,
			CustomerContactId INTEGER NULL,
			Subject TEXT NOT NULL,
			Description TEXT NULL,
			Category TEXT NULL,
			Priority INTEGER NOT NULL,
			Status INTEGER NOT NULL,
			OwnerUserId INTEGER NULL,
			SalesOrderId INTEGER NULL,
			ItemId INTEGER NULL,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			UpdatedAtUtc TEXT NOT NULL,
			UpdatedByUserId INTEGER NOT NULL,
			DueAtUtc TEXT NULL,
			ResolvedAtUtc TEXT NULL,
			ResolvedByUserId INTEGER NULL,
			ClosedAtUtc TEXT NULL,
			ClosedByUserId INTEGER NULL,
			CancelledAtUtc TEXT NULL,
			CancelledByUserId INTEGER NULL,
			FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			FOREIGN KEY(CustomerContactId) REFERENCES CustomerContacts(Id),
			FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			FOREIGN KEY(SalesOrderId) REFERENCES SalesOrders(Id),
			FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CHECK(Priority BETWEEN 1 AND 4),
			CHECK(Status BETWEEN 1 AND 7));
		CREATE INDEX IF NOT EXISTS IX_ServiceCases_Owner_Status_Due ON ServiceCases(OwnerUserId,Status,DueAtUtc,Id);
		CREATE INDEX IF NOT EXISTS IX_ServiceCases_Customer_Status ON ServiceCases(CustomerId,Status,Id);
		CREATE TABLE IF NOT EXISTS ServiceCaseHistory (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			ServiceCaseId INTEGER NOT NULL,
			PreviousStatus INTEGER NULL,
			Status INTEGER NOT NULL,
			Note TEXT NULL,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			FOREIGN KEY(ServiceCaseId) REFERENCES ServiceCases(Id),
			CHECK(PreviousStatus IS NULL OR PreviousStatus BETWEEN 1 AND 7),
			CHECK(Status BETWEEN 1 AND 7));
		CREATE INDEX IF NOT EXISTS IX_ServiceCaseHistory_Case ON ServiceCaseHistory(ServiceCaseId,CreatedAtUtc,Id);
		CREATE TABLE IF NOT EXISTS ServiceOrders (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			Version INTEGER NOT NULL DEFAULT 1,
			OrderNumber TEXT NOT NULL UNIQUE,
			ServiceCaseId INTEGER NOT NULL UNIQUE,
			CustomerId INTEGER NOT NULL,
			AssignedOwnerUserId INTEGER NULL,
			ServicedItemId INTEGER NULL,
			ServicedInventoryId INTEGER NULL,
			SerialLotReference TEXT NULL,
			Status INTEGER NOT NULL,
			PlannedStartAtUtc TEXT NULL,
			PlannedEndAtUtc TEXT NULL,
			CompletionNotes TEXT NULL,
			CompletedAtUtc TEXT NULL,
			CompletedByUserId INTEGER NULL,
			SalesInvoiceId INTEGER NULL UNIQUE,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			UpdatedAtUtc TEXT NOT NULL,
			UpdatedByUserId INTEGER NOT NULL,
			FOREIGN KEY(ServiceCaseId) REFERENCES ServiceCases(Id),
			FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			FOREIGN KEY(AssignedOwnerUserId) REFERENCES Users(Id),
			FOREIGN KEY(ServicedItemId) REFERENCES Items(Id),
			FOREIGN KEY(ServicedInventoryId) REFERENCES Inventories(Id),
			FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id),
			CHECK(Status BETWEEN 1 AND 4));
		CREATE INDEX IF NOT EXISTS IX_ServiceOrders_Owner_Status ON ServiceOrders(AssignedOwnerUserId,Status,PlannedEndAtUtc,Id);
		CREATE TABLE IF NOT EXISTS ServiceWorkLines (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			Version INTEGER NOT NULL DEFAULT 1,
			ServiceOrderId INTEGER NOT NULL,
			Description TEXT NOT NULL,
			Quantity REAL NOT NULL,
			UnitPrice REAL NOT NULL,
			Billable INTEGER NOT NULL,
			TaxRate REAL NOT NULL,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			FOREIGN KEY(ServiceOrderId) REFERENCES ServiceOrders(Id),
			CHECK(Quantity > 0),
			CHECK(UnitPrice >= 0),
			CHECK(Billable IN (0,1)),
			CHECK(TaxRate >= 0));
		CREATE INDEX IF NOT EXISTS IX_ServiceWorkLines_Order ON ServiceWorkLines(ServiceOrderId,Id);
		CREATE TABLE IF NOT EXISTS ServicePartEvidence (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			ServiceOrderId INTEGER NOT NULL,
			StockMovementId INTEGER NOT NULL UNIQUE,
			MovementKind INTEGER NOT NULL,
			InventoryId INTEGER NOT NULL,
			ItemId INTEGER NOT NULL,
			PartNumber TEXT NOT NULL,
			Description TEXT NOT NULL,
			Quantity INTEGER NOT NULL,
			UnitPrice REAL NOT NULL,
			Billable INTEGER NOT NULL,
			TaxRate REAL NOT NULL,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			FOREIGN KEY(ServiceOrderId) REFERENCES ServiceOrders(Id),
			FOREIGN KEY(StockMovementId) REFERENCES StockMovements(Id),
			FOREIGN KEY(InventoryId) REFERENCES Inventories(Id),
			FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CHECK(MovementKind IN (1,2)),
			CHECK(Quantity > 0),
			CHECK(UnitPrice >= 0),
			CHECK(Billable IN (0,1)),
			CHECK(TaxRate >= 0));
		CREATE INDEX IF NOT EXISTS IX_ServicePartEvidence_Order ON ServicePartEvidence(ServiceOrderId,Id);
		""";

	private const string SqlServer = """
		IF OBJECT_ID(N'ServiceCases',N'U') IS NULL CREATE TABLE ServiceCases (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, CaseNumber nvarchar(40) NOT NULL UNIQUE,
			CustomerId bigint NOT NULL, CustomerContactId bigint NULL, Subject nvarchar(200) NOT NULL, Description nvarchar(4000) NULL,
			Category nvarchar(100) NULL, Priority int NOT NULL, Status int NOT NULL, OwnerUserId bigint NULL, SalesOrderId bigint NULL, ItemId bigint NULL,
			CreatedAtUtc datetime2(6) NOT NULL, CreatedByUserId bigint NOT NULL, UpdatedAtUtc datetime2(6) NOT NULL, UpdatedByUserId bigint NOT NULL,
			DueAtUtc datetime2(6) NULL, ResolvedAtUtc datetime2(6) NULL, ResolvedByUserId bigint NULL, ClosedAtUtc datetime2(6) NULL, ClosedByUserId bigint NULL,
			CancelledAtUtc datetime2(6) NULL, CancelledByUserId bigint NULL,
			CONSTRAINT FK_ServiceCases_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			CONSTRAINT FK_ServiceCases_Contact FOREIGN KEY(CustomerContactId) REFERENCES CustomerContacts(Id),
			CONSTRAINT FK_ServiceCases_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ServiceCases_SalesOrder FOREIGN KEY(SalesOrderId) REFERENCES SalesOrders(Id),
			CONSTRAINT FK_ServiceCases_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT CK_ServiceCases_Priority CHECK(Priority BETWEEN 1 AND 4),
			CONSTRAINT CK_ServiceCases_Status CHECK(Status BETWEEN 1 AND 7));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ServiceCases_Owner_Status_Due' AND object_id=OBJECT_ID(N'ServiceCases')) CREATE INDEX IX_ServiceCases_Owner_Status_Due ON ServiceCases(OwnerUserId,Status,DueAtUtc,Id);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ServiceCases_Customer_Status' AND object_id=OBJECT_ID(N'ServiceCases')) CREATE INDEX IX_ServiceCases_Customer_Status ON ServiceCases(CustomerId,Status,Id);
		IF OBJECT_ID(N'ServiceCaseHistory',N'U') IS NULL CREATE TABLE ServiceCaseHistory (
			Id bigint IDENTITY(1,1) PRIMARY KEY, ServiceCaseId bigint NOT NULL, PreviousStatus int NULL, Status int NOT NULL, Note nvarchar(2000) NULL,
			CreatedAtUtc datetime2(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			CONSTRAINT FK_ServiceCaseHistory_Case FOREIGN KEY(ServiceCaseId) REFERENCES ServiceCases(Id),
			CONSTRAINT CK_ServiceCaseHistory_Previous CHECK(PreviousStatus IS NULL OR PreviousStatus BETWEEN 1 AND 7),
			CONSTRAINT CK_ServiceCaseHistory_Status CHECK(Status BETWEEN 1 AND 7));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ServiceCaseHistory_Case' AND object_id=OBJECT_ID(N'ServiceCaseHistory')) CREATE INDEX IX_ServiceCaseHistory_Case ON ServiceCaseHistory(ServiceCaseId,CreatedAtUtc,Id);
		IF OBJECT_ID(N'ServiceOrders',N'U') IS NULL CREATE TABLE ServiceOrders (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, OrderNumber nvarchar(40) NOT NULL UNIQUE,
			ServiceCaseId bigint NOT NULL UNIQUE, CustomerId bigint NOT NULL, AssignedOwnerUserId bigint NULL, ServicedItemId bigint NULL,
			ServicedInventoryId bigint NULL, SerialLotReference nvarchar(200) NULL, Status int NOT NULL, PlannedStartAtUtc datetime2(6) NULL,
			PlannedEndAtUtc datetime2(6) NULL, CompletionNotes nvarchar(4000) NULL, CompletedAtUtc datetime2(6) NULL, CompletedByUserId bigint NULL,
			SalesInvoiceId bigint NULL UNIQUE, CreatedAtUtc datetime2(6) NOT NULL, CreatedByUserId bigint NOT NULL, UpdatedAtUtc datetime2(6) NOT NULL, UpdatedByUserId bigint NOT NULL,
			CONSTRAINT FK_ServiceOrders_Case FOREIGN KEY(ServiceCaseId) REFERENCES ServiceCases(Id),
			CONSTRAINT FK_ServiceOrders_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			CONSTRAINT FK_ServiceOrders_Owner FOREIGN KEY(AssignedOwnerUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ServiceOrders_Item FOREIGN KEY(ServicedItemId) REFERENCES Items(Id),
			CONSTRAINT FK_ServiceOrders_Inventory FOREIGN KEY(ServicedInventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_ServiceOrders_Invoice FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id),
			CONSTRAINT CK_ServiceOrders_Status CHECK(Status BETWEEN 1 AND 4));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ServiceOrders_Owner_Status' AND object_id=OBJECT_ID(N'ServiceOrders')) CREATE INDEX IX_ServiceOrders_Owner_Status ON ServiceOrders(AssignedOwnerUserId,Status,PlannedEndAtUtc,Id);
		IF OBJECT_ID(N'ServiceWorkLines',N'U') IS NULL CREATE TABLE ServiceWorkLines (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, ServiceOrderId bigint NOT NULL, Description nvarchar(1000) NOT NULL,
			Quantity decimal(18,4) NOT NULL, UnitPrice decimal(19,4) NOT NULL, Billable bit NOT NULL, TaxRate decimal(9,4) NOT NULL,
			CreatedAtUtc datetime2(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			CONSTRAINT FK_ServiceWorkLines_Order FOREIGN KEY(ServiceOrderId) REFERENCES ServiceOrders(Id),
			CONSTRAINT CK_ServiceWorkLines_Quantity CHECK(Quantity>0), CONSTRAINT CK_ServiceWorkLines_UnitPrice CHECK(UnitPrice>=0), CONSTRAINT CK_ServiceWorkLines_TaxRate CHECK(TaxRate>=0));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ServiceWorkLines_Order' AND object_id=OBJECT_ID(N'ServiceWorkLines')) CREATE INDEX IX_ServiceWorkLines_Order ON ServiceWorkLines(ServiceOrderId,Id);
		IF OBJECT_ID(N'ServicePartEvidence',N'U') IS NULL CREATE TABLE ServicePartEvidence (
			Id bigint IDENTITY(1,1) PRIMARY KEY, ServiceOrderId bigint NOT NULL, StockMovementId bigint NOT NULL UNIQUE, MovementKind int NOT NULL,
			InventoryId bigint NOT NULL, ItemId bigint NOT NULL, PartNumber nvarchar(100) NOT NULL, Description nvarchar(500) NOT NULL, Quantity int NOT NULL,
			UnitPrice decimal(19,4) NOT NULL, Billable bit NOT NULL, TaxRate decimal(9,4) NOT NULL, CreatedAtUtc datetime2(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			CONSTRAINT FK_ServicePartEvidence_Order FOREIGN KEY(ServiceOrderId) REFERENCES ServiceOrders(Id),
			CONSTRAINT FK_ServicePartEvidence_Movement FOREIGN KEY(StockMovementId) REFERENCES StockMovements(Id),
			CONSTRAINT FK_ServicePartEvidence_Inventory FOREIGN KEY(InventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_ServicePartEvidence_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT CK_ServicePartEvidence_Kind CHECK(MovementKind IN (1,2)), CONSTRAINT CK_ServicePartEvidence_Quantity CHECK(Quantity>0),
			CONSTRAINT CK_ServicePartEvidence_UnitPrice CHECK(UnitPrice>=0), CONSTRAINT CK_ServicePartEvidence_TaxRate CHECK(TaxRate>=0));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ServicePartEvidence_Order' AND object_id=OBJECT_ID(N'ServicePartEvidence')) CREATE INDEX IX_ServicePartEvidence_Order ON ServicePartEvidence(ServiceOrderId,Id);
		""";

	private const string MySql = """
		CREATE TABLE IF NOT EXISTS ServiceCases (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, CaseNumber varchar(40) NOT NULL UNIQUE,
			CustomerId bigint NOT NULL, CustomerContactId bigint NULL, Subject varchar(200) NOT NULL, Description varchar(4000) NULL,
			Category varchar(100) NULL, Priority int NOT NULL, Status int NOT NULL, OwnerUserId bigint NULL, SalesOrderId bigint NULL, ItemId bigint NULL,
			CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL, UpdatedAtUtc datetime(6) NOT NULL, UpdatedByUserId bigint NOT NULL,
			DueAtUtc datetime(6) NULL, ResolvedAtUtc datetime(6) NULL, ResolvedByUserId bigint NULL, ClosedAtUtc datetime(6) NULL, ClosedByUserId bigint NULL,
			CancelledAtUtc datetime(6) NULL, CancelledByUserId bigint NULL,
			INDEX IX_ServiceCases_Owner_Status_Due(OwnerUserId,Status,DueAtUtc,Id), INDEX IX_ServiceCases_Customer_Status(CustomerId,Status,Id),
			CONSTRAINT FK_ServiceCases_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			CONSTRAINT FK_ServiceCases_Contact FOREIGN KEY(CustomerContactId) REFERENCES CustomerContacts(Id),
			CONSTRAINT FK_ServiceCases_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ServiceCases_SalesOrder FOREIGN KEY(SalesOrderId) REFERENCES SalesOrders(Id),
			CONSTRAINT FK_ServiceCases_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT CK_ServiceCases_Priority CHECK(Priority BETWEEN 1 AND 4), CONSTRAINT CK_ServiceCases_Status CHECK(Status BETWEEN 1 AND 7)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ServiceCaseHistory (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ServiceCaseId bigint NOT NULL, PreviousStatus int NULL, Status int NOT NULL,
			Note varchar(2000) NULL, CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			INDEX IX_ServiceCaseHistory_Case(ServiceCaseId,CreatedAtUtc,Id),
			CONSTRAINT FK_ServiceCaseHistory_Case FOREIGN KEY(ServiceCaseId) REFERENCES ServiceCases(Id),
			CONSTRAINT CK_ServiceCaseHistory_Previous CHECK(PreviousStatus IS NULL OR PreviousStatus BETWEEN 1 AND 7),
			CONSTRAINT CK_ServiceCaseHistory_Status CHECK(Status BETWEEN 1 AND 7)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ServiceOrders (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, OrderNumber varchar(40) NOT NULL UNIQUE,
			ServiceCaseId bigint NOT NULL UNIQUE, CustomerId bigint NOT NULL, AssignedOwnerUserId bigint NULL, ServicedItemId bigint NULL,
			ServicedInventoryId bigint NULL, SerialLotReference varchar(200) NULL, Status int NOT NULL, PlannedStartAtUtc datetime(6) NULL,
			PlannedEndAtUtc datetime(6) NULL, CompletionNotes varchar(4000) NULL, CompletedAtUtc datetime(6) NULL, CompletedByUserId bigint NULL,
			SalesInvoiceId bigint NULL UNIQUE, CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL, UpdatedAtUtc datetime(6) NOT NULL, UpdatedByUserId bigint NOT NULL,
			INDEX IX_ServiceOrders_Owner_Status(AssignedOwnerUserId,Status,PlannedEndAtUtc,Id),
			CONSTRAINT FK_ServiceOrders_Case FOREIGN KEY(ServiceCaseId) REFERENCES ServiceCases(Id),
			CONSTRAINT FK_ServiceOrders_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			CONSTRAINT FK_ServiceOrders_Owner FOREIGN KEY(AssignedOwnerUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ServiceOrders_Item FOREIGN KEY(ServicedItemId) REFERENCES Items(Id),
			CONSTRAINT FK_ServiceOrders_Inventory FOREIGN KEY(ServicedInventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_ServiceOrders_Invoice FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id),
			CONSTRAINT CK_ServiceOrders_Status CHECK(Status BETWEEN 1 AND 4)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ServiceWorkLines (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, ServiceOrderId bigint NOT NULL, Description varchar(1000) NOT NULL,
			Quantity decimal(18,4) NOT NULL, UnitPrice decimal(19,4) NOT NULL, Billable boolean NOT NULL, TaxRate decimal(9,4) NOT NULL,
			CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL, INDEX IX_ServiceWorkLines_Order(ServiceOrderId,Id),
			CONSTRAINT FK_ServiceWorkLines_Order FOREIGN KEY(ServiceOrderId) REFERENCES ServiceOrders(Id),
			CONSTRAINT CK_ServiceWorkLines_Quantity CHECK(Quantity>0), CONSTRAINT CK_ServiceWorkLines_UnitPrice CHECK(UnitPrice>=0), CONSTRAINT CK_ServiceWorkLines_TaxRate CHECK(TaxRate>=0)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ServicePartEvidence (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ServiceOrderId bigint NOT NULL, StockMovementId bigint NOT NULL UNIQUE, MovementKind int NOT NULL,
			InventoryId bigint NOT NULL, ItemId bigint NOT NULL, PartNumber varchar(100) NOT NULL, Description varchar(500) NOT NULL, Quantity int NOT NULL,
			UnitPrice decimal(19,4) NOT NULL, Billable boolean NOT NULL, TaxRate decimal(9,4) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			INDEX IX_ServicePartEvidence_Order(ServiceOrderId,Id),
			CONSTRAINT FK_ServicePartEvidence_Order FOREIGN KEY(ServiceOrderId) REFERENCES ServiceOrders(Id),
			CONSTRAINT FK_ServicePartEvidence_Movement FOREIGN KEY(StockMovementId) REFERENCES StockMovements(Id),
			CONSTRAINT FK_ServicePartEvidence_Inventory FOREIGN KEY(InventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_ServicePartEvidence_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT CK_ServicePartEvidence_Kind CHECK(MovementKind IN (1,2)), CONSTRAINT CK_ServicePartEvidence_Quantity CHECK(Quantity>0),
			CONSTRAINT CK_ServicePartEvidence_UnitPrice CHECK(UnitPrice>=0), CONSTRAINT CK_ServicePartEvidence_TaxRate CHECK(TaxRate>=0)) ENGINE=InnoDB;
		""";
}
