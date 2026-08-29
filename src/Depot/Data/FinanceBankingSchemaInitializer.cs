// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class FinanceBankingSchemaInitializer
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
		_ => throw new NotSupportedException($"Finance Banking schema initialization is not supported for provider '{provider}'.")
	};

	private static readonly string[] LocalStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceBankAccounts (Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, LegalEntityId TEXT NOT NULL, AccountingBookId TEXT NOT NULL, GeneralLedgerAccountId TEXT NOT NULL, CurrencyCode TEXT NOT NULL, Name TEXT NOT NULL, BankName TEXT NULL, Iban TEXT NULL, Bic TEXT NULL, LocalAccountNumber TEXT NULL, IsActive INTEGER NOT NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceBankAccounts_Entity ON FinanceBankAccounts (LegalEntityId,IsActive,Name,Id);",
		"CREATE TABLE IF NOT EXISTS FinanceBankStatements (Id INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL UNIQUE, BankAccountId INTEGER NOT NULL, Format INTEGER NOT NULL, StatementReference TEXT NOT NULL, ImportHash TEXT NOT NULL UNIQUE, SourceFileName TEXT NULL, CurrencyCode TEXT NOT NULL, FromDate TEXT NOT NULL, ToDate TEXT NOT NULL, OpeningBalance NUMERIC NOT NULL, ClosingBalance NUMERIC NOT NULL, ImportedAtUtc TEXT NOT NULL, ImportedByUserId INTEGER NOT NULL, UNIQUE(BankAccountId,StatementReference));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceBankStatements_AccountDate ON FinanceBankStatements (BankAccountId,ToDate,Id);",
		"CREATE TABLE IF NOT EXISTS FinanceBankStatementLines (Id INTEGER PRIMARY KEY AUTOINCREMENT, StatementId INTEGER NOT NULL, LineNumber INTEGER NOT NULL, BookingDate TEXT NOT NULL, ValueDate TEXT NULL, Amount NUMERIC NOT NULL, CurrencyCode TEXT NOT NULL, ExternalId TEXT NULL, Reference TEXT NULL, CounterpartyName TEXT NULL, BankTransactionCode TEXT NULL, UNIQUE(StatementId,LineNumber));",
		"CREATE INDEX IF NOT EXISTS IX_FinanceBankStatementLines_Date ON FinanceBankStatementLines (StatementId,BookingDate,Id);",
		"CREATE TABLE IF NOT EXISTS FinanceBankReconciliations (Id INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL UNIQUE, StatementLineId INTEGER NOT NULL, TargetKind INTEGER NOT NULL, TargetId INTEGER NOT NULL, TargetJournalEntryId INTEGER NOT NULL, MatchedAmount NUMERIC NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL, ReversalOperationId TEXT NULL UNIQUE, ReversedAtUtc TEXT NULL, ReversedByUserId INTEGER NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinanceBankReconciliations_Line ON FinanceBankReconciliations (StatementLineId,ReversedAtUtc,Id);",
		"CREATE TABLE IF NOT EXISTS FinancePaymentRuns (Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, OperationId TEXT NOT NULL UNIQUE, BankAccountId INTEGER NOT NULL, PaymentDate TEXT NOT NULL, CurrencyCode TEXT NOT NULL, Description TEXT NOT NULL, Status INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL, ApprovedAtUtc TEXT NULL, ApprovedByUserId INTEGER NULL, ApprovalComment TEXT NULL, CompletedAtUtc TEXT NULL);",
		"CREATE INDEX IF NOT EXISTS IX_FinancePaymentRuns_Date ON FinancePaymentRuns (BankAccountId,PaymentDate,Status,Id);",
		"CREATE TABLE IF NOT EXISTS FinancePaymentRunLines (Id INTEGER PRIMARY KEY AUTOINCREMENT, PaymentRunId INTEGER NOT NULL, PayableOpenItemId INTEGER NOT NULL, SupplierId INTEGER NOT NULL, Amount NUMERIC NOT NULL, Reference TEXT NULL, Status INTEGER NOT NULL, ExecutionOperationId TEXT NOT NULL UNIQUE, PayablePaymentId INTEGER NULL, ExecutedAtUtc TEXT NULL, ExecutedByUserId INTEGER NULL, ExecutionReference TEXT NULL, UNIQUE(PaymentRunId,PayableOpenItemId));"
	];

	private static readonly string[] SqlServerStatements =
	[
		"IF OBJECT_ID(N'FinanceBankAccounts',N'U') IS NULL CREATE TABLE FinanceBankAccounts (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Version bigint NOT NULL CONSTRAINT DF_FinanceBankAccounts_Version DEFAULT 1, LegalEntityId nvarchar(36) NOT NULL, AccountingBookId nvarchar(36) NOT NULL, GeneralLedgerAccountId nvarchar(36) NOT NULL, CurrencyCode nvarchar(3) NOT NULL, Name nvarchar(200) NOT NULL, BankName nvarchar(200) NULL, Iban nvarchar(64) NULL, Bic nvarchar(32) NULL, LocalAccountNumber nvarchar(100) NULL, IsActive bit NOT NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceBankAccounts_Entity' AND object_id=OBJECT_ID(N'FinanceBankAccounts')) CREATE INDEX IX_FinanceBankAccounts_Entity ON FinanceBankAccounts (LegalEntityId,IsActive,Name,Id);",
		"IF OBJECT_ID(N'FinanceBankStatements',N'U') IS NULL CREATE TABLE FinanceBankStatements (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, OperationId nvarchar(36) NOT NULL UNIQUE, BankAccountId bigint NOT NULL, Format int NOT NULL, StatementReference nvarchar(200) NOT NULL, ImportHash char(64) NOT NULL UNIQUE, SourceFileName nvarchar(260) NULL, CurrencyCode nvarchar(3) NOT NULL, FromDate date NOT NULL, ToDate date NOT NULL, OpeningBalance decimal(28,9) NOT NULL, ClosingBalance decimal(28,9) NOT NULL, ImportedAtUtc datetime2 NOT NULL, ImportedByUserId bigint NOT NULL, CONSTRAINT UQ_FinanceBankStatements_Reference UNIQUE(BankAccountId,StatementReference));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceBankStatements_AccountDate' AND object_id=OBJECT_ID(N'FinanceBankStatements')) CREATE INDEX IX_FinanceBankStatements_AccountDate ON FinanceBankStatements (BankAccountId,ToDate,Id);",
		"IF OBJECT_ID(N'FinanceBankStatementLines',N'U') IS NULL CREATE TABLE FinanceBankStatementLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, StatementId bigint NOT NULL, LineNumber int NOT NULL, BookingDate date NOT NULL, ValueDate date NULL, Amount decimal(28,9) NOT NULL, CurrencyCode nvarchar(3) NOT NULL, ExternalId nvarchar(200) NULL, Reference nvarchar(500) NULL, CounterpartyName nvarchar(300) NULL, BankTransactionCode nvarchar(100) NULL, CONSTRAINT UQ_FinanceBankStatementLines UNIQUE(StatementId,LineNumber));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceBankStatementLines_Date' AND object_id=OBJECT_ID(N'FinanceBankStatementLines')) CREATE INDEX IX_FinanceBankStatementLines_Date ON FinanceBankStatementLines (StatementId,BookingDate,Id);",
		"IF OBJECT_ID(N'FinanceBankReconciliations',N'U') IS NULL CREATE TABLE FinanceBankReconciliations (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, OperationId nvarchar(36) NOT NULL UNIQUE, StatementLineId bigint NOT NULL, TargetKind int NOT NULL, TargetId bigint NOT NULL, TargetJournalEntryId bigint NOT NULL, MatchedAmount decimal(28,9) NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL, ReversalOperationId nvarchar(36) NULL, ReversedAtUtc datetime2 NULL, ReversedByUserId bigint NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_FinanceBankReconciliations_Reversal' AND object_id=OBJECT_ID(N'FinanceBankReconciliations')) CREATE UNIQUE INDEX UQ_FinanceBankReconciliations_Reversal ON FinanceBankReconciliations (ReversalOperationId) WHERE ReversalOperationId IS NOT NULL;",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinanceBankReconciliations_Line' AND object_id=OBJECT_ID(N'FinanceBankReconciliations')) CREATE INDEX IX_FinanceBankReconciliations_Line ON FinanceBankReconciliations (StatementLineId,ReversedAtUtc,Id);",
		"IF OBJECT_ID(N'FinancePaymentRuns',N'U') IS NULL CREATE TABLE FinancePaymentRuns (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Version bigint NOT NULL CONSTRAINT DF_FinancePaymentRuns_Version DEFAULT 1, OperationId nvarchar(36) NOT NULL UNIQUE, BankAccountId bigint NOT NULL, PaymentDate date NOT NULL, CurrencyCode nvarchar(3) NOT NULL, Description nvarchar(500) NOT NULL, Status int NOT NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL, ApprovedAtUtc datetime2 NULL, ApprovedByUserId bigint NULL, ApprovalComment nvarchar(500) NULL, CompletedAtUtc datetime2 NULL);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_FinancePaymentRuns_Date' AND object_id=OBJECT_ID(N'FinancePaymentRuns')) CREATE INDEX IX_FinancePaymentRuns_Date ON FinancePaymentRuns (BankAccountId,PaymentDate,Status,Id);",
		"IF OBJECT_ID(N'FinancePaymentRunLines',N'U') IS NULL CREATE TABLE FinancePaymentRunLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, PaymentRunId bigint NOT NULL, PayableOpenItemId bigint NOT NULL, SupplierId bigint NOT NULL, Amount decimal(28,9) NOT NULL, Reference nvarchar(200) NULL, Status int NOT NULL, ExecutionOperationId nvarchar(36) NOT NULL UNIQUE, PayablePaymentId bigint NULL, ExecutedAtUtc datetime2 NULL, ExecutedByUserId bigint NULL, ExecutionReference nvarchar(200) NULL, CONSTRAINT UQ_FinancePaymentRunLines_OpenItem UNIQUE(PaymentRunId,PayableOpenItemId));"
	];

	private static readonly string[] MySqlStatements =
	[
		"CREATE TABLE IF NOT EXISTS FinanceBankAccounts (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, LegalEntityId VARCHAR(36) NOT NULL, AccountingBookId VARCHAR(36) NOT NULL, GeneralLedgerAccountId VARCHAR(36) NOT NULL, CurrencyCode VARCHAR(3) NOT NULL, Name VARCHAR(200) NOT NULL, BankName VARCHAR(200) NULL, Iban VARCHAR(64) NULL, Bic VARCHAR(32) NULL, LocalAccountNumber VARCHAR(100) NULL, IsActive BOOLEAN NOT NULL, INDEX IX_FinanceBankAccounts_Entity (LegalEntityId,IsActive,Name,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceBankStatements (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, OperationId VARCHAR(36) NOT NULL UNIQUE, BankAccountId BIGINT NOT NULL, Format INT NOT NULL, StatementReference VARCHAR(200) NOT NULL, ImportHash CHAR(64) NOT NULL UNIQUE, SourceFileName VARCHAR(260) NULL, CurrencyCode VARCHAR(3) NOT NULL, FromDate DATE NOT NULL, ToDate DATE NOT NULL, OpeningBalance DECIMAL(28,9) NOT NULL, ClosingBalance DECIMAL(28,9) NOT NULL, ImportedAtUtc DATETIME(6) NOT NULL, ImportedByUserId BIGINT NOT NULL, UNIQUE KEY UQ_FinanceBankStatements_Reference (BankAccountId,StatementReference), INDEX IX_FinanceBankStatements_AccountDate (BankAccountId,ToDate,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceBankStatementLines (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, StatementId BIGINT NOT NULL, LineNumber INT NOT NULL, BookingDate DATE NOT NULL, ValueDate DATE NULL, Amount DECIMAL(28,9) NOT NULL, CurrencyCode VARCHAR(3) NOT NULL, ExternalId VARCHAR(200) NULL, Reference VARCHAR(500) NULL, CounterpartyName VARCHAR(300) NULL, BankTransactionCode VARCHAR(100) NULL, UNIQUE KEY UQ_FinanceBankStatementLines (StatementId,LineNumber), INDEX IX_FinanceBankStatementLines_Date (StatementId,BookingDate,Id));",
		"CREATE TABLE IF NOT EXISTS FinanceBankReconciliations (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, OperationId VARCHAR(36) NOT NULL UNIQUE, StatementLineId BIGINT NOT NULL, TargetKind INT NOT NULL, TargetId BIGINT NOT NULL, TargetJournalEntryId BIGINT NOT NULL, MatchedAmount DECIMAL(28,9) NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL, ReversalOperationId VARCHAR(36) NULL UNIQUE, ReversedAtUtc DATETIME(6) NULL, ReversedByUserId BIGINT NULL, INDEX IX_FinanceBankReconciliations_Line (StatementLineId,ReversedAtUtc,Id));",
		"CREATE TABLE IF NOT EXISTS FinancePaymentRuns (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Version BIGINT NOT NULL DEFAULT 1, OperationId VARCHAR(36) NOT NULL UNIQUE, BankAccountId BIGINT NOT NULL, PaymentDate DATE NOT NULL, CurrencyCode VARCHAR(3) NOT NULL, Description VARCHAR(500) NOT NULL, Status INT NOT NULL, CreatedAtUtc DATETIME(6) NOT NULL, CreatedByUserId BIGINT NOT NULL, ApprovedAtUtc DATETIME(6) NULL, ApprovedByUserId BIGINT NULL, ApprovalComment VARCHAR(500) NULL, CompletedAtUtc DATETIME(6) NULL, INDEX IX_FinancePaymentRuns_Date (BankAccountId,PaymentDate,Status,Id));",
		"CREATE TABLE IF NOT EXISTS FinancePaymentRunLines (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, PaymentRunId BIGINT NOT NULL, PayableOpenItemId BIGINT NOT NULL, SupplierId BIGINT NOT NULL, Amount DECIMAL(28,9) NOT NULL, Reference VARCHAR(200) NULL, Status INT NOT NULL, ExecutionOperationId VARCHAR(36) NOT NULL UNIQUE, PayablePaymentId BIGINT NULL, ExecutedAtUtc DATETIME(6) NULL, ExecutedByUserId BIGINT NULL, ExecutionReference VARCHAR(200) NULL, UNIQUE KEY UQ_FinancePaymentRunLines_OpenItem (PaymentRunId,PayableOpenItemId));"
	];
}
