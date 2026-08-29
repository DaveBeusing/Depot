// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class FinanceInventoryAccountingAdvancedSchemaInitializer
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
		_ => throw new NotSupportedException($"Advanced Inventory Accounting schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] LocalStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceInventoryAccountingPolicies (Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, SingletonKey INTEGER NOT NULL DEFAULT 1 UNIQUE, InventoryControlAccountId TEXT NOT NULL, InventoryAdjustmentPostingProfileId INTEGER NOT NULL, PurchaseVariancePostingProfileId INTEGER NOT NULL, LandedCostPostingProfileId INTEGER NOT NULL, IsActive INTEGER NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryPurchaseVariances (Id INTEGER PRIMARY KEY AUTOINCREMENT, SupplierDocumentId INTEGER NOT NULL UNIQUE, OperationId TEXT NOT NULL UNIQUE, CurrencyCode TEXT NOT NULL, ExpectedNetAmount NUMERIC NOT NULL, ActualNetAmount NUMERIC NOT NULL, SignedVarianceAmount NUMERIC NOT NULL, JournalEntryId INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL, ReversalOperationId TEXT NULL UNIQUE, ReversalJournalEntryId INTEGER NULL, ReversedAtUtc TEXT NULL, ReversedByUserId INTEGER NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryLandedCostOperations (Id INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL UNIQUE, RequestHash TEXT NOT NULL, PostingDate TEXT NOT NULL, CurrencyCode TEXT NOT NULL, Amount NUMERIC NOT NULL, AllocationMethod INTEGER NOT NULL, Reference TEXT NULL, JournalEntryId INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL, ReversalOperationId TEXT NULL UNIQUE, ReversalJournalEntryId INTEGER NULL, ReversedAtUtc TEXT NULL, ReversedByUserId INTEGER NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryLandedCostAllocations (Id INTEGER PRIMARY KEY AUTOINCREMENT, LandedCostOperationId INTEGER NOT NULL, LayerId INTEGER NOT NULL, Amount NUMERIC NOT NULL, UnitCostIncrease NUMERIC NOT NULL, UNIQUE(LandedCostOperationId,LayerId));",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryReconciliationRuns (Id INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL UNIQUE, AccountingBookId TEXT NOT NULL, InventoryControlAccountId TEXT NOT NULL, AsOfDate TEXT NOT NULL, ReportingCurrencyCode TEXT NOT NULL, ValuationAmount NUMERIC NOT NULL, GeneralLedgerAmount NUMERIC NOT NULL, Difference NUMERIC NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceInventoryReconciliationRuns_Date ON FinanceInventoryReconciliationRuns (AccountingBookId,AsOfDate,Id);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryReconciliationLines (Id INTEGER PRIMARY KEY AUTOINCREMENT, RunId INTEGER NOT NULL, ItemId INTEGER NOT NULL, Quantity INTEGER NOT NULL, ReportingValue NUMERIC NOT NULL, UNIQUE(RunId,ItemId));"
	];

	private static readonly string[] SqlServerStatements =
	[
		"IF OBJECT_ID(N'FinanceInventoryAccountingPolicies',N'U') IS NULL CREATE TABLE FinanceInventoryAccountingPolicies (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Version bigint NOT NULL CONSTRAINT DF_FinanceInventoryAccountingPolicies_Version DEFAULT 1, SingletonKey int NOT NULL CONSTRAINT DF_FinanceInventoryAccountingPolicies_Singleton DEFAULT 1 UNIQUE, InventoryControlAccountId nvarchar(36) NOT NULL, InventoryAdjustmentPostingProfileId bigint NOT NULL, PurchaseVariancePostingProfileId bigint NOT NULL, LandedCostPostingProfileId bigint NOT NULL, IsActive bit NOT NULL);",
		"IF OBJECT_ID(N'FinanceInventoryPurchaseVariances',N'U') IS NULL CREATE TABLE FinanceInventoryPurchaseVariances (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierDocumentId bigint NOT NULL UNIQUE, OperationId nvarchar(36) NOT NULL UNIQUE, CurrencyCode nvarchar(3) NOT NULL, ExpectedNetAmount decimal(28,9) NOT NULL, ActualNetAmount decimal(28,9) NOT NULL, SignedVarianceAmount decimal(28,9) NOT NULL, JournalEntryId bigint NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL, ReversalOperationId nvarchar(36) NULL, ReversalJournalEntryId bigint NULL, ReversedAtUtc datetime2 NULL, ReversedByUserId bigint NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_FinanceInventoryPurchaseVariances_Reversal' AND object_id=OBJECT_ID(N'FinanceInventoryPurchaseVariances')) CREATE UNIQUE INDEX UQ_FinanceInventoryPurchaseVariances_Reversal ON FinanceInventoryPurchaseVariances (ReversalOperationId) WHERE ReversalOperationId IS NOT NULL;",
		"IF OBJECT_ID(N'FinanceInventoryLandedCostOperations',N'U') IS NULL CREATE TABLE FinanceInventoryLandedCostOperations (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, OperationId nvarchar(36) NOT NULL UNIQUE, RequestHash char(64) NOT NULL, PostingDate date NOT NULL, CurrencyCode nvarchar(3) NOT NULL, Amount decimal(28,9) NOT NULL, AllocationMethod int NOT NULL, Reference nvarchar(200) NULL, JournalEntryId bigint NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL, ReversalOperationId nvarchar(36) NULL, ReversalJournalEntryId bigint NULL, ReversedAtUtc datetime2 NULL, ReversedByUserId bigint NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_FinanceInventoryLandedCostOperations_Reversal' AND object_id=OBJECT_ID(N'FinanceInventoryLandedCostOperations')) CREATE UNIQUE INDEX UQ_FinanceInventoryLandedCostOperations_Reversal ON FinanceInventoryLandedCostOperations (ReversalOperationId) WHERE ReversalOperationId IS NOT NULL;",
		"IF OBJECT_ID(N'FinanceInventoryLandedCostAllocations',N'U') IS NULL CREATE TABLE FinanceInventoryLandedCostAllocations (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, LandedCostOperationId bigint NOT NULL, LayerId bigint NOT NULL, Amount decimal(28,9) NOT NULL, UnitCostIncrease decimal(28,12) NOT NULL, CONSTRAINT UQ_FinanceInventoryLandedCostAllocations UNIQUE(LandedCostOperationId,LayerId));",
		"IF OBJECT_ID(N'FinanceInventoryReconciliationRuns',N'U') IS NULL CREATE TABLE FinanceInventoryReconciliationRuns (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, OperationId nvarchar(36) NOT NULL UNIQUE, AccountingBookId nvarchar(36) NOT NULL, InventoryControlAccountId nvarchar(36) NOT NULL, AsOfDate date NOT NULL, ReportingCurrencyCode nvarchar(3) NOT NULL, ValuationAmount decimal(28,9) NOT NULL, GeneralLedgerAmount decimal(28,9) NOT NULL, Difference decimal(28,9) NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceInventoryReconciliationRuns_Date' AND object_id=OBJECT_ID(N'FinanceInventoryReconciliationRuns')) CREATE INDEX IX_FinanceInventoryReconciliationRuns_Date ON FinanceInventoryReconciliationRuns (AccountingBookId,AsOfDate,Id);",
		"IF OBJECT_ID(N'FinanceInventoryReconciliationLines',N'U') IS NULL CREATE TABLE FinanceInventoryReconciliationLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, RunId bigint NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, ReportingValue decimal(28,9) NOT NULL, CONSTRAINT UQ_FinanceInventoryReconciliationLines UNIQUE(RunId,ItemId));"
	];

	private static readonly string[] MySqlStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceInventoryAccountingPolicies (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, SingletonKey INT NOT NULL DEFAULT 1 UNIQUE, InventoryControlAccountId VARCHAR(36) NOT NULL, InventoryAdjustmentPostingProfileId BIGINT NOT NULL, PurchaseVariancePostingProfileId BIGINT NOT NULL, LandedCostPostingProfileId BIGINT NOT NULL, IsActive BOOLEAN NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryPurchaseVariances (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, SupplierDocumentId BIGINT NOT NULL UNIQUE, OperationId VARCHAR(36) NOT NULL UNIQUE, CurrencyCode VARCHAR(3) NOT NULL, ExpectedNetAmount DECIMAL(28,9) NOT NULL, ActualNetAmount DECIMAL(28,9) NOT NULL, SignedVarianceAmount DECIMAL(28,9) NOT NULL, JournalEntryId BIGINT NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL, ReversalOperationId VARCHAR(36) NULL UNIQUE, ReversalJournalEntryId BIGINT NULL, ReversedAtUtc DATETIME(6) NULL, ReversedByUserId BIGINT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryLandedCostOperations (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, OperationId VARCHAR(36) NOT NULL UNIQUE, RequestHash CHAR(64) NOT NULL, PostingDate DATE NOT NULL, CurrencyCode VARCHAR(3) NOT NULL, Amount DECIMAL(28,9) NOT NULL, AllocationMethod INT NOT NULL, Reference VARCHAR(200) NULL, JournalEntryId BIGINT NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL, ReversalOperationId VARCHAR(36) NULL UNIQUE, ReversalJournalEntryId BIGINT NULL, ReversedAtUtc DATETIME(6) NULL, ReversedByUserId BIGINT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryLandedCostAllocations (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, LandedCostOperationId BIGINT NOT NULL, LayerId BIGINT NOT NULL, Amount DECIMAL(28,9) NOT NULL, UnitCostIncrease DECIMAL(28,12) NOT NULL, UNIQUE KEY UQ_FinanceInventoryLandedCostAllocations (LandedCostOperationId,LayerId));",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryReconciliationRuns (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, OperationId VARCHAR(36) NOT NULL UNIQUE, AccountingBookId VARCHAR(36) NOT NULL, InventoryControlAccountId VARCHAR(36) NOT NULL, AsOfDate DATE NOT NULL, ReportingCurrencyCode VARCHAR(3) NOT NULL, ValuationAmount DECIMAL(28,9) NOT NULL, GeneralLedgerAmount DECIMAL(28,9) NOT NULL, Difference DECIMAL(28,9) NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL, INDEX IX_FinanceInventoryReconciliationRuns_Date (AccountingBookId,AsOfDate,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceInventoryReconciliationLines (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, RunId BIGINT NOT NULL, ItemId BIGINT NOT NULL, Quantity INT NOT NULL, ReportingValue DECIMAL(28,9) NOT NULL, UNIQUE KEY UQ_FinanceInventoryReconciliationLines (RunId,ItemId));"
	];
}
