// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class ServicePartOperationSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => """
				CREATE TABLE IF NOT EXISTS ServicePartOperations (
					OperationId TEXT NOT NULL PRIMARY KEY,
					RequestHash TEXT NOT NULL,
					ServicePartEvidenceId INTEGER NOT NULL UNIQUE,
					OriginalConsumptionId INTEGER NULL,
					FOREIGN KEY(ServicePartEvidenceId) REFERENCES ServicePartEvidence(Id),
					FOREIGN KEY(OriginalConsumptionId) REFERENCES ServicePartEvidence(Id),
					CHECK(OriginalConsumptionId IS NULL OR OriginalConsumptionId <> ServicePartEvidenceId));
				CREATE INDEX IF NOT EXISTS IX_ServicePartOperations_Consumption ON ServicePartOperations(OriginalConsumptionId,ServicePartEvidenceId);
				""",
			DatabaseProvider.SqlServer => """
				DECLARE @invoiceConstraint sysname;
				SELECT @invoiceConstraint=constraintInfo.name
				FROM sys.key_constraints constraintInfo
				INNER JOIN sys.index_columns indexColumn ON indexColumn.object_id=constraintInfo.parent_object_id AND indexColumn.index_id=constraintInfo.unique_index_id
				INNER JOIN sys.columns columnInfo ON columnInfo.object_id=indexColumn.object_id AND columnInfo.column_id=indexColumn.column_id
				WHERE constraintInfo.parent_object_id=OBJECT_ID(N'ServiceOrders') AND constraintInfo.type=N'UQ' AND columnInfo.name=N'SalesInvoiceId'
				AND (SELECT COUNT(*) FROM sys.index_columns keys WHERE keys.object_id=constraintInfo.parent_object_id AND keys.index_id=constraintInfo.unique_index_id AND keys.key_ordinal>0)=1;
				IF @invoiceConstraint IS NOT NULL
				BEGIN
					DECLARE @dropInvoiceConstraint nvarchar(max)=N'ALTER TABLE ServiceOrders DROP CONSTRAINT '+QUOTENAME(@invoiceConstraint);
					EXEC sys.sp_executesql @dropInvoiceConstraint;
				END;
				IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ServiceOrders_Invoice' AND object_id=OBJECT_ID(N'ServiceOrders'))
					CREATE UNIQUE INDEX UX_ServiceOrders_Invoice ON ServiceOrders(SalesInvoiceId) WHERE SalesInvoiceId IS NOT NULL;
				IF OBJECT_ID(N'ServicePartOperations',N'U') IS NULL CREATE TABLE ServicePartOperations (
					OperationId varchar(32) NOT NULL PRIMARY KEY,
					RequestHash varchar(64) NOT NULL,
					ServicePartEvidenceId bigint NOT NULL UNIQUE,
					OriginalConsumptionId bigint NULL,
					CONSTRAINT FK_ServicePartOperations_Evidence FOREIGN KEY(ServicePartEvidenceId) REFERENCES ServicePartEvidence(Id),
					CONSTRAINT FK_ServicePartOperations_Consumption FOREIGN KEY(OriginalConsumptionId) REFERENCES ServicePartEvidence(Id),
					CONSTRAINT CK_ServicePartOperations_Consumption CHECK(OriginalConsumptionId IS NULL OR OriginalConsumptionId <> ServicePartEvidenceId));
				IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ServicePartOperations_Consumption' AND object_id=OBJECT_ID(N'ServicePartOperations'))
					CREATE INDEX IX_ServicePartOperations_Consumption ON ServicePartOperations(OriginalConsumptionId,ServicePartEvidenceId);
				""",
			DatabaseProvider.MySql => """
				CREATE TABLE IF NOT EXISTS ServicePartOperations (
					OperationId varchar(32) NOT NULL PRIMARY KEY,
					RequestHash varchar(64) NOT NULL,
					ServicePartEvidenceId bigint NOT NULL UNIQUE,
					OriginalConsumptionId bigint NULL,
					INDEX IX_ServicePartOperations_Consumption(OriginalConsumptionId,ServicePartEvidenceId),
					CONSTRAINT FK_ServicePartOperations_Evidence FOREIGN KEY(ServicePartEvidenceId) REFERENCES ServicePartEvidence(Id),
					CONSTRAINT FK_ServicePartOperations_Consumption FOREIGN KEY(OriginalConsumptionId) REFERENCES ServicePartEvidence(Id),
					CONSTRAINT CK_ServicePartOperations_Consumption CHECK(OriginalConsumptionId IS NULL OR OriginalConsumptionId <> ServicePartEvidenceId)) ENGINE=InnoDB;
				""",
			_ => throw new NotSupportedException($"Service-parts operations are not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
