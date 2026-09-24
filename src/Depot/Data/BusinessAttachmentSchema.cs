// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class BusinessAttachmentSchema
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
				CREATE TABLE IF NOT EXISTS BusinessAttachments
				(
					Id TEXT NOT NULL PRIMARY KEY,
					EntityKind INTEGER NOT NULL,
					EntityId INTEGER NOT NULL,
					FileName TEXT NOT NULL,
					MediaType TEXT NOT NULL,
					ByteLength INTEGER NOT NULL,
					Sha256 TEXT NOT NULL,
					Description TEXT NULL,
					Category TEXT NULL,
					CreatedByUserId INTEGER NULL,
					CreatedAtUtc TEXT NOT NULL,
					CurrentRevision INTEGER NOT NULL,
					Status INTEGER NOT NULL,
					Version INTEGER NOT NULL,
					CHECK (EntityKind BETWEEN 1 AND 13),
					CHECK (ByteLength >= 0),
					CHECK (CurrentRevision > 0),
					CHECK (Status IN (1, 2)),
					CHECK (Version > 0)
				);
				CREATE TABLE IF NOT EXISTS BusinessAttachmentRevisions
				(
					AttachmentId TEXT NOT NULL,
					Revision INTEGER NOT NULL,
					FileName TEXT NOT NULL,
					MediaType TEXT NOT NULL,
					ByteLength INTEGER NOT NULL,
					Sha256 TEXT NOT NULL,
					CreatedByUserId INTEGER NULL,
					CreatedAtUtc TEXT NOT NULL,
					PRIMARY KEY (AttachmentId, Revision),
					FOREIGN KEY (AttachmentId) REFERENCES BusinessAttachments(Id) ON DELETE RESTRICT,
					CHECK (Revision > 0),
					CHECK (ByteLength >= 0)
				);
				CREATE TABLE IF NOT EXISTS BusinessAttachmentContents
				(
					AttachmentId TEXT NOT NULL,
					Revision INTEGER NOT NULL,
					Content BLOB NOT NULL,
					PRIMARY KEY (AttachmentId, Revision),
					FOREIGN KEY (AttachmentId, Revision) REFERENCES BusinessAttachmentRevisions(AttachmentId, Revision) ON DELETE RESTRICT
				);
				CREATE INDEX IF NOT EXISTS IX_BusinessAttachments_Entity
					ON BusinessAttachments (EntityKind, EntityId, Status, CreatedAtUtc);
				CREATE INDEX IF NOT EXISTS IX_BusinessAttachments_Metadata
					ON BusinessAttachments (FileName, Category);
				""",
			DatabaseProvider.SqlServer =>
				"""
				IF OBJECT_ID(N'BusinessAttachments', N'U') IS NULL
				BEGIN
					CREATE TABLE BusinessAttachments
					(
						Id char(36) NOT NULL,
						EntityKind int NOT NULL,
						EntityId bigint NOT NULL,
						FileName nvarchar(255) NOT NULL,
						MediaType nvarchar(127) NOT NULL,
						ByteLength bigint NOT NULL,
						Sha256 char(64) NOT NULL,
						Description nvarchar(1000) NULL,
						Category nvarchar(100) NULL,
						CreatedByUserId bigint NULL,
						CreatedAtUtc datetime2(6) NOT NULL,
						CurrentRevision int NOT NULL,
						Status int NOT NULL,
						Version bigint NOT NULL,
						CONSTRAINT PK_BusinessAttachments PRIMARY KEY (Id),
						CONSTRAINT CK_BusinessAttachments_EntityKind CHECK (EntityKind BETWEEN 1 AND 13),
						CONSTRAINT CK_BusinessAttachments_ByteLength CHECK (ByteLength >= 0),
						CONSTRAINT CK_BusinessAttachments_CurrentRevision CHECK (CurrentRevision > 0),
						CONSTRAINT CK_BusinessAttachments_Status CHECK (Status IN (1, 2)),
						CONSTRAINT CK_BusinessAttachments_Version CHECK (Version > 0)
					);
				END;
				IF OBJECT_ID(N'BusinessAttachmentRevisions', N'U') IS NULL
				BEGIN
					CREATE TABLE BusinessAttachmentRevisions
					(
						AttachmentId char(36) NOT NULL,
						Revision int NOT NULL,
						FileName nvarchar(255) NOT NULL,
						MediaType nvarchar(127) NOT NULL,
						ByteLength bigint NOT NULL,
						Sha256 char(64) NOT NULL,
						CreatedByUserId bigint NULL,
						CreatedAtUtc datetime2(6) NOT NULL,
						CONSTRAINT PK_BusinessAttachmentRevisions PRIMARY KEY (AttachmentId, Revision),
						CONSTRAINT FK_BusinessAttachmentRevisions_Attachment FOREIGN KEY (AttachmentId) REFERENCES BusinessAttachments(Id),
						CONSTRAINT CK_BusinessAttachmentRevisions_Revision CHECK (Revision > 0),
						CONSTRAINT CK_BusinessAttachmentRevisions_ByteLength CHECK (ByteLength >= 0)
					);
				END;
				IF OBJECT_ID(N'BusinessAttachmentContents', N'U') IS NULL
				BEGIN
					CREATE TABLE BusinessAttachmentContents
					(
						AttachmentId char(36) NOT NULL,
						Revision int NOT NULL,
						Content varbinary(max) NOT NULL,
						CONSTRAINT PK_BusinessAttachmentContents PRIMARY KEY (AttachmentId, Revision),
						CONSTRAINT FK_BusinessAttachmentContents_Revision FOREIGN KEY (AttachmentId, Revision) REFERENCES BusinessAttachmentRevisions(AttachmentId, Revision)
					);
				END;
				IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_BusinessAttachments_Entity' AND object_id=OBJECT_ID(N'BusinessAttachments'))
					CREATE INDEX IX_BusinessAttachments_Entity ON BusinessAttachments (EntityKind, EntityId, Status, CreatedAtUtc);
				IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_BusinessAttachments_Metadata' AND object_id=OBJECT_ID(N'BusinessAttachments'))
					CREATE INDEX IX_BusinessAttachments_Metadata ON BusinessAttachments (FileName, Category);
				""",
			DatabaseProvider.MySql =>
				"""
				CREATE TABLE IF NOT EXISTS BusinessAttachments
				(
					Id CHAR(36) NOT NULL,
					EntityKind INT NOT NULL,
					EntityId BIGINT NOT NULL,
					FileName VARCHAR(255) NOT NULL,
					MediaType VARCHAR(127) NOT NULL,
					ByteLength BIGINT NOT NULL,
					Sha256 CHAR(64) NOT NULL,
					Description VARCHAR(1000) NULL,
					Category VARCHAR(100) NULL,
					CreatedByUserId BIGINT NULL,
					CreatedAtUtc DATETIME(6) NOT NULL,
					CurrentRevision INT NOT NULL,
					Status INT NOT NULL,
					Version BIGINT NOT NULL,
					PRIMARY KEY (Id),
					CONSTRAINT CK_BusinessAttachments_EntityKind CHECK (EntityKind BETWEEN 1 AND 13),
					CONSTRAINT CK_BusinessAttachments_ByteLength CHECK (ByteLength >= 0),
					CONSTRAINT CK_BusinessAttachments_CurrentRevision CHECK (CurrentRevision > 0),
					CONSTRAINT CK_BusinessAttachments_Status CHECK (Status IN (1, 2)),
					CONSTRAINT CK_BusinessAttachments_Version CHECK (Version > 0),
					INDEX IX_BusinessAttachments_Entity (EntityKind, EntityId, Status, CreatedAtUtc),
					INDEX IX_BusinessAttachments_Metadata (FileName, Category)
				) ENGINE=InnoDB;
				CREATE TABLE IF NOT EXISTS BusinessAttachmentRevisions
				(
					AttachmentId CHAR(36) NOT NULL,
					Revision INT NOT NULL,
					FileName VARCHAR(255) NOT NULL,
					MediaType VARCHAR(127) NOT NULL,
					ByteLength BIGINT NOT NULL,
					Sha256 CHAR(64) NOT NULL,
					CreatedByUserId BIGINT NULL,
					CreatedAtUtc DATETIME(6) NOT NULL,
					PRIMARY KEY (AttachmentId, Revision),
					CONSTRAINT FK_BusinessAttachmentRevisions_Attachment FOREIGN KEY (AttachmentId) REFERENCES BusinessAttachments(Id),
					CONSTRAINT CK_BusinessAttachmentRevisions_Revision CHECK (Revision > 0),
					CONSTRAINT CK_BusinessAttachmentRevisions_ByteLength CHECK (ByteLength >= 0)
				) ENGINE=InnoDB;
				CREATE TABLE IF NOT EXISTS BusinessAttachmentContents
				(
					AttachmentId CHAR(36) NOT NULL,
					Revision INT NOT NULL,
					Content LONGBLOB NOT NULL,
					PRIMARY KEY (AttachmentId, Revision),
					CONSTRAINT FK_BusinessAttachmentContents_Revision FOREIGN KEY (AttachmentId, Revision) REFERENCES BusinessAttachmentRevisions(AttachmentId, Revision)
				) ENGINE=InnoDB;
				""",
			_ => throw new NotSupportedException($"Business-attachment persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	public static void ExpandEntityKindConstraint(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();

		if (connectionFactory.Provider == DatabaseProvider.Local)
		{
			using (var foreignKeysOff = connection.CreateCommand())
			{
				foreignKeysOff.CommandText = "PRAGMA foreign_keys=OFF;";
				foreignKeysOff.ExecuteNonQuery();
			}

			try
			{
				using var transaction = connectionFactory.BeginWriteTransaction(connection);
				using var command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandText =
					"""
					DROP TABLE IF EXISTS BusinessAttachments_V2;
					CREATE TABLE BusinessAttachments_V2
					(
						Id TEXT NOT NULL PRIMARY KEY,
						EntityKind INTEGER NOT NULL,
						EntityId INTEGER NOT NULL,
						FileName TEXT NOT NULL,
						MediaType TEXT NOT NULL,
						ByteLength INTEGER NOT NULL,
						Sha256 TEXT NOT NULL,
						Description TEXT NULL,
						Category TEXT NULL,
						CreatedByUserId INTEGER NULL,
						CreatedAtUtc TEXT NOT NULL,
						CurrentRevision INTEGER NOT NULL,
						Status INTEGER NOT NULL,
						Version INTEGER NOT NULL,
						CHECK (EntityKind BETWEEN 1 AND 13),
						CHECK (ByteLength >= 0),
						CHECK (CurrentRevision > 0),
						CHECK (Status IN (1, 2)),
						CHECK (Version > 0)
					);
					INSERT INTO BusinessAttachments_V2
						(Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,Description,Category,CreatedByUserId,CreatedAtUtc,CurrentRevision,Status,Version)
					SELECT
						Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,Description,Category,CreatedByUserId,CreatedAtUtc,CurrentRevision,Status,Version
					FROM BusinessAttachments;
					DROP TABLE BusinessAttachments;
					ALTER TABLE BusinessAttachments_V2 RENAME TO BusinessAttachments;
					CREATE INDEX IX_BusinessAttachments_Entity
						ON BusinessAttachments (EntityKind, EntityId, Status, CreatedAtUtc);
					CREATE INDEX IX_BusinessAttachments_Metadata
						ON BusinessAttachments (FileName, Category);
					""";
				command.ExecuteNonQuery();
				transaction.Commit();
			}
			finally
			{
				using var foreignKeysOn = connection.CreateCommand();
				foreignKeysOn.CommandText = "PRAGMA foreign_keys=ON;";
				foreignKeysOn.ExecuteNonQuery();
			}

			return;
		}

		using var constraintCommand = connection.CreateCommand();
		constraintCommand.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.SqlServer =>
				"""
				BEGIN TRANSACTION;
				BEGIN TRY
					IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'BusinessAttachments') AND name=N'CK_BusinessAttachments_EntityKind')
						ALTER TABLE BusinessAttachments DROP CONSTRAINT CK_BusinessAttachments_EntityKind;
					ALTER TABLE BusinessAttachments WITH CHECK
						ADD CONSTRAINT CK_BusinessAttachments_EntityKind CHECK (EntityKind BETWEEN 1 AND 13);
					COMMIT TRANSACTION;
				END TRY
				BEGIN CATCH
					IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
					THROW;
				END CATCH;
				""",
			DatabaseProvider.MySql =>
				"""
				ALTER TABLE BusinessAttachments
					DROP CONSTRAINT CK_BusinessAttachments_EntityKind,
					ADD CONSTRAINT CK_BusinessAttachments_EntityKind CHECK (EntityKind BETWEEN 1 AND 13);
				""",
			_ => throw new NotSupportedException($"Business-attachment constraint migration is not supported for provider '{connectionFactory.Provider}'.")
		};
		constraintCommand.ExecuteNonQuery();
	}

}
