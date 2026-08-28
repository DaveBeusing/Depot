// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Models;

namespace Depot.Data;

public static class FinanceSchemaMigration
{
	public const int CurrentVersion = 1;
	private const string FeatureName = "Finance";

	public static void Migrate(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		EnsureVersionTable(connectionFactory);
		var version = ReadVersion(connectionFactory);
		if (version > CurrentVersion) throw new InvalidOperationException($"Finance schema version '{version}' is newer than the supported version '{CurrentVersion}'.");
		if (version == 0)
		{
			FinanceSchemaInitializer.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 1);
			version = 1;
		}
		if (version != CurrentVersion) throw new InvalidOperationException($"Finance schema version '{version}' is not supported. Expected '{CurrentVersion}'.");
	}

	private static void EnsureVersionTable(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS DepotFeatureVersions (Name TEXT PRIMARY KEY, Version INTEGER NOT NULL);",
			DatabaseProvider.SqlServer => "IF OBJECT_ID(N'DepotFeatureVersions', N'U') IS NULL CREATE TABLE DepotFeatureVersions (Name nvarchar(100) NOT NULL PRIMARY KEY, Version int NOT NULL);",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS DepotFeatureVersions (Name VARCHAR(100) NOT NULL PRIMARY KEY, Version INT NOT NULL);",
			_ => throw new NotSupportedException($"Finance migrations are not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private static int ReadVersion(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT Version FROM DepotFeatureVersions WHERE Name='{FeatureName}';";
		var value = command.ExecuteScalar();
		return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	private static void WriteVersion(IDatabaseConnectionFactory connectionFactory, int version)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON CONFLICT(Name) DO UPDATE SET Version=excluded.Version;",
			DatabaseProvider.SqlServer => $"IF EXISTS (SELECT 1 FROM DepotFeatureVersions WHERE Name=N'{FeatureName}') UPDATE DepotFeatureVersions SET Version={version} WHERE Name=N'{FeatureName}'; ELSE INSERT INTO DepotFeatureVersions (Name, Version) VALUES (N'{FeatureName}', {version});",
			DatabaseProvider.MySql => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON DUPLICATE KEY UPDATE Version=VALUES(Version);",
			_ => throw new NotSupportedException($"Finance migrations are not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}

internal static class FinanceSchemaInitializer
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
		_ => throw new NotSupportedException($"Finance schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] LocalStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceCurrencies (Code TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, MinorUnits INTEGER NOT NULL, IsActive INTEGER NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceLegalEntities (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, CountryCode TEXT NOT NULL, FunctionalCurrencyCode TEXT NOT NULL, IsActive INTEGER NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceTaxRegistrations (Id TEXT NOT NULL PRIMARY KEY, LegalEntityId TEXT NOT NULL, CountryCode TEXT NOT NULL, SchemeCode TEXT NOT NULL, RegistrationNumber TEXT NOT NULL, ValidFrom TEXT NULL, ValidTo TEXT NULL, UNIQUE(LegalEntityId, CountryCode, SchemeCode, RegistrationNumber));",
		"CREATE TABLE IF NOT EXISTS FinanceExchangeRates (Id TEXT NOT NULL PRIMARY KEY, BaseCurrencyCode TEXT NOT NULL, QuoteCurrencyCode TEXT NOT NULL, Rate NUMERIC NOT NULL, EffectiveAtUtc TEXT NOT NULL, SourceCode TEXT NOT NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceExchangeRates_Pair_Effective ON FinanceExchangeRates (BaseCurrencyCode, QuoteCurrencyCode, EffectiveAtUtc);",
		"CREATE TABLE IF NOT EXISTS FinanceFiscalCalendars (Id TEXT NOT NULL PRIMARY KEY, LegalEntityId TEXT NOT NULL, Code TEXT NOT NULL, Name TEXT NOT NULL, IsActive INTEGER NOT NULL, UNIQUE(LegalEntityId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceAccountingPeriods (Id TEXT NOT NULL PRIMARY KEY, FiscalCalendarId TEXT NOT NULL, Code TEXT NOT NULL, StartDate TEXT NOT NULL, EndDate TEXT NOT NULL, Status INTEGER NOT NULL, UNIQUE(FiscalCalendarId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceChartsOfAccounts (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, IsActive INTEGER NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceAccounts (Id TEXT NOT NULL PRIMARY KEY, ChartOfAccountsId TEXT NOT NULL, Number TEXT NOT NULL, Name TEXT NOT NULL, AccountType INTEGER NOT NULL, AllowDirectPosting INTEGER NOT NULL, IsActive INTEGER NOT NULL, UNIQUE(ChartOfAccountsId, Number));",
		"CREATE TABLE IF NOT EXISTS FinanceAccountingBooks (Id TEXT NOT NULL PRIMARY KEY, LegalEntityId TEXT NOT NULL, ChartOfAccountsId TEXT NOT NULL, Code TEXT NOT NULL, Name TEXT NOT NULL, ReportingCurrencyCode TEXT NOT NULL, AccountingStandardCode TEXT NOT NULL, IsPrimary INTEGER NOT NULL, IsActive INTEGER NOT NULL, UNIQUE(LegalEntityId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceJournals (Id TEXT NOT NULL PRIMARY KEY, AccountingBookId TEXT NOT NULL, Code TEXT NOT NULL, Name TEXT NOT NULL, IsActive INTEGER NOT NULL, UNIQUE(AccountingBookId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceDimensions (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, IsRequired INTEGER NOT NULL, IsActive INTEGER NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceDimensionValues (Id TEXT NOT NULL PRIMARY KEY, DimensionId TEXT NOT NULL, Code TEXT NOT NULL, Name TEXT NOT NULL, IsActive INTEGER NOT NULL, UNIQUE(DimensionId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceNumberSequences (Id TEXT NOT NULL PRIMARY KEY, LegalEntityId TEXT NOT NULL, Code TEXT NOT NULL, DocumentType TEXT NOT NULL, Prefix TEXT NOT NULL, NumericLength INTEGER NOT NULL, NextNumber INTEGER NOT NULL, IsActive INTEGER NOT NULL, UNIQUE(LegalEntityId, Code));"
	];

	private static readonly string[] SqlServerStatements =
	[
		"IF OBJECT_ID(N'FinanceCurrencies', N'U') IS NULL CREATE TABLE FinanceCurrencies (Code nvarchar(3) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL, MinorUnits int NOT NULL, IsActive bit NOT NULL);",
		"IF OBJECT_ID(N'FinanceLegalEntities', N'U') IS NULL CREATE TABLE FinanceLegalEntities (Id nvarchar(36) NOT NULL PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE, Name nvarchar(250) NOT NULL, CountryCode nvarchar(2) NOT NULL, FunctionalCurrencyCode nvarchar(3) NOT NULL, IsActive bit NOT NULL);",
		"IF OBJECT_ID(N'FinanceTaxRegistrations', N'U') IS NULL CREATE TABLE FinanceTaxRegistrations (Id nvarchar(36) NOT NULL PRIMARY KEY, LegalEntityId nvarchar(36) NOT NULL, CountryCode nvarchar(2) NOT NULL, SchemeCode nvarchar(50) NOT NULL, RegistrationNumber nvarchar(100) NOT NULL, ValidFrom date NULL, ValidTo date NULL, CONSTRAINT UQ_FinanceTaxRegistrations UNIQUE (LegalEntityId, CountryCode, SchemeCode, RegistrationNumber));",
		"IF OBJECT_ID(N'FinanceExchangeRates', N'U') IS NULL CREATE TABLE FinanceExchangeRates (Id nvarchar(36) NOT NULL PRIMARY KEY, BaseCurrencyCode nvarchar(3) NOT NULL, QuoteCurrencyCode nvarchar(3) NOT NULL, Rate decimal(28,12) NOT NULL, EffectiveAtUtc datetimeoffset NOT NULL, SourceCode nvarchar(100) NOT NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceExchangeRates_Pair_Effective' AND object_id=OBJECT_ID(N'FinanceExchangeRates')) CREATE INDEX IX_FinanceExchangeRates_Pair_Effective ON FinanceExchangeRates (BaseCurrencyCode, QuoteCurrencyCode, EffectiveAtUtc);",
		"IF OBJECT_ID(N'FinanceFiscalCalendars', N'U') IS NULL CREATE TABLE FinanceFiscalCalendars (Id nvarchar(36) NOT NULL PRIMARY KEY, LegalEntityId nvarchar(36) NOT NULL, Code nvarchar(50) NOT NULL, Name nvarchar(200) NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceFiscalCalendars UNIQUE (LegalEntityId, Code));",
		"IF OBJECT_ID(N'FinanceAccountingPeriods', N'U') IS NULL CREATE TABLE FinanceAccountingPeriods (Id nvarchar(36) NOT NULL PRIMARY KEY, FiscalCalendarId nvarchar(36) NOT NULL, Code nvarchar(50) NOT NULL, StartDate date NOT NULL, EndDate date NOT NULL, Status int NOT NULL, CONSTRAINT UQ_FinanceAccountingPeriods UNIQUE (FiscalCalendarId, Code));",
		"IF OBJECT_ID(N'FinanceChartsOfAccounts', N'U') IS NULL CREATE TABLE FinanceChartsOfAccounts (Id nvarchar(36) NOT NULL PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE, Name nvarchar(200) NOT NULL, IsActive bit NOT NULL);",
		"IF OBJECT_ID(N'FinanceAccounts', N'U') IS NULL CREATE TABLE FinanceAccounts (Id nvarchar(36) NOT NULL PRIMARY KEY, ChartOfAccountsId nvarchar(36) NOT NULL, Number nvarchar(50) NOT NULL, Name nvarchar(200) NOT NULL, AccountType int NOT NULL, AllowDirectPosting bit NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceAccounts UNIQUE (ChartOfAccountsId, Number));",
		"IF OBJECT_ID(N'FinanceAccountingBooks', N'U') IS NULL CREATE TABLE FinanceAccountingBooks (Id nvarchar(36) NOT NULL PRIMARY KEY, LegalEntityId nvarchar(36) NOT NULL, ChartOfAccountsId nvarchar(36) NOT NULL, Code nvarchar(50) NOT NULL, Name nvarchar(200) NOT NULL, ReportingCurrencyCode nvarchar(3) NOT NULL, AccountingStandardCode nvarchar(100) NOT NULL, IsPrimary bit NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceAccountingBooks UNIQUE (LegalEntityId, Code));",
		"IF OBJECT_ID(N'FinanceJournals', N'U') IS NULL CREATE TABLE FinanceJournals (Id nvarchar(36) NOT NULL PRIMARY KEY, AccountingBookId nvarchar(36) NOT NULL, Code nvarchar(50) NOT NULL, Name nvarchar(200) NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceJournals UNIQUE (AccountingBookId, Code));",
		"IF OBJECT_ID(N'FinanceDimensions', N'U') IS NULL CREATE TABLE FinanceDimensions (Id nvarchar(36) NOT NULL PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE, Name nvarchar(200) NOT NULL, IsRequired bit NOT NULL, IsActive bit NOT NULL);",
		"IF OBJECT_ID(N'FinanceDimensionValues', N'U') IS NULL CREATE TABLE FinanceDimensionValues (Id nvarchar(36) NOT NULL PRIMARY KEY, DimensionId nvarchar(36) NOT NULL, Code nvarchar(100) NOT NULL, Name nvarchar(200) NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceDimensionValues UNIQUE (DimensionId, Code));",
		"IF OBJECT_ID(N'FinanceNumberSequences', N'U') IS NULL CREATE TABLE FinanceNumberSequences (Id nvarchar(36) NOT NULL PRIMARY KEY, LegalEntityId nvarchar(36) NOT NULL, Code nvarchar(50) NOT NULL, DocumentType nvarchar(100) NOT NULL, Prefix nvarchar(50) NOT NULL, NumericLength int NOT NULL, NextNumber bigint NOT NULL, IsActive bit NOT NULL, CONSTRAINT UQ_FinanceNumberSequences UNIQUE (LegalEntityId, Code));"
	];

	private static readonly string[] MySqlStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceCurrencies (Code VARCHAR(3) NOT NULL PRIMARY KEY, Name VARCHAR(200) NOT NULL, MinorUnits INT NOT NULL, IsActive BOOLEAN NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceLegalEntities (Id VARCHAR(36) NOT NULL PRIMARY KEY, Code VARCHAR(50) NOT NULL UNIQUE, Name VARCHAR(250) NOT NULL, CountryCode VARCHAR(2) NOT NULL, FunctionalCurrencyCode VARCHAR(3) NOT NULL, IsActive BOOLEAN NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceTaxRegistrations (Id VARCHAR(36) NOT NULL PRIMARY KEY, LegalEntityId VARCHAR(36) NOT NULL, CountryCode VARCHAR(2) NOT NULL, SchemeCode VARCHAR(50) NOT NULL, RegistrationNumber VARCHAR(100) NOT NULL, ValidFrom DATE NULL, ValidTo DATE NULL, UNIQUE KEY UQ_FinanceTaxRegistrations (LegalEntityId, CountryCode, SchemeCode, RegistrationNumber));",
		"CREATE TABLE IF NOT EXISTS FinanceExchangeRates (Id VARCHAR(36) NOT NULL PRIMARY KEY, BaseCurrencyCode VARCHAR(3) NOT NULL, QuoteCurrencyCode VARCHAR(3) NOT NULL, Rate DECIMAL(28,12) NOT NULL, EffectiveAtUtc DATETIME(6) NOT NULL, SourceCode VARCHAR(100) NOT NULL, INDEX IX_FinanceExchangeRates_Pair_Effective (BaseCurrencyCode, QuoteCurrencyCode, EffectiveAtUtc));",
		"CREATE TABLE IF NOT EXISTS FinanceFiscalCalendars (Id VARCHAR(36) NOT NULL PRIMARY KEY, LegalEntityId VARCHAR(36) NOT NULL, Code VARCHAR(50) NOT NULL, Name VARCHAR(200) NOT NULL, IsActive BOOLEAN NOT NULL, UNIQUE KEY UQ_FinanceFiscalCalendars (LegalEntityId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceAccountingPeriods (Id VARCHAR(36) NOT NULL PRIMARY KEY, FiscalCalendarId VARCHAR(36) NOT NULL, Code VARCHAR(50) NOT NULL, StartDate DATE NOT NULL, EndDate DATE NOT NULL, Status INT NOT NULL, UNIQUE KEY UQ_FinanceAccountingPeriods (FiscalCalendarId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceChartsOfAccounts (Id VARCHAR(36) NOT NULL PRIMARY KEY, Code VARCHAR(50) NOT NULL UNIQUE, Name VARCHAR(200) NOT NULL, IsActive BOOLEAN NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceAccounts (Id VARCHAR(36) NOT NULL PRIMARY KEY, ChartOfAccountsId VARCHAR(36) NOT NULL, Number VARCHAR(50) NOT NULL, Name VARCHAR(200) NOT NULL, AccountType INT NOT NULL, AllowDirectPosting BOOLEAN NOT NULL, IsActive BOOLEAN NOT NULL, UNIQUE KEY UQ_FinanceAccounts (ChartOfAccountsId, Number));",
		"CREATE TABLE IF NOT EXISTS FinanceAccountingBooks (Id VARCHAR(36) NOT NULL PRIMARY KEY, LegalEntityId VARCHAR(36) NOT NULL, ChartOfAccountsId VARCHAR(36) NOT NULL, Code VARCHAR(50) NOT NULL, Name VARCHAR(200) NOT NULL, ReportingCurrencyCode VARCHAR(3) NOT NULL, AccountingStandardCode VARCHAR(100) NOT NULL, IsPrimary BOOLEAN NOT NULL, IsActive BOOLEAN NOT NULL, UNIQUE KEY UQ_FinanceAccountingBooks (LegalEntityId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceJournals (Id VARCHAR(36) NOT NULL PRIMARY KEY, AccountingBookId VARCHAR(36) NOT NULL, Code VARCHAR(50) NOT NULL, Name VARCHAR(200) NOT NULL, IsActive BOOLEAN NOT NULL, UNIQUE KEY UQ_FinanceJournals (AccountingBookId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceDimensions (Id VARCHAR(36) NOT NULL PRIMARY KEY, Code VARCHAR(50) NOT NULL UNIQUE, Name VARCHAR(200) NOT NULL, IsRequired BOOLEAN NOT NULL, IsActive BOOLEAN NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceDimensionValues (Id VARCHAR(36) NOT NULL PRIMARY KEY, DimensionId VARCHAR(36) NOT NULL, Code VARCHAR(100) NOT NULL, Name VARCHAR(200) NOT NULL, IsActive BOOLEAN NOT NULL, UNIQUE KEY UQ_FinanceDimensionValues (DimensionId, Code));",
		"CREATE TABLE IF NOT EXISTS FinanceNumberSequences (Id VARCHAR(36) NOT NULL PRIMARY KEY, LegalEntityId VARCHAR(36) NOT NULL, Code VARCHAR(50) NOT NULL, DocumentType VARCHAR(100) NOT NULL, Prefix VARCHAR(50) NOT NULL, NumericLength INT NOT NULL, NextNumber BIGINT NOT NULL, IsActive BOOLEAN NOT NULL, UNIQUE KEY UQ_FinanceNumberSequences (LegalEntityId, Code));"
	];
}
