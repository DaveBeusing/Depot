// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

internal static class FinanceSepaPaymentExportSchemaInitializer
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
		_ => throw new NotSupportedException($"Finance SEPA payment-export schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] LocalStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceSepaDebtorProfiles (BankAccountId INTEGER NOT NULL PRIMARY KEY, Version INTEGER NOT NULL DEFAULT 1, Name TEXT NOT NULL, StreetName TEXT NOT NULL, BuildingNumber TEXT NULL, PostalCode TEXT NOT NULL, TownName TEXT NOT NULL, CountrySubdivision TEXT NULL, CountryCode TEXT NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceSepaCreditorProfiles (SupplierId INTEGER NOT NULL PRIMARY KEY, Version INTEGER NOT NULL DEFAULT 1, Name TEXT NOT NULL, Iban TEXT NOT NULL, Bic TEXT NULL, StreetName TEXT NOT NULL, BuildingNumber TEXT NULL, PostalCode TEXT NOT NULL, TownName TEXT NOT NULL, CountrySubdivision TEXT NULL, CountryCode TEXT NOT NULL, IsActive INTEGER NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceSepaPaymentExports (Id INTEGER PRIMARY KEY AUTOINCREMENT, PaymentRunId INTEGER NOT NULL, ExportSequence INTEGER NOT NULL, ExportKey TEXT NOT NULL UNIQUE, MessageId TEXT NOT NULL, PaymentInformationId TEXT NOT NULL, FileName TEXT NOT NULL, MessageVersion TEXT NOT NULL, SchemeProfile TEXT NOT NULL, GeneratedAtUtc TEXT NOT NULL, GeneratedByUserId INTEGER NOT NULL, TransactionCount INTEGER NOT NULL, ControlSum NUMERIC NOT NULL, BankAccountId INTEGER NOT NULL, XmlSha256 TEXT NOT NULL, XmlPayload BLOB NOT NULL, CurrentStatus INTEGER NOT NULL, SupersedesExportId INTEGER NULL, UNIQUE(PaymentRunId,ExportSequence));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceSepaPaymentExports_Run ON FinanceSepaPaymentExports (PaymentRunId,ExportSequence DESC,Id DESC);",
		"CREATE TABLE IF NOT EXISTS FinanceSepaPaymentExportStatusHistory (Id INTEGER PRIMARY KEY AUTOINCREMENT, ExportId INTEGER NOT NULL, Status INTEGER NOT NULL, RecordedAtUtc TEXT NOT NULL, RecordedByUserId INTEGER NOT NULL, ExternalReference TEXT NULL, EvidenceNote TEXT NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceSepaPaymentExportStatusHistory_Export ON FinanceSepaPaymentExportStatusHistory (ExportId,Id);"
	];

	private static readonly string[] SqlServerStatements =
	[
		"IF OBJECT_ID(N'FinanceSepaDebtorProfiles',N'U') IS NULL CREATE TABLE FinanceSepaDebtorProfiles (BankAccountId bigint NOT NULL PRIMARY KEY, Version bigint NOT NULL CONSTRAINT DF_FinanceSepaDebtorProfiles_Version DEFAULT 1, Name nvarchar(70) NOT NULL, StreetName nvarchar(70) NOT NULL, BuildingNumber nvarchar(16) NULL, PostalCode nvarchar(16) NOT NULL, TownName nvarchar(35) NOT NULL, CountrySubdivision nvarchar(35) NULL, CountryCode nvarchar(2) NOT NULL);",
		"IF OBJECT_ID(N'FinanceSepaCreditorProfiles',N'U') IS NULL CREATE TABLE FinanceSepaCreditorProfiles (SupplierId bigint NOT NULL PRIMARY KEY, Version bigint NOT NULL CONSTRAINT DF_FinanceSepaCreditorProfiles_Version DEFAULT 1, Name nvarchar(70) NOT NULL, Iban nvarchar(34) NOT NULL, Bic nvarchar(11) NULL, StreetName nvarchar(70) NOT NULL, BuildingNumber nvarchar(16) NULL, PostalCode nvarchar(16) NOT NULL, TownName nvarchar(35) NOT NULL, CountrySubdivision nvarchar(35) NULL, CountryCode nvarchar(2) NOT NULL, IsActive bit NOT NULL);",
		"IF OBJECT_ID(N'FinanceSepaPaymentExports',N'U') IS NULL CREATE TABLE FinanceSepaPaymentExports (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, PaymentRunId bigint NOT NULL, ExportSequence int NOT NULL, ExportKey char(64) NOT NULL UNIQUE, MessageId nvarchar(35) NOT NULL, PaymentInformationId nvarchar(35) NOT NULL, FileName nvarchar(180) NOT NULL, MessageVersion nvarchar(40) NOT NULL, SchemeProfile nvarchar(80) NOT NULL, GeneratedAtUtc datetime2 NOT NULL, GeneratedByUserId bigint NOT NULL, TransactionCount int NOT NULL, ControlSum decimal(28,9) NOT NULL, BankAccountId bigint NOT NULL, XmlSha256 char(64) NOT NULL, XmlPayload varbinary(max) NOT NULL, CurrentStatus int NOT NULL, SupersedesExportId bigint NULL, CONSTRAINT UQ_FinanceSepaPaymentExports_RunSequence UNIQUE(PaymentRunId,ExportSequence));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceSepaPaymentExports_Run' AND object_id=OBJECT_ID(N'FinanceSepaPaymentExports')) CREATE INDEX IX_FinanceSepaPaymentExports_Run ON FinanceSepaPaymentExports (PaymentRunId,ExportSequence DESC,Id DESC);",
		"IF OBJECT_ID(N'FinanceSepaPaymentExportStatusHistory',N'U') IS NULL CREATE TABLE FinanceSepaPaymentExportStatusHistory (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, ExportId bigint NOT NULL, Status int NOT NULL, RecordedAtUtc datetime2 NOT NULL, RecordedByUserId bigint NOT NULL, ExternalReference nvarchar(200) NULL, EvidenceNote nvarchar(500) NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceSepaPaymentExportStatusHistory_Export' AND object_id=OBJECT_ID(N'FinanceSepaPaymentExportStatusHistory')) CREATE INDEX IX_FinanceSepaPaymentExportStatusHistory_Export ON FinanceSepaPaymentExportStatusHistory (ExportId,Id);"
	];

	private static readonly string[] MySqlStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceSepaDebtorProfiles (BankAccountId BIGINT NOT NULL PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, Name VARCHAR(70) NOT NULL, StreetName VARCHAR(70) NOT NULL, BuildingNumber VARCHAR(16) NULL, PostalCode VARCHAR(16) NOT NULL, TownName VARCHAR(35) NOT NULL, CountrySubdivision VARCHAR(35) NULL, CountryCode CHAR(2) NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceSepaCreditorProfiles (SupplierId BIGINT NOT NULL PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, Name VARCHAR(70) NOT NULL, Iban VARCHAR(34) NOT NULL, Bic VARCHAR(11) NULL, StreetName VARCHAR(70) NOT NULL, BuildingNumber VARCHAR(16) NULL, PostalCode VARCHAR(16) NOT NULL, TownName VARCHAR(35) NOT NULL, CountrySubdivision VARCHAR(35) NULL, CountryCode CHAR(2) NOT NULL, IsActive BOOLEAN NOT NULL);",
		"CREATE TABLE IF NOT EXISTS FinanceSepaPaymentExports (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, PaymentRunId BIGINT NOT NULL, ExportSequence INT NOT NULL, ExportKey CHAR(64) NOT NULL UNIQUE, MessageId VARCHAR(35) NOT NULL, PaymentInformationId VARCHAR(35) NOT NULL, FileName VARCHAR(180) NOT NULL, MessageVersion VARCHAR(40) NOT NULL, SchemeProfile VARCHAR(80) NOT NULL, GeneratedAtUtc DATETIME(6) NOT NULL, GeneratedByUserId BIGINT NOT NULL, TransactionCount INT NOT NULL, ControlSum DECIMAL(28,9) NOT NULL, BankAccountId BIGINT NOT NULL, XmlSha256 CHAR(64) NOT NULL, XmlPayload LONGBLOB NOT NULL, CurrentStatus INT NOT NULL, SupersedesExportId BIGINT NULL, UNIQUE KEY UQ_FinanceSepaPaymentExports_RunSequence (PaymentRunId,ExportSequence), INDEX IX_FinanceSepaPaymentExports_Run (PaymentRunId,ExportSequence,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceSepaPaymentExportStatusHistory (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, ExportId BIGINT NOT NULL, Status INT NOT NULL, RecordedAtUtc DATETIME(6) NOT NULL, RecordedByUserId BIGINT NOT NULL, ExternalReference VARCHAR(200) NULL, EvidenceNote VARCHAR(500) NULL, INDEX IX_FinanceSepaPaymentExportStatusHistory_Export (ExportId,Id));"
	];
}
