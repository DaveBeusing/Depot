// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class FinanceFixedAssetsSchemaInitializer
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection=connectionFactory.CreateConnection();
		connection.Open();
		using var command=connection.CreateCommand();
		foreach(var statement in Statements(connectionFactory.Provider)){command.CommandText=statement;command.Parameters.Clear();command.ExecuteNonQuery();}
	}

	private static IReadOnlyList<string> Statements(DatabaseProvider provider)=>provider switch
	{
		DatabaseProvider.Local=>Local,
		DatabaseProvider.SqlServer=>SqlServer,
		DatabaseProvider.MySql=>MySql,
		_=>throw new NotSupportedException($"Finance Fixed Assets schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] Local=[
		"CREATE TABLE IF NOT EXISTS FinanceAssetClasses (Id INTEGER PRIMARY KEY AUTOINCREMENT,Version INTEGER NOT NULL DEFAULT 1,LegalEntityId TEXT NOT NULL,Code TEXT NOT NULL,Name TEXT NOT NULL,PostingProfileId INTEGER NOT NULL,DefaultUsefulLifeMonths INTEGER NOT NULL,DefaultMethod INTEGER NOT NULL,IsActive INTEGER NOT NULL,UNIQUE(LegalEntityId,Code));",
		"CREATE TABLE IF NOT EXISTS FinanceFixedAssets (Id INTEGER PRIMARY KEY AUTOINCREMENT,Version INTEGER NOT NULL DEFAULT 1,AssetNumber TEXT NOT NULL UNIQUE,LegalEntityId TEXT NOT NULL,AssetClassId INTEGER NOT NULL,Description TEXT NOT NULL,AcquisitionDate TEXT NOT NULL,CapitalizationDate TEXT NULL,DepreciationStartDate TEXT NOT NULL,CurrencyCode TEXT NOT NULL,OriginalCost NUMERIC NOT NULL,SalvageValue NUMERIC NOT NULL,UsefulLifeMonths INTEGER NOT NULL,DepreciationMethod INTEGER NOT NULL,Location TEXT NULL,Custodian TEXT NULL,Status INTEGER NOT NULL,SourceSupplierDocumentLineId INTEGER NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceFixedAssets_EntityStatus ON FinanceFixedAssets (LegalEntityId,Status,AssetNumber);",
		"CREATE TABLE IF NOT EXISTS FinanceAssetDepreciationPeriods (Id INTEGER PRIMARY KEY AUTOINCREMENT,AssetId INTEGER NOT NULL,AccountingPeriodId TEXT NOT NULL,PeriodStart TEXT NOT NULL,PeriodEnd TEXT NOT NULL,PlannedAmount NUMERIC NOT NULL,PostedAmount NUMERIC NOT NULL DEFAULT 0,JournalEntryId INTEGER NULL,OperationId TEXT NULL,UNIQUE(AssetId,AccountingPeriodId));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceAssetDepreciationPeriods_Run ON FinanceAssetDepreciationPeriods (AccountingPeriodId,JournalEntryId,AssetId);",
		"CREATE TABLE IF NOT EXISTS FinanceAssetTransactions (Id INTEGER PRIMARY KEY AUTOINCREMENT,AssetId INTEGER NOT NULL,Kind INTEGER NOT NULL,OperationId TEXT NOT NULL UNIQUE,TransactionDate TEXT NOT NULL,Amount NUMERIC NOT NULL,JournalEntryId INTEGER NULL,Reason TEXT NULL,Evidence TEXT NULL,CreatedAtUtc TEXT NOT NULL,CreatedByUserId INTEGER NOT NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceAssetTransactions_Asset ON FinanceAssetTransactions (AssetId,TransactionDate,Id);"
	];
	private static readonly string[] SqlServer=[
		"IF OBJECT_ID(N'FinanceAssetClasses',N'U') IS NULL CREATE TABLE FinanceAssetClasses (Id bigint IDENTITY(1,1) PRIMARY KEY,Version bigint NOT NULL DEFAULT 1,LegalEntityId nvarchar(36) NOT NULL,Code nvarchar(50) NOT NULL,Name nvarchar(200) NOT NULL,PostingProfileId bigint NOT NULL,DefaultUsefulLifeMonths int NOT NULL,DefaultMethod int NOT NULL,IsActive bit NOT NULL,CONSTRAINT UQ_FinanceAssetClasses UNIQUE(LegalEntityId,Code));",
		"IF OBJECT_ID(N'FinanceFixedAssets',N'U') IS NULL CREATE TABLE FinanceFixedAssets (Id bigint IDENTITY(1,1) PRIMARY KEY,Version bigint NOT NULL DEFAULT 1,AssetNumber nvarchar(50) NOT NULL UNIQUE,LegalEntityId nvarchar(36) NOT NULL,AssetClassId bigint NOT NULL,Description nvarchar(500) NOT NULL,AcquisitionDate date NOT NULL,CapitalizationDate date NULL,DepreciationStartDate date NOT NULL,CurrencyCode nvarchar(3) NOT NULL,OriginalCost decimal(28,9) NOT NULL,SalvageValue decimal(28,9) NOT NULL,UsefulLifeMonths int NOT NULL,DepreciationMethod int NOT NULL,Location nvarchar(200) NULL,Custodian nvarchar(200) NULL,Status int NOT NULL,SourceSupplierDocumentLineId bigint NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceFixedAssets_EntityStatus' AND object_id=OBJECT_ID(N'FinanceFixedAssets')) CREATE INDEX IX_FinanceFixedAssets_EntityStatus ON FinanceFixedAssets (LegalEntityId,Status,AssetNumber);",
		"IF OBJECT_ID(N'FinanceAssetDepreciationPeriods',N'U') IS NULL CREATE TABLE FinanceAssetDepreciationPeriods (Id bigint IDENTITY(1,1) PRIMARY KEY,AssetId bigint NOT NULL,AccountingPeriodId nvarchar(36) NOT NULL,PeriodStart date NOT NULL,PeriodEnd date NOT NULL,PlannedAmount decimal(28,9) NOT NULL,PostedAmount decimal(28,9) NOT NULL DEFAULT 0,JournalEntryId bigint NULL,OperationId nvarchar(36) NULL,CONSTRAINT UQ_FinanceAssetDepreciation UNIQUE(AssetId,AccountingPeriodId));",
		"IF OBJECT_ID(N'FinanceAssetTransactions',N'U') IS NULL CREATE TABLE FinanceAssetTransactions (Id bigint IDENTITY(1,1) PRIMARY KEY,AssetId bigint NOT NULL,Kind int NOT NULL,OperationId nvarchar(36) NOT NULL UNIQUE,TransactionDate date NOT NULL,Amount decimal(28,9) NOT NULL,JournalEntryId bigint NULL,Reason nvarchar(500) NULL,Evidence nvarchar(1000) NULL,CreatedAtUtc datetime2 NOT NULL,CreatedByUserId bigint NOT NULL);"
	];
	private static readonly string[] MySql=[
		"CREATE TABLE IF NOT EXISTS FinanceAssetClasses (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,Version BIGINT NOT NULL DEFAULT 1,LegalEntityId VARCHAR(36) NOT NULL,Code VARCHAR(50) NOT NULL,Name VARCHAR(200) NOT NULL,PostingProfileId BIGINT NOT NULL,DefaultUsefulLifeMonths INT NOT NULL,DefaultMethod INT NOT NULL,IsActive BOOLEAN NOT NULL,UNIQUE KEY UQ_FinanceAssetClasses (LegalEntityId,Code));",
		"CREATE TABLE IF NOT EXISTS FinanceFixedAssets (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,Version BIGINT NOT NULL DEFAULT 1,AssetNumber VARCHAR(50) NOT NULL UNIQUE,LegalEntityId VARCHAR(36) NOT NULL,AssetClassId BIGINT NOT NULL,Description VARCHAR(500) NOT NULL,AcquisitionDate DATE NOT NULL,CapitalizationDate DATE NULL,DepreciationStartDate DATE NOT NULL,CurrencyCode VARCHAR(3) NOT NULL,OriginalCost DECIMAL(28,9) NOT NULL,SalvageValue DECIMAL(28,9) NOT NULL,UsefulLifeMonths INT NOT NULL,DepreciationMethod INT NOT NULL,Location VARCHAR(200) NULL,Custodian VARCHAR(200) NULL,Status INT NOT NULL,SourceSupplierDocumentLineId BIGINT NULL,INDEX IX_FinanceFixedAssets_EntityStatus (LegalEntityId,Status,AssetNumber));",
		"CREATE TABLE IF NOT EXISTS FinanceAssetDepreciationPeriods (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,AssetId BIGINT NOT NULL,AccountingPeriodId VARCHAR(36) NOT NULL,PeriodStart DATE NOT NULL,PeriodEnd DATE NOT NULL,PlannedAmount DECIMAL(28,9) NOT NULL,PostedAmount DECIMAL(28,9) NOT NULL DEFAULT 0,JournalEntryId BIGINT NULL,OperationId VARCHAR(36) NULL,UNIQUE KEY UQ_FinanceAssetDepreciation (AssetId,AccountingPeriodId),INDEX IX_FinanceAssetDepreciationPeriods_Run (AccountingPeriodId,JournalEntryId,AssetId));",
		"CREATE TABLE IF NOT EXISTS FinanceAssetTransactions (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,AssetId BIGINT NOT NULL,Kind INT NOT NULL,OperationId VARCHAR(36) NOT NULL UNIQUE,TransactionDate DATE NOT NULL,Amount DECIMAL(28,9) NOT NULL,JournalEntryId BIGINT NULL,Reason VARCHAR(500) NULL,Evidence VARCHAR(1000) NULL,CreatedAtUtc DATETIME(6) NOT NULL,CreatedByUserId BIGINT NOT NULL,INDEX IX_FinanceAssetTransactions_Asset (AssetId,TransactionDate,Id));"
	];
}
