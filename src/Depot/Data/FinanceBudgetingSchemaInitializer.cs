// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class FinanceBudgetingSchemaInitializer
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
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
		DatabaseProvider.Local => Local,
		DatabaseProvider.SqlServer => SqlServer,
		DatabaseProvider.MySql => MySql,
		_ => throw new NotSupportedException($"Finance Budgeting schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] Local =
	[
		"CREATE TABLE IF NOT EXISTS FinanceBudgetVersions (Id INTEGER PRIMARY KEY AUTOINCREMENT,Version INTEGER NOT NULL DEFAULT 1,LegalEntityId TEXT NOT NULL,AccountingBookId TEXT NOT NULL,FiscalCalendarId TEXT NOT NULL,FiscalYear INTEGER NOT NULL,BudgetName TEXT NOT NULL,BudgetVersionNumber INTEGER NOT NULL,CurrencyCode TEXT NOT NULL,Status INTEGER NOT NULL,OwnerUserId INTEGER NOT NULL,Description TEXT NULL,SourceKind INTEGER NOT NULL,SourceBudgetVersionId INTEGER NULL,ApprovalInstanceId TEXT NULL,CreatedAtUtc TEXT NOT NULL,CreatedByUserId INTEGER NOT NULL,UpdatedAtUtc TEXT NOT NULL,UpdatedByUserId INTEGER NOT NULL,UNIQUE(LegalEntityId,AccountingBookId,FiscalYear,BudgetName,BudgetVersionNumber));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceBudgetVersions_List ON FinanceBudgetVersions (LegalEntityId,AccountingBookId,FiscalYear,Status,BudgetName,BudgetVersionNumber);",
		"CREATE TABLE IF NOT EXISTS FinanceBudgetLines (Id INTEGER PRIMARY KEY AUTOINCREMENT,Version INTEGER NOT NULL DEFAULT 1,BudgetVersionId INTEGER NOT NULL,AccountId TEXT NOT NULL,AccountingPeriodId TEXT NOT NULL,DimensionId TEXT NOT NULL DEFAULT '',DimensionValueId TEXT NOT NULL DEFAULT '',Amount NUMERIC NOT NULL,SourceEvidence TEXT NULL,UNIQUE(BudgetVersionId,AccountId,AccountingPeriodId,DimensionId,DimensionValueId));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceBudgetLines_Aggregate ON FinanceBudgetLines (BudgetVersionId,AccountingPeriodId,AccountId,DimensionId,DimensionValueId);"
	];

	private static readonly string[] SqlServer =
	[
		"IF OBJECT_ID(N'FinanceBudgetVersions',N'U') IS NULL CREATE TABLE FinanceBudgetVersions (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,Version bigint NOT NULL CONSTRAINT DF_FinanceBudgetVersions_Version DEFAULT 1,LegalEntityId nvarchar(36) NOT NULL,AccountingBookId nvarchar(36) NOT NULL,FiscalCalendarId nvarchar(36) NOT NULL,FiscalYear int NOT NULL,BudgetName nvarchar(200) NOT NULL,BudgetVersionNumber int NOT NULL,CurrencyCode nvarchar(3) NOT NULL,Status int NOT NULL,OwnerUserId bigint NOT NULL,Description nvarchar(1000) NULL,SourceKind int NOT NULL,SourceBudgetVersionId bigint NULL,ApprovalInstanceId nvarchar(36) NULL,CreatedAtUtc datetime2 NOT NULL,CreatedByUserId bigint NOT NULL,UpdatedAtUtc datetime2 NOT NULL,UpdatedByUserId bigint NOT NULL,CONSTRAINT UQ_FinanceBudgetVersions UNIQUE(LegalEntityId,AccountingBookId,FiscalYear,BudgetName,BudgetVersionNumber));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceBudgetVersions_List' AND object_id=OBJECT_ID(N'FinanceBudgetVersions')) CREATE INDEX IX_FinanceBudgetVersions_List ON FinanceBudgetVersions (LegalEntityId,AccountingBookId,FiscalYear,Status,BudgetName,BudgetVersionNumber);",
		"IF OBJECT_ID(N'FinanceBudgetLines',N'U') IS NULL CREATE TABLE FinanceBudgetLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,Version bigint NOT NULL CONSTRAINT DF_FinanceBudgetLines_Version DEFAULT 1,BudgetVersionId bigint NOT NULL,AccountId nvarchar(36) NOT NULL,AccountingPeriodId nvarchar(36) NOT NULL,DimensionId nvarchar(36) NOT NULL CONSTRAINT DF_FinanceBudgetLines_DimensionId DEFAULT '',DimensionValueId nvarchar(36) NOT NULL CONSTRAINT DF_FinanceBudgetLines_DimensionValueId DEFAULT '',Amount decimal(28,9) NOT NULL,SourceEvidence nvarchar(500) NULL,CONSTRAINT UQ_FinanceBudgetLines UNIQUE(BudgetVersionId,AccountId,AccountingPeriodId,DimensionId,DimensionValueId));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceBudgetLines_Aggregate' AND object_id=OBJECT_ID(N'FinanceBudgetLines')) CREATE INDEX IX_FinanceBudgetLines_Aggregate ON FinanceBudgetLines (BudgetVersionId,AccountingPeriodId,AccountId,DimensionId,DimensionValueId);"
	];

	private static readonly string[] MySql =
	[
		"CREATE TABLE IF NOT EXISTS FinanceBudgetVersions (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,Version BIGINT NOT NULL DEFAULT 1,LegalEntityId VARCHAR(36) NOT NULL,AccountingBookId VARCHAR(36) NOT NULL,FiscalCalendarId VARCHAR(36) NOT NULL,FiscalYear INT NOT NULL,BudgetName VARCHAR(200) NOT NULL,BudgetVersionNumber INT NOT NULL,CurrencyCode VARCHAR(3) NOT NULL,Status INT NOT NULL,OwnerUserId BIGINT NOT NULL,Description VARCHAR(1000) NULL,SourceKind INT NOT NULL,SourceBudgetVersionId BIGINT NULL,ApprovalInstanceId VARCHAR(36) NULL,CreatedAtUtc DATETIME(6) NOT NULL,CreatedByUserId BIGINT NOT NULL,UpdatedAtUtc DATETIME(6) NOT NULL,UpdatedByUserId BIGINT NOT NULL,UNIQUE KEY UQ_FinanceBudgetVersions (LegalEntityId,AccountingBookId,FiscalYear,BudgetName,BudgetVersionNumber),INDEX IX_FinanceBudgetVersions_List (LegalEntityId,AccountingBookId,FiscalYear,Status,BudgetName,BudgetVersionNumber));",
		"CREATE TABLE IF NOT EXISTS FinanceBudgetLines (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,Version BIGINT NOT NULL DEFAULT 1,BudgetVersionId BIGINT NOT NULL,AccountId VARCHAR(36) NOT NULL,AccountingPeriodId VARCHAR(36) NOT NULL,DimensionId VARCHAR(36) NOT NULL DEFAULT '',DimensionValueId VARCHAR(36) NOT NULL DEFAULT '',Amount DECIMAL(28,9) NOT NULL,SourceEvidence VARCHAR(500) NULL,UNIQUE KEY UQ_FinanceBudgetLines (BudgetVersionId,AccountId,AccountingPeriodId,DimensionId,DimensionValueId),INDEX IX_FinanceBudgetLines_Aggregate (BudgetVersionId,AccountingPeriodId,AccountId,DimensionId,DimensionValueId));"
	];
}
