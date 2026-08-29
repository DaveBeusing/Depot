// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class FinanceInventoryAccountingSchemaInitializer
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		foreach (var statement in Statements(connectionFactory.Provider))
		{
			command.CommandText = statement;
			command.Parameters.Clear();
			command.ExecuteNonQuery();
		}
	}

	private static IReadOnlyList<string> Statements(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => LocalStatements,
		DatabaseProvider.SqlServer => SqlServerStatements,
		DatabaseProvider.MySql => MySqlStatements,
		_ => throw new NotSupportedException($"Finance Inventory Accounting schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] LocalStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceInventoryAccountingConfigurations (Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, SingletonKey INTEGER NOT NULL DEFAULT 1 UNIQUE, LegalEntityId TEXT NOT NULL, FiscalCalendarId TEXT NOT NULL, PurchaseOrderPriceCurrency TEXT NOT NULL, ValuationMethod INTEGER NOT NULL, GoodsReceiptPostingProfileId INTEGER NOT NULL, SalesIssuePostingProfileId INTEGER NOT NULL, IsActive INTEGER NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryValuationLayers (Id INTEGER PRIMARY KEY AUTOINCREMENT, AccountingBookId TEXT NOT NULL, ItemId INTEGER NOT NULL, SourceMovementId INTEGER NOT NULL UNIQUE, AcquiredDate TEXT NOT NULL, CurrencyCode TEXT NOT NULL, OriginalQuantity INTEGER NOT NULL, RemainingQuantity INTEGER NOT NULL, UnitCost NUMERIC NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NULL, ReversedAtUtc TEXT NULL, ReversedByUserId INTEGER NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceInventoryValuationLayers_Fifo ON FinanceInventoryValuationLayers (AccountingBookId,ItemId,ReversedAtUtc,RemainingQuantity,AcquiredDate,Id);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryValuationConsumptions (Id INTEGER PRIMARY KEY AUTOINCREMENT, MovementId INTEGER NOT NULL, LayerId INTEGER NOT NULL, Quantity INTEGER NOT NULL, UnitCost NUMERIC NOT NULL, Amount NUMERIC NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NULL, ReversedAtUtc TEXT NULL, ReversedByUserId INTEGER NULL, UNIQUE(MovementId,LayerId));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceInventoryValuationConsumptions_Movement ON FinanceInventoryValuationConsumptions (MovementId,ReversedAtUtc,Id);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryAccountingEvents (Id INTEGER PRIMARY KEY AUTOINCREMENT, MovementId INTEGER NOT NULL UNIQUE, Kind INTEGER NOT NULL, AccountingBookId TEXT NOT NULL, ItemId INTEGER NOT NULL, Quantity INTEGER NOT NULL, CurrencyCode TEXT NOT NULL, Amount NUMERIC NOT NULL, JournalEntryId INTEGER NOT NULL, OperationId TEXT NOT NULL UNIQUE, ReversalOfMovementId INTEGER NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceInventoryAccountingEvents_BookItem ON FinanceInventoryAccountingEvents (AccountingBookId,ItemId,CreatedAtUtc,Id);"
	];

	private static readonly string[] SqlServerStatements =
	[
		"IF OBJECT_ID(N'FinanceInventoryAccountingConfigurations',N'U') IS NULL CREATE TABLE FinanceInventoryAccountingConfigurations (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Version bigint NOT NULL CONSTRAINT DF_FinanceInventoryAccountingConfigurations_Version DEFAULT 1, SingletonKey int NOT NULL CONSTRAINT DF_FinanceInventoryAccountingConfigurations_Singleton DEFAULT 1, LegalEntityId nvarchar(36) NOT NULL, FiscalCalendarId nvarchar(36) NOT NULL, PurchaseOrderPriceCurrency nvarchar(3) NOT NULL, ValuationMethod int NOT NULL, GoodsReceiptPostingProfileId bigint NOT NULL, SalesIssuePostingProfileId bigint NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceInventoryAccountingConfigurations_Singleton UNIQUE(SingletonKey));",
		"IF OBJECT_ID(N'FinanceInventoryValuationLayers',N'U') IS NULL CREATE TABLE FinanceInventoryValuationLayers (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, AccountingBookId nvarchar(36) NOT NULL, ItemId bigint NOT NULL, SourceMovementId bigint NOT NULL UNIQUE, AcquiredDate date NOT NULL, CurrencyCode nvarchar(3) NOT NULL, OriginalQuantity int NOT NULL, RemainingQuantity int NOT NULL, UnitCost decimal(28,9) NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NULL, ReversedAtUtc datetime2 NULL, ReversedByUserId bigint NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceInventoryValuationLayers_Fifo' AND object_id=OBJECT_ID(N'FinanceInventoryValuationLayers')) CREATE INDEX IX_FinanceInventoryValuationLayers_Fifo ON FinanceInventoryValuationLayers (AccountingBookId,ItemId,ReversedAtUtc,RemainingQuantity,AcquiredDate,Id);",
		"IF OBJECT_ID(N'FinanceInventoryValuationConsumptions',N'U') IS NULL CREATE TABLE FinanceInventoryValuationConsumptions (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, MovementId bigint NOT NULL, LayerId bigint NOT NULL, Quantity int NOT NULL, UnitCost decimal(28,9) NOT NULL, Amount decimal(28,9) NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NULL, ReversedAtUtc datetime2 NULL, ReversedByUserId bigint NULL, CONSTRAINT UQ_FinanceInventoryValuationConsumptions UNIQUE(MovementId,LayerId));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceInventoryValuationConsumptions_Movement' AND object_id=OBJECT_ID(N'FinanceInventoryValuationConsumptions')) CREATE INDEX IX_FinanceInventoryValuationConsumptions_Movement ON FinanceInventoryValuationConsumptions (MovementId,ReversedAtUtc,Id);",
		"IF OBJECT_ID(N'FinanceInventoryAccountingEvents',N'U') IS NULL CREATE TABLE FinanceInventoryAccountingEvents (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, MovementId bigint NOT NULL UNIQUE, Kind int NOT NULL, AccountingBookId nvarchar(36) NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, CurrencyCode nvarchar(3) NOT NULL, Amount decimal(28,9) NOT NULL, JournalEntryId bigint NOT NULL, OperationId nvarchar(36) NOT NULL UNIQUE, ReversalOfMovementId bigint NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceInventoryAccountingEvents_BookItem' AND object_id=OBJECT_ID(N'FinanceInventoryAccountingEvents')) CREATE INDEX IX_FinanceInventoryAccountingEvents_BookItem ON FinanceInventoryAccountingEvents (AccountingBookId,ItemId,CreatedAtUtc,Id);"
	];

	private static readonly string[] MySqlStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceInventoryAccountingConfigurations (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, SingletonKey INT NOT NULL DEFAULT 1 UNIQUE, LegalEntityId VARCHAR(36) NOT NULL, FiscalCalendarId VARCHAR(36) NOT NULL, PurchaseOrderPriceCurrency VARCHAR(3) NOT NULL, ValuationMethod INT NOT NULL, GoodsReceiptPostingProfileId BIGINT NOT NULL, SalesIssuePostingProfileId BIGINT NOT NULL, IsActive BOOLEAN NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryValuationLayers (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, AccountingBookId VARCHAR(36) NOT NULL, ItemId BIGINT NOT NULL, SourceMovementId BIGINT NOT NULL UNIQUE, AcquiredDate DATE NOT NULL, CurrencyCode VARCHAR(3) NOT NULL, OriginalQuantity INT NOT NULL, RemainingQuantity INT NOT NULL, UnitCost DECIMAL(28,9) NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NULL, ReversedAtUtc DATETIME(6) NULL, ReversedByUserId BIGINT NULL, INDEX IX_FinanceInventoryValuationLayers_Fifo (AccountingBookId,ItemId,ReversedAtUtc,RemainingQuantity,AcquiredDate,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryValuationConsumptions (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, MovementId BIGINT NOT NULL, LayerId BIGINT NOT NULL, Quantity INT NOT NULL, UnitCost DECIMAL(28,9) NOT NULL, Amount DECIMAL(28,9) NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NULL, ReversedAtUtc DATETIME(6) NULL, ReversedByUserId BIGINT NULL, UNIQUE KEY UQ_FinanceInventoryValuationConsumptions (MovementId,LayerId), INDEX IX_FinanceInventoryValuationConsumptions_Movement (MovementId,ReversedAtUtc,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryAccountingEvents (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, MovementId BIGINT NOT NULL UNIQUE, Kind INT NOT NULL, AccountingBookId VARCHAR(36) NOT NULL, ItemId BIGINT NOT NULL, Quantity INT NOT NULL, CurrencyCode VARCHAR(3) NOT NULL, Amount DECIMAL(28,9) NOT NULL, JournalEntryId BIGINT NOT NULL, OperationId VARCHAR(36) NOT NULL UNIQUE, ReversalOfMovementId BIGINT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NULL, INDEX IX_FinanceInventoryAccountingEvents_BookItem (AccountingBookId,ItemId,CreatedAtUtc,Id));"
	];
}
