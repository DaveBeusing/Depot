// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class FinanceReportingSchemaInitializer
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
		DatabaseProvider.Local => LocalStatements,
		DatabaseProvider.SqlServer => SqlServerStatements,
		DatabaseProvider.MySql => MySqlStatements,
		_ => throw new NotSupportedException($"Finance Reporting schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] LocalStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceReportingAccountMappings (Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, AccountingBookId TEXT NOT NULL, AccountId TEXT NOT NULL, StatementSection INTEGER NOT NULL, CashFlowCategory INTEGER NOT NULL, TaxCategory INTEGER NOT NULL, IsCashAccount INTEGER NOT NULL, IsCostOfGoodsSold INTEGER NOT NULL, SortOrder INTEGER NOT NULL, IsActive INTEGER NOT NULL, UNIQUE(AccountingBookId,AccountId));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceReportingAccountMappings_Book ON FinanceReportingAccountMappings (AccountingBookId,IsActive,SortOrder,Id);",
		"CREATE TABLE IF NOT EXISTS FinanceReportSnapshots (Id INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL UNIQUE, Kind INTEGER NOT NULL, AccountingBookId TEXT NOT NULL, FromDate TEXT NULL, ToDate TEXT NULL, AsOfDate TEXT NULL, DimensionId TEXT NULL, DimensionValueId TEXT NULL, ParameterHash TEXT NOT NULL, ContentHash TEXT NOT NULL, ContentCsv TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceReportSnapshots_BookKind ON FinanceReportSnapshots (AccountingBookId,Kind,CreatedAtUtc,Id);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceReportSnapshots_ParameterHash ON FinanceReportSnapshots (ParameterHash,CreatedAtUtc,Id);"
	];

	private static readonly string[] SqlServerStatements =
	[
		"IF OBJECT_ID(N'FinanceReportingAccountMappings',N'U') IS NULL CREATE TABLE FinanceReportingAccountMappings (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Version bigint NOT NULL CONSTRAINT DF_FinanceReportingAccountMappings_Version DEFAULT 1, AccountingBookId nvarchar(36) NOT NULL, AccountId nvarchar(36) NOT NULL, StatementSection int NOT NULL, CashFlowCategory int NOT NULL, TaxCategory int NOT NULL, IsCashAccount bit NOT NULL, IsCostOfGoodsSold bit NOT NULL, SortOrder int NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceReportingAccountMappings UNIQUE(AccountingBookId,AccountId));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceReportingAccountMappings_Book' AND object_id=OBJECT_ID(N'FinanceReportingAccountMappings')) CREATE INDEX IX_FinanceReportingAccountMappings_Book ON FinanceReportingAccountMappings (AccountingBookId,IsActive,SortOrder,Id);",
		"IF OBJECT_ID(N'FinanceReportSnapshots',N'U') IS NULL CREATE TABLE FinanceReportSnapshots (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, OperationId nvarchar(36) NOT NULL UNIQUE, Kind int NOT NULL, AccountingBookId nvarchar(36) NOT NULL, FromDate date NULL, ToDate date NULL, AsOfDate date NULL, DimensionId nvarchar(36) NULL, DimensionValueId nvarchar(36) NULL, ParameterHash char(64) NOT NULL, ContentHash char(64) NOT NULL, ContentCsv nvarchar(max) NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceReportSnapshots_BookKind' AND object_id=OBJECT_ID(N'FinanceReportSnapshots')) CREATE INDEX IX_FinanceReportSnapshots_BookKind ON FinanceReportSnapshots (AccountingBookId,Kind,CreatedAtUtc,Id);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceReportSnapshots_ParameterHash' AND object_id=OBJECT_ID(N'FinanceReportSnapshots')) CREATE INDEX IX_FinanceReportSnapshots_ParameterHash ON FinanceReportSnapshots (ParameterHash,CreatedAtUtc,Id);"
	];

	private static readonly string[] MySqlStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceReportingAccountMappings (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, AccountingBookId VARCHAR(36) NOT NULL, AccountId VARCHAR(36) NOT NULL, StatementSection INT NOT NULL, CashFlowCategory INT NOT NULL, TaxCategory INT NOT NULL, IsCashAccount BOOLEAN NOT NULL, IsCostOfGoodsSold BOOLEAN NOT NULL, SortOrder INT NOT NULL, IsActive BOOLEAN NOT NULL, UNIQUE KEY UQ_FinanceReportingAccountMappings (AccountingBookId,AccountId), INDEX IX_FinanceReportingAccountMappings_Book (AccountingBookId,IsActive,SortOrder,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceReportSnapshots (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, OperationId VARCHAR(36) NOT NULL UNIQUE, Kind INT NOT NULL, AccountingBookId VARCHAR(36) NOT NULL, FromDate DATE NULL, ToDate DATE NULL, AsOfDate DATE NULL, DimensionId VARCHAR(36) NULL, DimensionValueId VARCHAR(36) NULL, ParameterHash CHAR(64) NOT NULL, ContentHash CHAR(64) NOT NULL, ContentCsv LONGTEXT NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL, INDEX IX_FinanceReportSnapshots_BookKind (AccountingBookId,Kind,CreatedAtUtc,Id), INDEX IX_FinanceReportSnapshots_ParameterHash (ParameterHash,CreatedAtUtc,Id));"
	];
}
