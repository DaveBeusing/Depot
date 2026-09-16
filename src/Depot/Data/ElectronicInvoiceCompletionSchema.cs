// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class ElectronicInvoiceCompletionSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		foreach (var statement in Statements(connectionFactory.Provider))
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
	}

	private static IReadOnlyList<string> Statements(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local =>
		[
			"ALTER TABLE SalesInvoiceLines ADD COLUMN TaxCategoryCode TEXT NOT NULL DEFAULT 'S';",
			"ALTER TABLE SalesInvoiceLines ADD COLUMN TaxExemptionReasonCode TEXT NULL;",
			"ALTER TABLE SalesInvoiceLines ADD COLUMN TaxExemptionReason TEXT NULL;",
			"ALTER TABLE SalesCreditNoteLines ADD COLUMN TaxCategoryCode TEXT NOT NULL DEFAULT 'S';",
			"ALTER TABLE SalesCreditNoteLines ADD COLUMN TaxExemptionReasonCode TEXT NULL;",
			"ALTER TABLE SalesCreditNoteLines ADD COLUMN TaxExemptionReason TEXT NULL;",
			"CREATE TABLE IF NOT EXISTS SalesCreditNoteFinalizations (SalesCreditNoteId INTEGER NOT NULL PRIMARY KEY, BuyerPayload TEXT NOT NULL, XRechnungXml TEXT NOT NULL, XRechnungSha256 TEXT NOT NULL, FinalizedAtUtc TEXT NOT NULL, FOREIGN KEY(SalesCreditNoteId) REFERENCES SalesCreditNotes(Id));",
			"CREATE TABLE IF NOT EXISTS SalesElectronicInvoiceEvidence (DocumentType TEXT NOT NULL, DocumentId INTEGER NOT NULL, RecipientIdentifier TEXT NOT NULL, RecipientScheme TEXT NOT NULL, RoutingChannel TEXT NOT NULL, GuidelineId TEXT NOT NULL, ValidatorProfile TEXT NOT NULL, FinalizedAtUtc TEXT NOT NULL, PRIMARY KEY(DocumentType,DocumentId));"
		],
		DatabaseProvider.SqlServer =>
		[
			"IF COL_LENGTH('SalesInvoiceLines','TaxCategoryCode') IS NULL ALTER TABLE SalesInvoiceLines ADD TaxCategoryCode nvarchar(10) NOT NULL CONSTRAINT DF_SalesInvoiceLines_TaxCategoryCode DEFAULT N'S';",
			"IF COL_LENGTH('SalesInvoiceLines','TaxExemptionReasonCode') IS NULL ALTER TABLE SalesInvoiceLines ADD TaxExemptionReasonCode nvarchar(100) NULL;",
			"IF COL_LENGTH('SalesInvoiceLines','TaxExemptionReason') IS NULL ALTER TABLE SalesInvoiceLines ADD TaxExemptionReason nvarchar(500) NULL;",
			"IF COL_LENGTH('SalesCreditNoteLines','TaxCategoryCode') IS NULL ALTER TABLE SalesCreditNoteLines ADD TaxCategoryCode nvarchar(10) NOT NULL CONSTRAINT DF_SalesCreditNoteLines_TaxCategoryCode DEFAULT N'S';",
			"IF COL_LENGTH('SalesCreditNoteLines','TaxExemptionReasonCode') IS NULL ALTER TABLE SalesCreditNoteLines ADD TaxExemptionReasonCode nvarchar(100) NULL;",
			"IF COL_LENGTH('SalesCreditNoteLines','TaxExemptionReason') IS NULL ALTER TABLE SalesCreditNoteLines ADD TaxExemptionReason nvarchar(500) NULL;",
			"IF OBJECT_ID(N'SalesCreditNoteFinalizations',N'U') IS NULL CREATE TABLE SalesCreditNoteFinalizations (SalesCreditNoteId bigint NOT NULL CONSTRAINT PK_SalesCreditNoteFinalizations PRIMARY KEY, BuyerPayload nvarchar(max) NOT NULL, XRechnungXml nvarchar(max) NOT NULL, XRechnungSha256 char(64) NOT NULL, FinalizedAtUtc nvarchar(40) NOT NULL, CONSTRAINT FK_SalesCreditNoteFinalizations_CreditNote FOREIGN KEY(SalesCreditNoteId) REFERENCES SalesCreditNotes(Id));",
			"IF OBJECT_ID(N'SalesElectronicInvoiceEvidence',N'U') IS NULL CREATE TABLE SalesElectronicInvoiceEvidence (DocumentType nvarchar(30) NOT NULL, DocumentId bigint NOT NULL, RecipientIdentifier nvarchar(250) NOT NULL, RecipientScheme nvarchar(50) NOT NULL, RoutingChannel nvarchar(50) NOT NULL, GuidelineId nvarchar(250) NOT NULL, ValidatorProfile nvarchar(100) NOT NULL, FinalizedAtUtc nvarchar(40) NOT NULL, CONSTRAINT PK_SalesElectronicInvoiceEvidence PRIMARY KEY(DocumentType,DocumentId));"
		],
		DatabaseProvider.MySql =>
		[
			"ALTER TABLE SalesInvoiceLines ADD COLUMN TaxCategoryCode VARCHAR(10) NOT NULL DEFAULT 'S';",
			"ALTER TABLE SalesInvoiceLines ADD COLUMN TaxExemptionReasonCode VARCHAR(100) NULL;",
			"ALTER TABLE SalesInvoiceLines ADD COLUMN TaxExemptionReason VARCHAR(500) NULL;",
			"ALTER TABLE SalesCreditNoteLines ADD COLUMN TaxCategoryCode VARCHAR(10) NOT NULL DEFAULT 'S';",
			"ALTER TABLE SalesCreditNoteLines ADD COLUMN TaxExemptionReasonCode VARCHAR(100) NULL;",
			"ALTER TABLE SalesCreditNoteLines ADD COLUMN TaxExemptionReason VARCHAR(500) NULL;",
			"CREATE TABLE IF NOT EXISTS SalesCreditNoteFinalizations (SalesCreditNoteId BIGINT NOT NULL PRIMARY KEY, BuyerPayload LONGTEXT NOT NULL, XRechnungXml LONGTEXT NOT NULL, XRechnungSha256 CHAR(64) NOT NULL, FinalizedAtUtc VARCHAR(40) NOT NULL, FOREIGN KEY(SalesCreditNoteId) REFERENCES SalesCreditNotes(Id)) ENGINE=InnoDB;",
			"CREATE TABLE IF NOT EXISTS SalesElectronicInvoiceEvidence (DocumentType VARCHAR(30) NOT NULL, DocumentId BIGINT NOT NULL, RecipientIdentifier VARCHAR(250) NOT NULL, RecipientScheme VARCHAR(50) NOT NULL, RoutingChannel VARCHAR(50) NOT NULL, GuidelineId VARCHAR(250) NOT NULL, ValidatorProfile VARCHAR(100) NOT NULL, FinalizedAtUtc VARCHAR(40) NOT NULL, PRIMARY KEY(DocumentType,DocumentId)) ENGINE=InnoDB;"
		],
		_ => throw new NotSupportedException($"Electronic invoice completion schema is not supported for provider '{provider}'.")
	};
}
