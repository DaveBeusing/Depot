// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class SubscriptionBillingSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		if (connectionFactory.Provider == DatabaseProvider.Local) UpgradeSqliteInvoiceSource(connectionFactory, connection);
		else UpgradeServerInvoiceSource(connectionFactory, connection);
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Subscription billing is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private static void UpgradeSqliteInvoiceSource(IDatabaseConnectionFactory connectionFactory, System.Data.Common.DbConnection connection)
	{
		using (var foreignKeysOff = connection.CreateCommand()) { foreignKeysOff.CommandText = "PRAGMA foreign_keys=OFF;"; foreignKeysOff.ExecuteNonQuery(); }
		try
		{
			using var transaction = connectionFactory.BeginWriteTransaction(connection);
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = """
				DROP TABLE IF EXISTS SalesInvoiceLines_V16;
				DROP TABLE IF EXISTS SalesInvoices_V16;
				CREATE TABLE SalesInvoices_V16 (
					Id INTEGER PRIMARY KEY AUTOINCREMENT, InvoiceNumber TEXT NOT NULL UNIQUE, CustomerId INTEGER NOT NULL REFERENCES Customers(Id),
					SalesOrderId INTEGER NULL REFERENCES SalesOrders(Id), ShipmentId INTEGER NULL REFERENCES Shipments(Id), InvoiceDate TEXT NOT NULL,
					DueDate TEXT NOT NULL, Currency TEXT NOT NULL DEFAULT 'EUR', Status INTEGER NOT NULL DEFAULT 1, CustomerReference TEXT NULL,
					BillingAddress TEXT NULL, Notes TEXT NULL, CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id), PostedByUserId INTEGER NULL REFERENCES Users(Id),
					PostedAtUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 1);
				INSERT INTO SalesInvoices_V16 (Id,InvoiceNumber,CustomerId,SalesOrderId,ShipmentId,InvoiceDate,DueDate,Currency,Status,CustomerReference,BillingAddress,Notes,CreatedByUserId,PostedByUserId,PostedAtUtc,Version)
				SELECT Id,InvoiceNumber,CustomerId,SalesOrderId,ShipmentId,InvoiceDate,DueDate,Currency,Status,CustomerReference,BillingAddress,Notes,CreatedByUserId,PostedByUserId,PostedAtUtc,Version FROM SalesInvoices;
				CREATE UNIQUE INDEX UX_SalesInvoices_ShipmentId ON SalesInvoices_V16(ShipmentId) WHERE ShipmentId IS NOT NULL;
				CREATE TABLE SalesInvoiceLines_V16 (
					Id INTEGER PRIMARY KEY AUTOINCREMENT, SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices_V16(Id), LineNumber INTEGER NOT NULL,
					SalesOrderLineId INTEGER NULL REFERENCES SalesOrderLines(Id), ShipmentLineId INTEGER NULL REFERENCES ShipmentLines(Id),
					PartNumber TEXT NOT NULL, Description TEXT NOT NULL, Quantity INTEGER NOT NULL, UnitPrice NUMERIC NOT NULL,
					DiscountPercent NUMERIC NOT NULL DEFAULT 0, TaxRate NUMERIC NOT NULL DEFAULT 19, TaxCategoryCode TEXT NOT NULL DEFAULT 'S',
					TaxExemptionReasonCode TEXT NULL, TaxExemptionReason TEXT NULL, Version INTEGER NOT NULL DEFAULT 1, UNIQUE(SalesInvoiceId,LineNumber));
				INSERT INTO SalesInvoiceLines_V16 (Id,SalesInvoiceId,LineNumber,SalesOrderLineId,ShipmentLineId,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate,TaxCategoryCode,TaxExemptionReasonCode,TaxExemptionReason,Version)
				SELECT Id,SalesInvoiceId,LineNumber,SalesOrderLineId,ShipmentLineId,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate,TaxCategoryCode,TaxExemptionReasonCode,TaxExemptionReason,Version FROM SalesInvoiceLines;
				DROP TABLE SalesInvoiceLines;
				DROP TABLE SalesInvoices;
				ALTER TABLE SalesInvoices_V16 RENAME TO SalesInvoices;
				ALTER TABLE SalesInvoiceLines_V16 RENAME TO SalesInvoiceLines;
				CREATE INDEX IF NOT EXISTS IX_SalesInvoices_Customer_Status ON SalesInvoices(CustomerId,Status);
				""";
			command.ExecuteNonQuery();
			transaction.Commit();
		}
		finally { using var foreignKeysOn = connection.CreateCommand(); foreignKeysOn.CommandText = "PRAGMA foreign_keys=ON;"; foreignKeysOn.ExecuteNonQuery(); }
	}

	private static void UpgradeServerInvoiceSource(IDatabaseConnectionFactory connectionFactory, System.Data.Common.DbConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.SqlServer => """
				BEGIN TRANSACTION;
				BEGIN TRY
					DECLARE @uq sysname;
					SELECT TOP (1) @uq=kc.name FROM sys.key_constraints kc
					INNER JOIN sys.index_columns ic ON ic.object_id=kc.parent_object_id AND ic.index_id=kc.unique_index_id
					INNER JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
					WHERE kc.parent_object_id=OBJECT_ID(N'SalesInvoices') AND kc.type=N'UQ' AND c.name=N'ShipmentId';
					IF @uq IS NOT NULL
					BEGIN
						DECLARE @dropShipmentConstraint nvarchar(max)=N'ALTER TABLE SalesInvoices DROP CONSTRAINT ' + QUOTENAME(@uq);
						EXEC sys.sp_executesql @dropShipmentConstraint;
					END;
					ALTER TABLE SalesInvoices ALTER COLUMN SalesOrderId bigint NULL;
					ALTER TABLE SalesInvoices ALTER COLUMN ShipmentId bigint NULL;
					IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'SalesInvoices') AND name=N'UX_SalesInvoices_ShipmentId')
						CREATE UNIQUE INDEX UX_SalesInvoices_ShipmentId ON SalesInvoices(ShipmentId) WHERE ShipmentId IS NOT NULL;
					ALTER TABLE SalesInvoiceLines ALTER COLUMN SalesOrderLineId bigint NULL;
					ALTER TABLE SalesInvoiceLines ALTER COLUMN ShipmentLineId bigint NULL;
					COMMIT TRANSACTION;
				END TRY
				BEGIN CATCH
					IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
					THROW;
				END CATCH;
				""",
			DatabaseProvider.MySql => """
				ALTER TABLE SalesInvoices MODIFY SalesOrderId BIGINT NULL, MODIFY ShipmentId BIGINT NULL;
				ALTER TABLE SalesInvoiceLines MODIFY SalesOrderLineId BIGINT NULL, MODIFY ShipmentLineId BIGINT NULL;
				""",
			_ => throw new NotSupportedException($"Invoice-source migration is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private const string Sqlite = """
		CREATE TABLE IF NOT EXISTS SubscriptionContracts (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, TermsRevision INTEGER NOT NULL DEFAULT 1,
			ContractNumber TEXT NOT NULL UNIQUE, CustomerId INTEGER NOT NULL, LegalEntityId TEXT NOT NULL, Currency TEXT NOT NULL,
			StartDate TEXT NOT NULL, EndDate TEXT NULL, Cadence INTEGER NOT NULL, NextBillingDate TEXT NOT NULL, Status INTEGER NOT NULL,
			StatusEffectiveDate TEXT NOT NULL, OwnerUserId INTEGER NOT NULL, Notes TEXT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL,
			UpdatedAtUtc TEXT NOT NULL, UpdatedByUserId INTEGER NOT NULL, FOREIGN KEY(CustomerId) REFERENCES Customers(Id), FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			CHECK(Cadence BETWEEN 1 AND 3), CHECK(Status BETWEEN 1 AND 5), CHECK(TermsRevision > 0));
		CREATE INDEX IF NOT EXISTS IX_SubscriptionContracts_Due ON SubscriptionContracts(Status,NextBillingDate,Id);
		CREATE INDEX IF NOT EXISTS IX_SubscriptionContracts_Customer ON SubscriptionContracts(CustomerId,Status,ContractNumber);
		CREATE TABLE IF NOT EXISTS SubscriptionContractLines (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, ContractId INTEGER NOT NULL, TermsRevision INTEGER NOT NULL, LineNumber INTEGER NOT NULL,
			ItemId INTEGER NOT NULL, PartNumber TEXT NOT NULL, Description TEXT NOT NULL, Quantity INTEGER NOT NULL, UnitPrice NUMERIC NOT NULL,
			DiscountPercent NUMERIC NOT NULL DEFAULT 0, TaxRate NUMERIC NOT NULL DEFAULT 19, PricePolicy INTEGER NOT NULL, PriceSourceListId INTEGER NULL,
			PriceSourceName TEXT NULL, PriceSourceScope INTEGER NULL, PriceSourceCurrency TEXT NULL, UNIQUE(ContractId,TermsRevision,LineNumber),
			FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id), FOREIGN KEY(ItemId) REFERENCES Items(Id), FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id),
			CHECK(Quantity > 0), CHECK(PricePolicy BETWEEN 1 AND 2));
		CREATE INDEX IF NOT EXISTS IX_SubscriptionContractLines_Current ON SubscriptionContractLines(ContractId,TermsRevision,LineNumber);
		CREATE TABLE IF NOT EXISTS SubscriptionContractRevisions (
			ContractId INTEGER NOT NULL, TermsRevision INTEGER NOT NULL, EffectiveFrom TEXT NOT NULL, TermsSnapshotJson TEXT NOT NULL,
			CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL, PRIMARY KEY(ContractId,TermsRevision), FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id));
		CREATE TABLE IF NOT EXISTS SubscriptionLifecycleEvents (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, ContractId INTEGER NOT NULL, Action TEXT NOT NULL, EffectiveDate TEXT NOT NULL,
			PreviousStatus INTEGER NOT NULL, NewStatus INTEGER NOT NULL, Comment TEXT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL,
			FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id));
		CREATE INDEX IF NOT EXISTS IX_SubscriptionLifecycleEvents_ContractDate ON SubscriptionLifecycleEvents(ContractId,EffectiveDate,Id);
		CREATE TABLE IF NOT EXISTS SubscriptionBillingInstances (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, ContractId INTEGER NOT NULL, TermsRevision INTEGER NOT NULL,
			PeriodStart TEXT NOT NULL, PeriodEnd TEXT NOT NULL, BillingDate TEXT NOT NULL, Status INTEGER NOT NULL, SalesInvoiceId INTEGER NULL,
			BlockReason TEXT NULL, CreatedAtUtc TEXT NOT NULL, GeneratedAtUtc TEXT NULL, GeneratedByUserId INTEGER NULL,
			UNIQUE(ContractId,PeriodStart,PeriodEnd), FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id), FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id),
			CHECK(Status BETWEEN 1 AND 4));
		CREATE UNIQUE INDEX IF NOT EXISTS UX_SubscriptionBillingInstances_Invoice ON SubscriptionBillingInstances(SalesInvoiceId) WHERE SalesInvoiceId IS NOT NULL;
		CREATE INDEX IF NOT EXISTS IX_SubscriptionBillingInstances_Due ON SubscriptionBillingInstances(Status,BillingDate,Id);
		CREATE TABLE IF NOT EXISTS SubscriptionBillingPriceEvidence (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, BillingInstanceId INTEGER NOT NULL, ContractLineId INTEGER NOT NULL, LineNumber INTEGER NOT NULL,
			UnitPrice NUMERIC NOT NULL, DiscountPercent NUMERIC NOT NULL, PriceSourceListId INTEGER NULL, PriceSourceName TEXT NULL, PriceSourceScope INTEGER NULL,
			PriceSourceCurrency TEXT NULL, ResolvedAtUtc TEXT NOT NULL, UNIQUE(BillingInstanceId,ContractLineId),
			FOREIGN KEY(BillingInstanceId) REFERENCES SubscriptionBillingInstances(Id), FOREIGN KEY(ContractLineId) REFERENCES SubscriptionContractLines(Id),
			FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id));
		""";

	private const string SqlServer = """
		IF OBJECT_ID(N'SubscriptionContracts',N'U') IS NULL CREATE TABLE SubscriptionContracts (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, TermsRevision int NOT NULL DEFAULT 1,
			ContractNumber nvarchar(100) NOT NULL UNIQUE, CustomerId bigint NOT NULL, LegalEntityId nvarchar(36) NOT NULL, Currency nvarchar(3) NOT NULL,
			StartDate date NOT NULL, EndDate date NULL, Cadence int NOT NULL, NextBillingDate date NOT NULL, Status int NOT NULL, StatusEffectiveDate date NOT NULL,
			OwnerUserId bigint NOT NULL, Notes nvarchar(4000) NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL, UpdatedAtUtc datetime2 NOT NULL, UpdatedByUserId bigint NOT NULL,
			CONSTRAINT FK_SubscriptionContracts_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id), CONSTRAINT FK_SubscriptionContracts_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			CONSTRAINT CK_SubscriptionContracts_Cadence CHECK(Cadence BETWEEN 1 AND 3), CONSTRAINT CK_SubscriptionContracts_Status CHECK(Status BETWEEN 1 AND 5),
			CONSTRAINT CK_SubscriptionContracts_TermsRevision CHECK(TermsRevision > 0));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'SubscriptionContracts') AND name=N'IX_SubscriptionContracts_Due') CREATE INDEX IX_SubscriptionContracts_Due ON SubscriptionContracts(Status,NextBillingDate,Id);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'SubscriptionContracts') AND name=N'IX_SubscriptionContracts_Customer') CREATE INDEX IX_SubscriptionContracts_Customer ON SubscriptionContracts(CustomerId,Status,ContractNumber);
		IF OBJECT_ID(N'SubscriptionContractLines',N'U') IS NULL CREATE TABLE SubscriptionContractLines (
			Id bigint IDENTITY(1,1) PRIMARY KEY, ContractId bigint NOT NULL, TermsRevision int NOT NULL, LineNumber int NOT NULL, ItemId bigint NOT NULL,
			PartNumber nvarchar(100) NOT NULL, Description nvarchar(500) NOT NULL, Quantity int NOT NULL, UnitPrice decimal(18,4) NOT NULL,
			DiscountPercent decimal(9,4) NOT NULL DEFAULT 0, TaxRate decimal(9,4) NOT NULL DEFAULT 19, PricePolicy int NOT NULL, PriceSourceListId bigint NULL,
			PriceSourceName nvarchar(250) NULL, PriceSourceScope int NULL, PriceSourceCurrency nvarchar(3) NULL,
			CONSTRAINT UQ_SubscriptionContractLines_Revision UNIQUE(ContractId,TermsRevision,LineNumber),
			CONSTRAINT FK_SubscriptionContractLines_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id),
			CONSTRAINT FK_SubscriptionContractLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT FK_SubscriptionContractLines_PriceList FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id),
			CONSTRAINT CK_SubscriptionContractLines_Quantity CHECK(Quantity > 0), CONSTRAINT CK_SubscriptionContractLines_Policy CHECK(PricePolicy BETWEEN 1 AND 2));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'SubscriptionContractLines') AND name=N'IX_SubscriptionContractLines_Current') CREATE INDEX IX_SubscriptionContractLines_Current ON SubscriptionContractLines(ContractId,TermsRevision,LineNumber);
		IF OBJECT_ID(N'SubscriptionContractRevisions',N'U') IS NULL CREATE TABLE SubscriptionContractRevisions (
			ContractId bigint NOT NULL, TermsRevision int NOT NULL, EffectiveFrom date NOT NULL, TermsSnapshotJson nvarchar(max) NOT NULL,
			CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL, CONSTRAINT PK_SubscriptionContractRevisions PRIMARY KEY(ContractId,TermsRevision),
			CONSTRAINT FK_SubscriptionContractRevisions_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id));
		IF OBJECT_ID(N'SubscriptionLifecycleEvents',N'U') IS NULL CREATE TABLE SubscriptionLifecycleEvents (
			Id bigint IDENTITY(1,1) PRIMARY KEY, ContractId bigint NOT NULL, Action nvarchar(50) NOT NULL, EffectiveDate date NOT NULL,
			PreviousStatus int NOT NULL, NewStatus int NOT NULL, Comment nvarchar(1000) NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL,
			CONSTRAINT FK_SubscriptionLifecycleEvents_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'SubscriptionLifecycleEvents') AND name=N'IX_SubscriptionLifecycleEvents_ContractDate') CREATE INDEX IX_SubscriptionLifecycleEvents_ContractDate ON SubscriptionLifecycleEvents(ContractId,EffectiveDate,Id);
		IF OBJECT_ID(N'SubscriptionBillingInstances',N'U') IS NULL CREATE TABLE SubscriptionBillingInstances (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, ContractId bigint NOT NULL, TermsRevision int NOT NULL,
			PeriodStart date NOT NULL, PeriodEnd date NOT NULL, BillingDate date NOT NULL, Status int NOT NULL, SalesInvoiceId bigint NULL,
			BlockReason nvarchar(2000) NULL, CreatedAtUtc datetime2 NOT NULL, GeneratedAtUtc datetime2 NULL, GeneratedByUserId bigint NULL,
			CONSTRAINT UQ_SubscriptionBillingInstances_Period UNIQUE(ContractId,PeriodStart,PeriodEnd),
			CONSTRAINT FK_SubscriptionBillingInstances_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id),
			CONSTRAINT FK_SubscriptionBillingInstances_Invoice FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id),
			CONSTRAINT CK_SubscriptionBillingInstances_Status CHECK(Status BETWEEN 1 AND 4));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'SubscriptionBillingInstances') AND name=N'UX_SubscriptionBillingInstances_Invoice') CREATE UNIQUE INDEX UX_SubscriptionBillingInstances_Invoice ON SubscriptionBillingInstances(SalesInvoiceId) WHERE SalesInvoiceId IS NOT NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'SubscriptionBillingInstances') AND name=N'IX_SubscriptionBillingInstances_Due') CREATE INDEX IX_SubscriptionBillingInstances_Due ON SubscriptionBillingInstances(Status,BillingDate,Id);
		IF OBJECT_ID(N'SubscriptionBillingPriceEvidence',N'U') IS NULL CREATE TABLE SubscriptionBillingPriceEvidence (
			Id bigint IDENTITY(1,1) PRIMARY KEY, BillingInstanceId bigint NOT NULL, ContractLineId bigint NOT NULL, LineNumber int NOT NULL,
			UnitPrice decimal(18,4) NOT NULL, DiscountPercent decimal(9,4) NOT NULL, PriceSourceListId bigint NULL, PriceSourceName nvarchar(250) NULL,
			PriceSourceScope int NULL, PriceSourceCurrency nvarchar(3) NULL, ResolvedAtUtc datetime2 NOT NULL,
			CONSTRAINT UQ_SubscriptionBillingPriceEvidence_Line UNIQUE(BillingInstanceId,ContractLineId),
			CONSTRAINT FK_SubscriptionBillingPriceEvidence_Instance FOREIGN KEY(BillingInstanceId) REFERENCES SubscriptionBillingInstances(Id),
			CONSTRAINT FK_SubscriptionBillingPriceEvidence_Line FOREIGN KEY(ContractLineId) REFERENCES SubscriptionContractLines(Id),
			CONSTRAINT FK_SubscriptionBillingPriceEvidence_PriceList FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id));
		""";

	private const string MySql = """
		CREATE TABLE IF NOT EXISTS SubscriptionContracts (
			Id BIGINT AUTO_INCREMENT PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, TermsRevision INT NOT NULL DEFAULT 1, ContractNumber VARCHAR(100) NOT NULL UNIQUE,
			CustomerId BIGINT NOT NULL, LegalEntityId VARCHAR(36) NOT NULL, Currency VARCHAR(3) NOT NULL, StartDate DATE NOT NULL, EndDate DATE NULL, Cadence INT NOT NULL,
			NextBillingDate DATE NOT NULL, Status INT NOT NULL, StatusEffectiveDate DATE NOT NULL, OwnerUserId BIGINT NOT NULL, Notes VARCHAR(4000) NULL,
			CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL, UpdatedAtUtc DATETIME(6) NOT NULL, UpdatedByUserId BIGINT NOT NULL,
			INDEX IX_SubscriptionContracts_Due(Status,NextBillingDate,Id), INDEX IX_SubscriptionContracts_Customer(CustomerId,Status,ContractNumber),
			CONSTRAINT FK_SubscriptionContracts_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id), CONSTRAINT FK_SubscriptionContracts_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			CONSTRAINT CK_SubscriptionContracts_Cadence CHECK(Cadence BETWEEN 1 AND 3), CONSTRAINT CK_SubscriptionContracts_Status CHECK(Status BETWEEN 1 AND 5),
			CONSTRAINT CK_SubscriptionContracts_TermsRevision CHECK(TermsRevision > 0)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS SubscriptionContractLines (
			Id BIGINT AUTO_INCREMENT PRIMARY KEY, ContractId BIGINT NOT NULL, TermsRevision INT NOT NULL, LineNumber INT NOT NULL, ItemId BIGINT NOT NULL,
			PartNumber VARCHAR(100) NOT NULL, Description VARCHAR(500) NOT NULL, Quantity INT NOT NULL, UnitPrice DECIMAL(18,4) NOT NULL,
			DiscountPercent DECIMAL(9,4) NOT NULL DEFAULT 0, TaxRate DECIMAL(9,4) NOT NULL DEFAULT 19, PricePolicy INT NOT NULL, PriceSourceListId BIGINT NULL,
			PriceSourceName VARCHAR(250) NULL, PriceSourceScope INT NULL, PriceSourceCurrency VARCHAR(3) NULL,
			UNIQUE KEY UQ_SubscriptionContractLines_Revision(ContractId,TermsRevision,LineNumber), INDEX IX_SubscriptionContractLines_Current(ContractId,TermsRevision,LineNumber),
			CONSTRAINT FK_SubscriptionContractLines_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id), CONSTRAINT FK_SubscriptionContractLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT FK_SubscriptionContractLines_PriceList FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id),
			CONSTRAINT CK_SubscriptionContractLines_Quantity CHECK(Quantity > 0), CONSTRAINT CK_SubscriptionContractLines_Policy CHECK(PricePolicy BETWEEN 1 AND 2)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS SubscriptionContractRevisions (
			ContractId BIGINT NOT NULL, TermsRevision INT NOT NULL, EffectiveFrom DATE NOT NULL, TermsSnapshotJson LONGTEXT NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL,
			CreatedByUserId BIGINT NOT NULL, PRIMARY KEY(ContractId,TermsRevision), CONSTRAINT FK_SubscriptionContractRevisions_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS SubscriptionLifecycleEvents (
			Id BIGINT AUTO_INCREMENT PRIMARY KEY, ContractId BIGINT NOT NULL, Action VARCHAR(50) NOT NULL, EffectiveDate DATE NOT NULL,
			PreviousStatus INT NOT NULL, NewStatus INT NOT NULL, Comment VARCHAR(1000) NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL,
			INDEX IX_SubscriptionLifecycleEvents_ContractDate(ContractId,EffectiveDate,Id), CONSTRAINT FK_SubscriptionLifecycleEvents_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS SubscriptionBillingInstances (
			Id BIGINT AUTO_INCREMENT PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, ContractId BIGINT NOT NULL, TermsRevision INT NOT NULL, PeriodStart DATE NOT NULL,
			PeriodEnd DATE NOT NULL, BillingDate DATE NOT NULL, Status INT NOT NULL, SalesInvoiceId BIGINT NULL, BlockReason VARCHAR(2000) NULL, CreatedAtUtc DATETIME(6) NOT NULL,
			GeneratedAtUtc DATETIME(6) NULL, GeneratedByUserId BIGINT NULL, UNIQUE KEY UQ_SubscriptionBillingInstances_Period(ContractId,PeriodStart,PeriodEnd),
			UNIQUE KEY UX_SubscriptionBillingInstances_Invoice(SalesInvoiceId), INDEX IX_SubscriptionBillingInstances_Due(Status,BillingDate,Id),
			CONSTRAINT FK_SubscriptionBillingInstances_Contract FOREIGN KEY(ContractId) REFERENCES SubscriptionContracts(Id),
			CONSTRAINT FK_SubscriptionBillingInstances_Invoice FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id),
			CONSTRAINT CK_SubscriptionBillingInstances_Status CHECK(Status BETWEEN 1 AND 4)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS SubscriptionBillingPriceEvidence (
			Id BIGINT AUTO_INCREMENT PRIMARY KEY, BillingInstanceId BIGINT NOT NULL, ContractLineId BIGINT NOT NULL, LineNumber INT NOT NULL,
			UnitPrice DECIMAL(18,4) NOT NULL, DiscountPercent DECIMAL(9,4) NOT NULL, PriceSourceListId BIGINT NULL, PriceSourceName VARCHAR(250) NULL,
			PriceSourceScope INT NULL, PriceSourceCurrency VARCHAR(3) NULL, ResolvedAtUtc DATETIME(6) NOT NULL, UNIQUE KEY UQ_SubscriptionBillingPriceEvidence_Line(BillingInstanceId,ContractLineId),
			CONSTRAINT FK_SubscriptionBillingPriceEvidence_Instance FOREIGN KEY(BillingInstanceId) REFERENCES SubscriptionBillingInstances(Id),
			CONSTRAINT FK_SubscriptionBillingPriceEvidence_Line FOREIGN KEY(ContractLineId) REFERENCES SubscriptionContractLines(Id),
			CONSTRAINT FK_SubscriptionBillingPriceEvidence_PriceList FOREIGN KEY(PriceSourceListId) REFERENCES SalesPriceLists(Id)) ENGINE=InnoDB;
		""";
}
