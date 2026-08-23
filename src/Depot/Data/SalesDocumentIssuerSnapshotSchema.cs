// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class SalesDocumentIssuerSnapshotSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS SalesDocumentIssuerSnapshots (DocumentType INTEGER NOT NULL, DocumentId INTEGER NOT NULL, Payload TEXT NOT NULL, CapturedAtUtc TEXT NOT NULL, PRIMARY KEY (DocumentType, DocumentId));",
			DatabaseProvider.SqlServer => "IF OBJECT_ID(N'SalesDocumentIssuerSnapshots', N'U') IS NULL CREATE TABLE SalesDocumentIssuerSnapshots (DocumentType int NOT NULL, DocumentId bigint NOT NULL, Payload nvarchar(max) NOT NULL, CapturedAtUtc nvarchar(40) NOT NULL, CONSTRAINT PK_SalesDocumentIssuerSnapshots PRIMARY KEY (DocumentType, DocumentId));",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS SalesDocumentIssuerSnapshots (DocumentType INT NOT NULL, DocumentId BIGINT NOT NULL, Payload LONGTEXT NOT NULL, CapturedAtUtc VARCHAR(40) NOT NULL, PRIMARY KEY (DocumentType, DocumentId)) ENGINE=InnoDB;",
			_ => throw new NotSupportedException($"Issuer snapshot schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
