// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class ZugferdFacturXSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS SalesHybridElectronicInvoiceArtifacts (DocumentType TEXT NOT NULL, DocumentId INTEGER NOT NULL, StandardVersion TEXT NOT NULL, Profile TEXT NOT NULL, XmlFileName TEXT NOT NULL, PdfAConformance TEXT NOT NULL, XmlSha256 TEXT NOT NULL, PdfSha256 TEXT NOT NULL, PdfBytes BLOB NOT NULL, CreatedAtUtc TEXT NOT NULL, PRIMARY KEY(DocumentType,DocumentId));",
			DatabaseProvider.SqlServer => "IF OBJECT_ID(N'SalesHybridElectronicInvoiceArtifacts',N'U') IS NULL CREATE TABLE SalesHybridElectronicInvoiceArtifacts (DocumentType nvarchar(30) NOT NULL, DocumentId bigint NOT NULL, StandardVersion nvarchar(80) NOT NULL, Profile nvarchar(40) NOT NULL, XmlFileName nvarchar(100) NOT NULL, PdfAConformance nvarchar(30) NOT NULL, XmlSha256 char(64) NOT NULL, PdfSha256 char(64) NOT NULL, PdfBytes varbinary(max) NOT NULL, CreatedAtUtc nvarchar(40) NOT NULL, CONSTRAINT PK_SalesHybridElectronicInvoiceArtifacts PRIMARY KEY(DocumentType,DocumentId));",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS SalesHybridElectronicInvoiceArtifacts (DocumentType VARCHAR(30) NOT NULL, DocumentId BIGINT NOT NULL, StandardVersion VARCHAR(80) NOT NULL, Profile VARCHAR(40) NOT NULL, XmlFileName VARCHAR(100) NOT NULL, PdfAConformance VARCHAR(30) NOT NULL, XmlSha256 CHAR(64) NOT NULL, PdfSha256 CHAR(64) NOT NULL, PdfBytes LONGBLOB NOT NULL, CreatedAtUtc VARCHAR(40) NOT NULL, PRIMARY KEY(DocumentType,DocumentId)) ENGINE=InnoDB;",
			_ => throw new NotSupportedException($"ZUGFeRD/Factur-X schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
