// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class DocumentTemplateSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local =>
				"""
				CREATE TABLE IF NOT EXISTS DocumentTemplates
				(
					DocumentType INTEGER NOT NULL,
					Version INTEGER NOT NULL,
					IsActive INTEGER NOT NULL,
					TemplateJson TEXT NOT NULL,
					PRIMARY KEY (DocumentType, Version),
					CHECK (IsActive IN (0, 1))
				);
				CREATE INDEX IF NOT EXISTS IX_DocumentTemplates_TypeActive
					ON DocumentTemplates (DocumentType, IsActive);
				""",
			DatabaseProvider.SqlServer =>
				"""
				IF OBJECT_ID(N'DocumentTemplates', N'U') IS NULL
				BEGIN
					CREATE TABLE DocumentTemplates
					(
						DocumentType int NOT NULL,
						Version int NOT NULL,
						IsActive bit NOT NULL,
						TemplateJson nvarchar(max) NOT NULL,
						CONSTRAINT PK_DocumentTemplates PRIMARY KEY (DocumentType, Version)
					);
				END;
				IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentTemplates_TypeActive' AND object_id = OBJECT_ID(N'DocumentTemplates'))
					CREATE INDEX IX_DocumentTemplates_TypeActive ON DocumentTemplates (DocumentType, IsActive);
				""",
			DatabaseProvider.MySql =>
				"""
				CREATE TABLE IF NOT EXISTS DocumentTemplates
				(
					DocumentType INT NOT NULL,
					Version INT NOT NULL,
					IsActive TINYINT(1) NOT NULL,
					TemplateJson LONGTEXT NOT NULL,
					PRIMARY KEY (DocumentType, Version),
					INDEX IX_DocumentTemplates_TypeActive (DocumentType, IsActive)
				) ENGINE=InnoDB;
				""",
			_ => throw new NotSupportedException($"Document-template persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
