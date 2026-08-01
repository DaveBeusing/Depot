// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

namespace Depot.Data;

public sealed class SqlServerDatabase : IDatabaseInitializer
{
	private readonly SqlServerConnectionFactory _connectionFactory;

	public SqlServerDatabase(SqlServerConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public void Initialize()
	{
		EnsureDatabaseExists();
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connection.BeginTransaction();
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = SchemaSql;
		command.Parameters.AddWithValue(
			"@DefaultPasswordHash",
			"pbkdf2-sha256$210000$9vL0kVt/HZBUCpsJYjPW6Q==$B1lZ+NRxxR/E8kwIE5PK0wXR2BPDmFTeLiKYyAEuhaE=");
		command.Parameters.AddWithValue("@CurrentVersion", DatabaseVersion.CurrentVersion);
		command.ExecuteNonQuery();
		EnsureReasonCodeTechnicalKeys(command);
		command.CommandText = ProcurementSql;
		command.Parameters.Clear();
		command.ExecuteNonQuery();
		command.CommandText = StockTransferSql;
		command.ExecuteNonQuery();
		command.CommandText = InventoryCountSql;
		command.ExecuteNonQuery();
		command.CommandText = MaterialIssueSql;
		command.ExecuteNonQuery();
		command.CommandText = MaterialReturnSql;
		command.ExecuteNonQuery();
		command.CommandText = SupplierReturnSql;
		command.ExecuteNonQuery();
		EnsureWorkflowOperations(command);

		command.CommandText = "SELECT Version FROM DatabaseInfo WHERE Id = 1;";
		var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
		if (version == 8)
		{
			MigrateToWarehouseStructure(command);
			version = 9;
		}
		if (version == 9)
		{
			MigrateToReasonCodes(command);
			version = 10;
		}
		if (version == 10)
		{
			MigrateToNormalizedItemMasterData(command);
			version = 11;
		}
		if (version == 11)
		{
			MigrateToSupplierManagement(command);
			version = 12;
		}
		if (version == 12)
		{
			MigrateSupplierAccountFields(command);
			version = 13;
		}
		if (version == 13)
		{
			MigrateSupplierClassification(command);
			version = 14;
		}
		if (version == 14)
		{
			MigrateToProcurement(command);
			version = 15;
		}
		if (version == 15)
		{
			MigrateReasonCodesToTechnicalKeys(command);
			version = 16;
		}
		if (version == 16)
		{
			MigrateGoodsReceiptsToDeliveryDocuments(command);
			version = 17;
		}
		if (version == 17)
		{
			MigrateToStockTransfers(command);
			version = 18;
		}
		if (version == 18)
		{
			MigrateToInventoryCounts(command);
			version = 19;
		}
		if (version == 19)
		{
			MigrateToMovementReversals(command);
			version = 20;
		}
		if (version == 20)
		{
			MigrateToPurchaseOrderApproval(command);
			version = 21;
		}
		if (version == 21)
		{
			MigrateToPurchaseOrderClosure(command);
			version = 22;
		}
		if (version == 22)
		{
			MigrateToMaterialIssues(command);
			version = 23;
		}
		if (version == 23)
		{
			MigrateToMaterialReturns(command);
			version = 24;
		}
		if (version == 24)
		{
			MigrateToSupplierReturns(command);
			version = 25;
		}
		if (version == 25)
		{
			MigrateToFixedUserRoles(command);
			version = 26;
		}
		if (version == 26)
		{
			MigrateToWorkflowOperations(command);
			version = 27;
		}
		if (version != DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"SQL Server schema version '{version}' is not supported. Expected '{DatabaseVersion.CurrentVersion}'.");
		}
		command.CommandText = "UPDATE Users SET Role = 1 WHERE IsAdministrator = 1 AND Role = 0;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();

		transaction.Commit();
	}

	private static void MigrateToPurchaseOrderApproval(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'Users', N'CanApprovePurchaseOrders') IS NULL ALTER TABLE Users ADD CanApprovePurchaseOrders bit NOT NULL CONSTRAINT DF_Users_CanApprovePurchaseOrders DEFAULT 0;
		IF COL_LENGTH(N'PurchaseOrders', N'CreatedByUserId') IS NULL ALTER TABLE PurchaseOrders ADD CreatedByUserId bigint NULL;
		IF COL_LENGTH(N'PurchaseOrders', N'SubmittedByUserId') IS NULL ALTER TABLE PurchaseOrders ADD SubmittedByUserId bigint NULL;
		IF COL_LENGTH(N'PurchaseOrders', N'SubmittedAtUtc') IS NULL ALTER TABLE PurchaseOrders ADD SubmittedAtUtc nvarchar(40) NULL;
		IF COL_LENGTH(N'PurchaseOrders', N'ApprovalDecisionByUserId') IS NULL ALTER TABLE PurchaseOrders ADD ApprovalDecisionByUserId bigint NULL;
		IF COL_LENGTH(N'PurchaseOrders', N'ApprovalDecisionAtUtc') IS NULL ALTER TABLE PurchaseOrders ADD ApprovalDecisionAtUtc nvarchar(40) NULL;
		IF COL_LENGTH(N'PurchaseOrders', N'ApprovalComment') IS NULL ALTER TABLE PurchaseOrders ADD ApprovalComment nvarchar(2000) NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PurchaseOrders_CreatedByUsers') ALTER TABLE PurchaseOrders ADD CONSTRAINT FK_PurchaseOrders_CreatedByUsers FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PurchaseOrders_SubmittedByUsers') ALTER TABLE PurchaseOrders ADD CONSTRAINT FK_PurchaseOrders_SubmittedByUsers FOREIGN KEY (SubmittedByUserId) REFERENCES Users(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PurchaseOrders_ApprovalDecisionByUsers') ALTER TABLE PurchaseOrders ADD CONSTRAINT FK_PurchaseOrders_ApprovalDecisionByUsers FOREIGN KEY (ApprovalDecisionByUserId) REFERENCES Users(Id);
		UPDATE DatabaseInfo SET Version = 21 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToPurchaseOrderClosure(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'PurchaseOrders', N'ClosedByUserId') IS NULL ALTER TABLE PurchaseOrders ADD ClosedByUserId bigint NULL;
		IF COL_LENGTH(N'PurchaseOrders', N'ClosedAtUtc') IS NULL ALTER TABLE PurchaseOrders ADD ClosedAtUtc nvarchar(40) NULL;
		IF COL_LENGTH(N'PurchaseOrders', N'CloseReason') IS NULL ALTER TABLE PurchaseOrders ADD CloseReason nvarchar(2000) NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PurchaseOrders_ClosedByUsers') ALTER TABLE PurchaseOrders ADD CONSTRAINT FK_PurchaseOrders_ClosedByUsers FOREIGN KEY (ClosedByUserId) REFERENCES Users(Id);
		UPDATE DatabaseInfo SET Version = 22 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToMaterialIssues(System.Data.Common.DbCommand command)
	{
		command.CommandText = MaterialIssueSql + " UPDATE DatabaseInfo SET Version = 23 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToMaterialReturns(System.Data.Common.DbCommand command)
	{
		command.CommandText = MaterialReturnSql + " UPDATE DatabaseInfo SET Version = 24 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToSupplierReturns(System.Data.Common.DbCommand command)
	{
		command.CommandText = SupplierReturnSql + " UPDATE DatabaseInfo SET Version = 25 WHERE Id = 1;";
		command.Parameters.Clear(); command.ExecuteNonQuery();
	}

	private static void MigrateToFixedUserRoles(System.Data.Common.DbCommand command)
	{
		command.CommandText = "IF COL_LENGTH(N'Users', N'Role') IS NULL ALTER TABLE Users ADD Role int NOT NULL CONSTRAINT DF_Users_Role DEFAULT 0; UPDATE Users SET Role = CASE WHEN IsAdministrator = 1 THEN 1 WHEN CanApprovePurchaseOrders = 1 THEN 3 ELSE 0 END; IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Users_Role') ALTER TABLE Users ADD CONSTRAINT CK_Users_Role CHECK (Role IN (0,1,2,3,4)); UPDATE DatabaseInfo SET Version = 26 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToWorkflowOperations(System.Data.Common.DbCommand command)
	{
		EnsureWorkflowOperations(command);
		command.CommandText = "UPDATE DatabaseInfo SET Version = 27 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void EnsureWorkflowOperations(System.Data.Common.DbCommand command)
	{
		command.CommandText = "IF OBJECT_ID(N'WorkflowOperations', N'U') IS NULL CREATE TABLE WorkflowOperations (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkflowOperations PRIMARY KEY, OperationId nvarchar(36) NOT NULL CONSTRAINT UQ_WorkflowOperations_OperationId UNIQUE, Workflow nvarchar(100) NOT NULL, EntityId bigint NOT NULL, CompletedAtUtc nvarchar(40) NOT NULL); IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowOperations_Entity') CREATE INDEX IX_WorkflowOperations_Entity ON WorkflowOperations(Workflow, EntityId);";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToStockTransfers(System.Data.Common.DbCommand command)
	{
		command.CommandText = StockTransferSql + " UPDATE DatabaseInfo SET Version = 18 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToInventoryCounts(System.Data.Common.DbCommand command)
	{
		command.CommandText = InventoryCountSql + " UPDATE DatabaseInfo SET Version = 19 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToMovementReversals(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'StockMovements', N'ReversalOfMovementId') IS NULL ALTER TABLE StockMovements ADD ReversalOfMovementId bigint NULL;
		IF COL_LENGTH(N'StockMovements', N'ReversalReason') IS NULL ALTER TABLE StockMovements ADD ReversalReason nvarchar(1000) NULL;
		IF COL_LENGTH(N'StockMovements', N'ReversedAtUtc') IS NULL ALTER TABLE StockMovements ADD ReversedAtUtc nvarchar(40) NULL;
		IF COL_LENGTH(N'StockMovements', N'ReversedByUserId') IS NULL ALTER TABLE StockMovements ADD ReversedByUserId bigint NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockMovements_ReversalOfMovement') ALTER TABLE StockMovements ADD CONSTRAINT FK_StockMovements_ReversalOfMovement FOREIGN KEY (ReversalOfMovementId) REFERENCES StockMovements(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockMovements_ReversedByUsers') ALTER TABLE StockMovements ADD CONSTRAINT FK_StockMovements_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_StockMovements_ReversalOfMovementId' AND object_id = OBJECT_ID(N'StockMovements')) CREATE UNIQUE INDEX UX_StockMovements_ReversalOfMovementId ON StockMovements(ReversalOfMovementId) WHERE ReversalOfMovementId IS NOT NULL;

		IF COL_LENGTH(N'GoodsReceipts', N'ReversedAtUtc') IS NULL ALTER TABLE GoodsReceipts ADD ReversedAtUtc nvarchar(40) NULL;
		IF COL_LENGTH(N'GoodsReceipts', N'ReversedByUserId') IS NULL ALTER TABLE GoodsReceipts ADD ReversedByUserId bigint NULL;
		IF COL_LENGTH(N'GoodsReceipts', N'ReversalReason') IS NULL ALTER TABLE GoodsReceipts ADD ReversalReason nvarchar(1000) NULL;
		IF COL_LENGTH(N'GoodsReceipts', N'Version') IS NULL ALTER TABLE GoodsReceipts ADD Version bigint NOT NULL CONSTRAINT DF_GoodsReceipts_Version DEFAULT 1;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_GoodsReceipts_ReversedByUsers') ALTER TABLE GoodsReceipts ADD CONSTRAINT FK_GoodsReceipts_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id);

		IF COL_LENGTH(N'StockTransfers', N'ReversedAtUtc') IS NULL ALTER TABLE StockTransfers ADD ReversedAtUtc nvarchar(40) NULL;
		IF COL_LENGTH(N'StockTransfers', N'ReversedByUserId') IS NULL ALTER TABLE StockTransfers ADD ReversedByUserId bigint NULL;
		IF COL_LENGTH(N'StockTransfers', N'ReversalReason') IS NULL ALTER TABLE StockTransfers ADD ReversalReason nvarchar(1000) NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockTransfers_ReversedByUsers') ALTER TABLE StockTransfers ADD CONSTRAINT FK_StockTransfers_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id);

		IF COL_LENGTH(N'InventoryCounts', N'ReversedAtUtc') IS NULL ALTER TABLE InventoryCounts ADD ReversedAtUtc nvarchar(40) NULL;
		IF COL_LENGTH(N'InventoryCounts', N'ReversedByUserId') IS NULL ALTER TABLE InventoryCounts ADD ReversedByUserId bigint NULL;
		IF COL_LENGTH(N'InventoryCounts', N'ReversalReason') IS NULL ALTER TABLE InventoryCounts ADD ReversalReason nvarchar(1000) NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InventoryCounts_ReversedByUsers') ALTER TABLE InventoryCounts ADD CONSTRAINT FK_InventoryCounts_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id);

		UPDATE DatabaseInfo SET Version = 20 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateGoodsReceiptsToDeliveryDocuments(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'GoodsReceipts', N'SupplierDeliveryNoteNumber') IS NULL
			ALTER TABLE GoodsReceipts ADD SupplierDeliveryNoteNumber nvarchar(100) NULL;
		IF COL_LENGTH(N'GoodsReceipts', N'ReceivedByUserId') IS NULL
			ALTER TABLE GoodsReceipts ADD ReceivedByUserId bigint NULL;

		UPDATE gr
		SET SupplierDeliveryNoteNumber = N'LEGACY-' + gr.ReceiptNumber,
			ReceivedByUserId = COALESCE(
				(SELECT TOP (1) ae.UserId FROM AuditEntries ae INNER JOIN Users auditUser ON auditUser.Id = ae.UserId WHERE ae.EntityType = N'GoodsReceipt' AND ae.EntityId = gr.Id AND ae.UserId IS NOT NULL ORDER BY ae.Id),
				(SELECT TOP (1) Id FROM Users WHERE Email = N'admin@depot.local'),
				(SELECT MIN(Id) FROM Users))
		FROM GoodsReceipts gr
		WHERE gr.SupplierDeliveryNoteNumber IS NULL OR gr.ReceivedByUserId IS NULL;

		ALTER TABLE GoodsReceipts ALTER COLUMN SupplierDeliveryNoteNumber nvarchar(100) NOT NULL;
		ALTER TABLE GoodsReceipts ALTER COLUMN ReceivedByUserId bigint NOT NULL;
		ALTER TABLE GoodsReceipts ALTER COLUMN InvoiceNumber nvarchar(100) NULL;
		ALTER TABLE GoodsReceipts ALTER COLUMN InvoiceDate nvarchar(10) NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_GoodsReceipts_ReceivedByUsers')
			ALTER TABLE GoodsReceipts ADD CONSTRAINT FK_GoodsReceipts_ReceivedByUsers FOREIGN KEY (ReceivedByUserId) REFERENCES Users(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GoodsReceipts_ReceivedByUserId' AND object_id = OBJECT_ID(N'GoodsReceipts'))
			CREATE INDEX IX_GoodsReceipts_ReceivedByUserId ON GoodsReceipts(ReceivedByUserId);
		UPDATE DatabaseInfo SET Version = 17 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateReasonCodesToTechnicalKeys(System.Data.Common.DbCommand command)
	{
		EnsureReasonCodeTechnicalKeys(command);
		command.CommandText = "UPDATE DatabaseInfo SET Version = 16 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void EnsureReasonCodeTechnicalKeys(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'ReasonCodes', N'Code') IS NULL
			ALTER TABLE ReasonCodes ADD Code nvarchar(50) NULL;
		IF COL_LENGTH(N'ReasonCodes', N'IsSystem') IS NULL
			ALTER TABLE ReasonCodes ADD IsSystem bit NOT NULL CONSTRAINT DF_ReasonCodes_IsSystem DEFAULT 0;

		UPDATE ReasonCodes
		SET Code = CASE Name
			WHEN N'Goods Receipt' THEN N'GOODS_RECEIPT'
			WHEN N'Goods Issue' THEN N'GOODS_ISSUE'
			WHEN N'Inventory Correction' THEN N'INVENTORY_CORRECTION'
			WHEN N'Damaged' THEN N'DAMAGED'
			WHEN N'Lost' THEN N'LOST'
			WHEN N'Returned' THEN N'RETURNED'
			WHEN N'Consumed' THEN N'CONSUMED'
			WHEN N'Demo' THEN N'DEMO'
			WHEN N'Repair' THEN N'REPAIR'
			WHEN N'Transfer' THEN N'TRANSFER'
			ELSE N'LEGACY_' + RIGHT(N'000000' + CONVERT(nvarchar(20), Id), 6)
		END
		WHERE Code IS NULL OR LTRIM(RTRIM(Code)) = N'';

		UPDATE ReasonCodes SET IsSystem = 1
		WHERE Code IN
		(
			N'GOODS_RECEIPT', N'GOODS_ISSUE', N'INVENTORY_CORRECTION', N'DAMAGED', N'LOST',
			N'RETURNED', N'CONSUMED', N'DEMO', N'REPAIR', N'TRANSFER'
		);
		ALTER TABLE ReasonCodes ALTER COLUMN Code nvarchar(50) NOT NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ReasonCodes_Code' AND object_id = OBJECT_ID(N'ReasonCodes'))
			CREATE UNIQUE INDEX UX_ReasonCodes_Code ON ReasonCodes(Code);

		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'GOODS_RECEIPT') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'GOODS_RECEIPT', N'Goods Receipt', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'GOODS_ISSUE') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'GOODS_ISSUE', N'Goods Issue', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'INVENTORY_CORRECTION') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'INVENTORY_CORRECTION', N'Inventory Correction', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'DAMAGED') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'DAMAGED', N'Damaged', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'LOST') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'LOST', N'Lost', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'RETURNED') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'RETURNED', N'Returned', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'CONSUMED') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'CONSUMED', N'Consumed', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'DEMO') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'DEMO', N'Demo', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'REPAIR') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'REPAIR', N'Repair', 1, 1);
		IF NOT EXISTS (SELECT 1 FROM ReasonCodes WHERE Code = N'TRANSFER') INSERT INTO ReasonCodes (Code, Name, IsSystem, IsActive) VALUES (N'TRANSFER', N'Transfer', 1, 1);
		UPDATE ReasonCodes SET IsSystem = 1, IsActive = 1 WHERE Code = N'GOODS_RECEIPT';
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToProcurement(System.Data.Common.DbCommand command)
	{
		command.CommandText = ProcurementSql + " UPDATE DatabaseInfo SET Version = 15 WHERE Id = 1;";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateSupplierClassification(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF OBJECT_ID(N'SupplierCategories', N'U') IS NULL CREATE TABLE SupplierCategories (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
		IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'IT Hardware') INSERT INTO SupplierCategories (Name) VALUES (N'IT Hardware');
		IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'ProAV') INSERT INTO SupplierCategories (Name) VALUES (N'ProAV');
		IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'Licensing') INSERT INTO SupplierCategories (Name) VALUES (N'Licensing');
		IF COL_LENGTH(N'Suppliers', N'AccountNumber') IS NULL ALTER TABLE Suppliers ADD AccountNumber bigint NULL;
		IF COL_LENGTH(N'Suppliers', N'SupplierCategoryId') IS NULL ALTER TABLE Suppliers ADD SupplierCategoryId bigint NULL;
		IF COL_LENGTH(N'Suppliers', N'SepaMandate') IS NULL ALTER TABLE Suppliers ADD SepaMandate nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'Quality') IS NULL ALTER TABLE Suppliers ADD Quality int NOT NULL CONSTRAINT DF_Suppliers_Quality DEFAULT 100;
		UPDATE Suppliers SET AccountNumber = Id WHERE AccountNumber IS NULL OR AccountNumber <= 0;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Suppliers_AccountNumber' AND object_id = OBJECT_ID(N'Suppliers')) CREATE UNIQUE INDEX UX_Suppliers_AccountNumber ON Suppliers(AccountNumber);
		IF COL_LENGTH(N'Suppliers', N'CategoryId') IS NOT NULL
		BEGIN
			EXEC(N'INSERT INTO SupplierCategories (Name, Description) SELECT DISTINCT c.Name, c.Description FROM Suppliers s INNER JOIN Categories c ON c.Id = s.CategoryId WHERE s.CategoryId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SupplierCategories sc WHERE sc.Name = c.Name);');
			EXEC(N'UPDATE s SET SupplierCategoryId = sc.Id FROM Suppliers s INNER JOIN Categories c ON c.Id = s.CategoryId INNER JOIN SupplierCategories sc ON sc.Name = c.Name WHERE s.SupplierCategoryId IS NULL;');
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Suppliers_SupplierCategories') ALTER TABLE Suppliers ADD CONSTRAINT FK_Suppliers_SupplierCategories FOREIGN KEY (SupplierCategoryId) REFERENCES SupplierCategories(Id);
		UPDATE DatabaseInfo SET Version = 14 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateSupplierAccountFields(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		DECLARE @HadNumericLoyalty bit = CASE WHEN COL_LENGTH(N'Suppliers', N'Loyalty') IS NULL THEN 0 ELSE 1 END;
		IF COL_LENGTH(N'Suppliers', N'CustomerNumber') IS NULL ALTER TABLE Suppliers ADD CustomerNumber nvarchar(100) NULL;
		IF COL_LENGTH(N'Suppliers', N'Loyalty') IS NULL ALTER TABLE Suppliers ADD Loyalty int NOT NULL CONSTRAINT DF_Suppliers_Loyalty DEFAULT 100;
		IF @HadNumericLoyalty = 0 AND COL_LENGTH(N'Suppliers', N'IsLoyal') IS NOT NULL EXEC(N'UPDATE Suppliers SET Loyalty = CASE WHEN IsLoyal = 1 THEN 100 ELSE 0 END;');
		UPDATE DatabaseInfo SET Version = 13 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToSupplierManagement(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'Suppliers', N'SupplierNumber') IS NULL ALTER TABLE Suppliers ADD SupplierNumber nvarchar(50) NULL;
		IF COL_LENGTH(N'Suppliers', N'Contact') IS NULL ALTER TABLE Suppliers ADD Contact nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'Email') IS NULL ALTER TABLE Suppliers ADD Email nvarchar(320) NULL;
		IF COL_LENGTH(N'Suppliers', N'Phone') IS NULL ALTER TABLE Suppliers ADD Phone nvarchar(100) NULL;
		IF COL_LENGTH(N'Suppliers', N'Address') IS NULL ALTER TABLE Suppliers ADD Address nvarchar(1000) NULL;
		IF COL_LENGTH(N'Suppliers', N'RmaTerms') IS NULL ALTER TABLE Suppliers ADD RmaTerms nvarchar(2000) NULL;
		IF COL_LENGTH(N'Suppliers', N'Url') IS NULL ALTER TABLE Suppliers ADD Url nvarchar(500) NULL;
		IF COL_LENGTH(N'Suppliers', N'PaymentTerm') IS NULL ALTER TABLE Suppliers ADD PaymentTerm nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'Iban') IS NULL ALTER TABLE Suppliers ADD Iban nvarchar(34) NULL;
		IF COL_LENGTH(N'Suppliers', N'AccountName') IS NULL ALTER TABLE Suppliers ADD AccountName nvarchar(200) NULL;
		IF COL_LENGTH(N'Suppliers', N'VatNumber') IS NULL ALTER TABLE Suppliers ADD VatNumber nvarchar(50) NULL;
		IF COL_LENGTH(N'Suppliers', N'CategoryId') IS NULL ALTER TABLE Suppliers ADD CategoryId bigint NULL;
		IF COL_LENGTH(N'Suppliers', N'IsLoyal') IS NULL ALTER TABLE Suppliers ADD IsLoyal bit NOT NULL CONSTRAINT DF_Suppliers_IsLoyal DEFAULT 0;
		IF COL_LENGTH(N'Suppliers', N'Notes') IS NULL ALTER TABLE Suppliers ADD Notes nvarchar(4000) NULL;
		UPDATE Suppliers SET SupplierNumber = N'SUP-' + CONVERT(nvarchar(20), Id) WHERE SupplierNumber IS NULL OR LTRIM(RTRIM(SupplierNumber)) = N'';
		IF COL_LENGTH(N'Suppliers', N'Description') IS NOT NULL EXEC(N'UPDATE Suppliers SET Notes = Description WHERE Notes IS NULL AND Description IS NOT NULL;');
		ALTER TABLE Suppliers ALTER COLUMN SupplierNumber nvarchar(50) NOT NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Suppliers_SupplierNumber' AND object_id = OBJECT_ID(N'Suppliers')) CREATE UNIQUE INDEX UX_Suppliers_SupplierNumber ON Suppliers(SupplierNumber);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Suppliers_Categories') ALTER TABLE Suppliers ADD CONSTRAINT FK_Suppliers_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id);
		IF OBJECT_ID(N'SupplierItems', N'U') IS NULL
		BEGIN
			CREATE TABLE SupplierItems (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierId bigint NOT NULL, ItemId bigint NOT NULL, SupplierPartNumber nvarchar(200) NOT NULL, PurchasePrice decimal(18,2) NOT NULL DEFAULT 0, LeadTimeDays int NOT NULL DEFAULT 0, MinimumOrderQuantity decimal(18,3) NOT NULL DEFAULT 1, IsPreferredSupplier bit NOT NULL DEFAULT 0, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_SupplierItems_Context UNIQUE (SupplierId, ItemId), CONSTRAINT FK_SupplierItems_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_SupplierItems_Items FOREIGN KEY (ItemId) REFERENCES Items(Id));
			CREATE INDEX IX_SupplierItems_SupplierId ON SupplierItems(SupplierId); CREATE INDEX IX_SupplierItems_ItemId ON SupplierItems(ItemId);
		END;
		INSERT INTO SupplierItems (SupplierId, ItemId, SupplierPartNumber, PurchasePrice, LeadTimeDays, MinimumOrderQuantity, IsPreferredSupplier)
		SELECT i.SupplierId, i.Id, i.PartNumber, 0, 0, 1, 1 FROM Items i WHERE i.SupplierId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SupplierItems si WHERE si.SupplierId = i.SupplierId AND si.ItemId = i.Id);
		UPDATE DatabaseInfo SET Version = 12 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToNormalizedItemMasterData(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		INSERT INTO Manufacturers (Name) SELECT DISTINCT LTRIM(RTRIM(i.Manufacturer)) FROM Items i WHERE i.Manufacturer IS NOT NULL AND LTRIM(RTRIM(i.Manufacturer)) <> '' AND NOT EXISTS (SELECT 1 FROM Manufacturers m WHERE m.Name = LTRIM(RTRIM(i.Manufacturer)));
		INSERT INTO Categories (Name) SELECT DISTINCT LTRIM(RTRIM(i.Category)) FROM Items i WHERE i.Category IS NOT NULL AND LTRIM(RTRIM(i.Category)) <> '' AND NOT EXISTS (SELECT 1 FROM Categories c WHERE c.Name = LTRIM(RTRIM(i.Category)));
		IF COL_LENGTH(N'Items', N'ManufacturerId') IS NULL ALTER TABLE Items ADD ManufacturerId bigint NULL;
		IF COL_LENGTH(N'Items', N'CategoryId') IS NULL ALTER TABLE Items ADD CategoryId bigint NULL;
		IF COL_LENGTH(N'Items', N'UnitOfMeasureId') IS NULL ALTER TABLE Items ADD UnitOfMeasureId bigint NULL;
		IF COL_LENGTH(N'Items', N'PackagingId') IS NULL ALTER TABLE Items ADD PackagingId bigint NULL;
		IF COL_LENGTH(N'Items', N'SupplierId') IS NULL ALTER TABLE Items ADD SupplierId bigint NULL;
		UPDATE i SET ManufacturerId = m.Id FROM Items i INNER JOIN Manufacturers m ON m.Name = LTRIM(RTRIM(i.Manufacturer));
		UPDATE i SET CategoryId = c.Id FROM Items i INNER JOIN Categories c ON c.Name = LTRIM(RTRIM(i.Category));
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Manufacturers') ALTER TABLE Items ADD CONSTRAINT FK_Items_Manufacturers FOREIGN KEY (ManufacturerId) REFERENCES Manufacturers(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Categories') ALTER TABLE Items ADD CONSTRAINT FK_Items_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_UnitsOfMeasure') ALTER TABLE Items ADD CONSTRAINT FK_Items_UnitsOfMeasure FOREIGN KEY (UnitOfMeasureId) REFERENCES UnitsOfMeasure(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Packagings') ALTER TABLE Items ADD CONSTRAINT FK_Items_Packagings FOREIGN KEY (PackagingId) REFERENCES Packagings(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Items_Suppliers') ALTER TABLE Items ADD CONSTRAINT FK_Items_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_ManufacturerId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_ManufacturerId ON Items(ManufacturerId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_CategoryId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_CategoryId ON Items(CategoryId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_UnitOfMeasureId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_UnitOfMeasureId ON Items(UnitOfMeasureId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_PackagingId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_PackagingId ON Items(PackagingId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Items_SupplierId' AND object_id = OBJECT_ID(N'Items')) CREATE INDEX IX_Items_SupplierId ON Items(SupplierId);
		UPDATE DatabaseInfo SET Version = 11 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToReasonCodes(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		IF COL_LENGTH(N'StockMovements', N'ReasonCodeId') IS NULL
			ALTER TABLE StockMovements ADD ReasonCodeId bigint NULL;

		IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockMovements_ReasonCodes')
			ALTER TABLE StockMovements ADD CONSTRAINT FK_StockMovements_ReasonCodes
				FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id);

		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovements_ReasonCodeId' AND object_id = OBJECT_ID(N'StockMovements'))
			CREATE INDEX IX_StockMovements_ReasonCodeId ON StockMovements(ReasonCodeId);

		UPDATE DatabaseInfo SET Version = 10 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private static void MigrateToWarehouseStructure(System.Data.Common.DbCommand command)
	{
		command.CommandText =
		"""
		DELETE FROM StorageLocations;
		SET IDENTITY_INSERT StorageLocations ON;
		INSERT INTO StorageLocations (Id, WarehouseId, Name, Description, IsActive, Version)
		SELECT l.Id, w.Id, l.Name, l.Description, l.IsActive, l.Version
		FROM Locations l
		CROSS JOIN Warehouses w
		WHERE w.Name = N'Main Warehouse';
		SET IDENTITY_INSERT StorageLocations OFF;

		ALTER TABLE Inventories ADD StorageLocationId bigint NULL;
		UPDATE Inventories SET StorageLocationId = LocationId;
		ALTER TABLE Inventories ALTER COLUMN StorageLocationId bigint NOT NULL;
		ALTER TABLE Inventories DROP CONSTRAINT FK_Inventories_Locations;
		ALTER TABLE Inventories DROP CONSTRAINT UQ_Inventories_Context;
		ALTER TABLE Inventories DROP COLUMN LocationId;
		ALTER TABLE Inventories ADD CONSTRAINT UQ_Inventories_Context UNIQUE (ItemId, PurposeId, StorageLocationId);
		ALTER TABLE Inventories ADD CONSTRAINT FK_Inventories_StorageLocations
			FOREIGN KEY (StorageLocationId) REFERENCES StorageLocations(Id);
		DROP TABLE Locations;
		UPDATE DatabaseInfo SET Version = 9 WHERE Id = 1;
		""";
		command.Parameters.Clear();
		command.ExecuteNonQuery();
	}

	private void EnsureDatabaseExists()
	{
		using var connection = _connectionFactory.CreateMasterConnection();
		connection.Open();
		using var existsCommand = connection.CreateCommand();
		existsCommand.CommandText = "SELECT DB_ID(@DatabaseName);";
		existsCommand.Parameters.AddWithValue("@DatabaseName", _connectionFactory.DatabaseName);
		var databaseId = existsCommand.ExecuteScalar();
		if (databaseId is not null and not DBNull)
		{
			return;
		}

		var escapedName = _connectionFactory.DatabaseName.Replace("]", "]]", StringComparison.Ordinal);
		using var createCommand = connection.CreateCommand();
		createCommand.CommandText = $"CREATE DATABASE [{escapedName}];";
		createCommand.ExecuteNonQuery();
	}

	private const string ProcurementSql =
	"""
	IF OBJECT_ID(N'PurchaseOrders', N'U') IS NULL
	BEGIN
		CREATE TABLE PurchaseOrders (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, OrderNumber nvarchar(50) NOT NULL UNIQUE, SupplierId bigint NOT NULL, OrderDate nvarchar(10) NOT NULL, ExpectedDeliveryDate nvarchar(10) NULL, Notes nvarchar(4000) NULL, Status int NOT NULL DEFAULT 1, CreatedByUserId bigint NULL, SubmittedByUserId bigint NULL, SubmittedAtUtc nvarchar(40) NULL, ApprovalDecisionByUserId bigint NULL, ApprovalDecisionAtUtc nvarchar(40) NULL, ApprovalComment nvarchar(2000) NULL, ClosedByUserId bigint NULL, ClosedAtUtc nvarchar(40) NULL, CloseReason nvarchar(2000) NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_PurchaseOrders_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_PurchaseOrders_CreatedByUsers FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id), CONSTRAINT FK_PurchaseOrders_SubmittedByUsers FOREIGN KEY (SubmittedByUserId) REFERENCES Users(Id), CONSTRAINT FK_PurchaseOrders_ApprovalDecisionByUsers FOREIGN KEY (ApprovalDecisionByUserId) REFERENCES Users(Id), CONSTRAINT FK_PurchaseOrders_ClosedByUsers FOREIGN KEY (ClosedByUserId) REFERENCES Users(Id));
		CREATE INDEX IX_PurchaseOrders_SupplierId_Status ON PurchaseOrders(SupplierId, Status); CREATE INDEX IX_PurchaseOrders_OrderDate ON PurchaseOrders(OrderDate);
	END;
	IF OBJECT_ID(N'PurchaseOrderLines', N'U') IS NULL
	BEGIN
		CREATE TABLE PurchaseOrderLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, PurchaseOrderId bigint NOT NULL, LineNumber int NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, UnitPrice decimal(18,2) NOT NULL DEFAULT 0, ReceivedQuantity int NOT NULL DEFAULT 0, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_PurchaseOrderLines_Number UNIQUE (PurchaseOrderId, LineNumber), CONSTRAINT UQ_PurchaseOrderLines_Item UNIQUE (PurchaseOrderId, ItemId), CONSTRAINT CK_PurchaseOrderLines_Quantity CHECK (Quantity > 0 AND ReceivedQuantity >= 0 AND ReceivedQuantity <= Quantity), CONSTRAINT FK_PurchaseOrderLines_Orders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT FK_PurchaseOrderLines_Items FOREIGN KEY (ItemId) REFERENCES Items(Id));
		CREATE INDEX IX_PurchaseOrderLines_ItemId ON PurchaseOrderLines(ItemId);
	END;
	IF OBJECT_ID(N'GoodsReceipts', N'U') IS NULL
	BEGIN
		CREATE TABLE GoodsReceipts (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, ReceiptNumber nvarchar(50) NOT NULL UNIQUE, PurchaseOrderId bigint NOT NULL, ReceiptDate nvarchar(10) NOT NULL, SupplierDeliveryNoteNumber nvarchar(100) NOT NULL, ReceivedByUserId bigint NOT NULL, InvoiceNumber nvarchar(100) NULL, InvoiceDate nvarchar(10) NULL, InvoiceDocumentPath nvarchar(1000) NULL, Notes nvarchar(4000) NULL, ReversedAtUtc nvarchar(40) NULL, ReversedByUserId bigint NULL, ReversalReason nvarchar(1000) NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_GoodsReceipts_Orders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT FK_GoodsReceipts_ReceivedByUsers FOREIGN KEY (ReceivedByUserId) REFERENCES Users(Id), CONSTRAINT FK_GoodsReceipts_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id));
		CREATE INDEX IX_GoodsReceipts_PurchaseOrderId ON GoodsReceipts(PurchaseOrderId);
		CREATE INDEX IX_GoodsReceipts_ReceivedByUserId ON GoodsReceipts(ReceivedByUserId);
	END;
	IF OBJECT_ID(N'GoodsReceiptLines', N'U') IS NULL
	BEGIN
		CREATE TABLE GoodsReceiptLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, GoodsReceiptId bigint NOT NULL, PurchaseOrderLineId bigint NOT NULL, InventoryId bigint NOT NULL, Quantity int NOT NULL, CONSTRAINT UQ_GoodsReceiptLines_OrderLine UNIQUE (GoodsReceiptId, PurchaseOrderLineId), CONSTRAINT CK_GoodsReceiptLines_Quantity CHECK (Quantity > 0), CONSTRAINT FK_GoodsReceiptLines_Receipts FOREIGN KEY (GoodsReceiptId) REFERENCES GoodsReceipts(Id), CONSTRAINT FK_GoodsReceiptLines_OrderLines FOREIGN KEY (PurchaseOrderLineId) REFERENCES PurchaseOrderLines(Id), CONSTRAINT FK_GoodsReceiptLines_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id));
		CREATE INDEX IX_GoodsReceiptLines_InventoryId ON GoodsReceiptLines(InventoryId);
	END;
	""";

	private const string StockTransferSql =
	"""
	IF OBJECT_ID(N'StockTransfers', N'U') IS NULL
	BEGIN
		CREATE TABLE StockTransfers
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
			TransferNumber nvarchar(50) NOT NULL UNIQUE,
			SourceWarehouseId bigint NOT NULL,
			DestinationWarehouseId bigint NOT NULL,
			TransferDate nvarchar(10) NOT NULL,
			Status int NOT NULL DEFAULT 1,
			CreatedByUserId bigint NOT NULL,
			PostedByUserId bigint NULL,
			Notes nvarchar(4000) NULL,
			ReversedAtUtc nvarchar(40) NULL,
			ReversedByUserId bigint NULL,
			ReversalReason nvarchar(1000) NULL,
			Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT CK_StockTransfers_Warehouses CHECK (SourceWarehouseId <> DestinationWarehouseId),
			CONSTRAINT CK_StockTransfers_Status CHECK (Status IN (1, 2, 3)),
			CONSTRAINT FK_StockTransfers_SourceWarehouses FOREIGN KEY (SourceWarehouseId) REFERENCES Warehouses(Id),
			CONSTRAINT FK_StockTransfers_DestinationWarehouses FOREIGN KEY (DestinationWarehouseId) REFERENCES Warehouses(Id),
			CONSTRAINT FK_StockTransfers_CreatedByUsers FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_StockTransfers_PostedByUsers FOREIGN KEY (PostedByUserId) REFERENCES Users(Id)
			,CONSTRAINT FK_StockTransfers_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id)
		);
		CREATE INDEX IX_StockTransfers_SourceWarehouseId_Status ON StockTransfers(SourceWarehouseId, Status);
		CREATE INDEX IX_StockTransfers_DestinationWarehouseId_Status ON StockTransfers(DestinationWarehouseId, Status);
		CREATE INDEX IX_StockTransfers_TransferDate ON StockTransfers(TransferDate);
	END;
	IF OBJECT_ID(N'StockTransferLines', N'U') IS NULL
	BEGIN
		CREATE TABLE StockTransferLines
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
			StockTransferId bigint NOT NULL,
			LineNumber int NOT NULL,
			SourceInventoryId bigint NOT NULL,
			DestinationInventoryId bigint NOT NULL,
			Quantity int NOT NULL,
			Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT UQ_StockTransferLines_Number UNIQUE (StockTransferId, LineNumber),
			CONSTRAINT UQ_StockTransferLines_InventoryPair UNIQUE (StockTransferId, SourceInventoryId, DestinationInventoryId),
			CONSTRAINT CK_StockTransferLines_Quantity CHECK (Quantity > 0),
			CONSTRAINT CK_StockTransferLines_Inventories CHECK (SourceInventoryId <> DestinationInventoryId),
			CONSTRAINT FK_StockTransferLines_Transfers FOREIGN KEY (StockTransferId) REFERENCES StockTransfers(Id),
			CONSTRAINT FK_StockTransferLines_SourceInventories FOREIGN KEY (SourceInventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_StockTransferLines_DestinationInventories FOREIGN KEY (DestinationInventoryId) REFERENCES Inventories(Id)
		);
		CREATE INDEX IX_StockTransferLines_SourceInventoryId ON StockTransferLines(SourceInventoryId);
		CREATE INDEX IX_StockTransferLines_DestinationInventoryId ON StockTransferLines(DestinationInventoryId);
	END;
	""";

	private const string InventoryCountSql =
	"""
	IF OBJECT_ID(N'InventoryCounts', N'U') IS NULL
	BEGIN
		CREATE TABLE InventoryCounts
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
			CountNumber nvarchar(50) NOT NULL UNIQUE,
			WarehouseId bigint NOT NULL,
			Status int NOT NULL DEFAULT 1,
			CreatedAtUtc nvarchar(40) NOT NULL,
			StartedAtUtc nvarchar(40) NULL,
			CompletedAtUtc nvarchar(40) NULL,
			CreatedByUserId bigint NOT NULL,
			PostedByUserId bigint NULL,
			Notes nvarchar(4000) NULL,
			ReversedAtUtc nvarchar(40) NULL,
			ReversedByUserId bigint NULL,
			ReversalReason nvarchar(1000) NULL,
			Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT CK_InventoryCounts_Status CHECK (Status IN (1, 2, 3, 4, 5)),
			CONSTRAINT FK_InventoryCounts_Warehouses FOREIGN KEY (WarehouseId) REFERENCES Warehouses(Id),
			CONSTRAINT FK_InventoryCounts_CreatedByUsers FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_InventoryCounts_PostedByUsers FOREIGN KEY (PostedByUserId) REFERENCES Users(Id)
			,CONSTRAINT FK_InventoryCounts_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id)
		);
		CREATE INDEX IX_InventoryCounts_WarehouseId_Status ON InventoryCounts(WarehouseId, Status);
		CREATE INDEX IX_InventoryCounts_CreatedAtUtc ON InventoryCounts(CreatedAtUtc);
	END;
	IF OBJECT_ID(N'InventoryCountLines', N'U') IS NULL
	BEGIN
		CREATE TABLE InventoryCountLines
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
			InventoryCountId bigint NOT NULL,
			InventoryId bigint NOT NULL,
			ExpectedQuantity bigint NOT NULL,
			CountedQuantity bigint NULL,
			CountedByUserId bigint NULL,
			CountedAtUtc nvarchar(40) NULL,
			Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT UQ_InventoryCountLines_Inventory UNIQUE (InventoryCountId, InventoryId),
			CONSTRAINT CK_InventoryCountLines_CountedQuantity CHECK (CountedQuantity IS NULL OR CountedQuantity >= 0),
			CONSTRAINT FK_InventoryCountLines_Counts FOREIGN KEY (InventoryCountId) REFERENCES InventoryCounts(Id),
			CONSTRAINT FK_InventoryCountLines_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_InventoryCountLines_CountedByUsers FOREIGN KEY (CountedByUserId) REFERENCES Users(Id)
		);
		CREATE INDEX IX_InventoryCountLines_InventoryId ON InventoryCountLines(InventoryId);
	END;
	""";

	private const string MaterialIssueSql =
	"""
	IF OBJECT_ID(N'MaterialIssues', N'U') IS NULL
	BEGIN
		CREATE TABLE MaterialIssues
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, IssueNumber nvarchar(50) NOT NULL UNIQUE,
			IssueDate nvarchar(10) NOT NULL, Status int NOT NULL DEFAULT 1, Recipient nvarchar(250) NOT NULL,
			Reference nvarchar(250) NULL, Notes nvarchar(4000) NULL, CreatedByUserId bigint NOT NULL,
			PostedByUserId bigint NULL, PostedAtUtc nvarchar(40) NULL, ReversedByUserId bigint NULL,
			ReversedAtUtc nvarchar(40) NULL, ReversalReason nvarchar(1000) NULL, Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT CK_MaterialIssues_Status CHECK (Status IN (1, 2, 3, 4)),
			CONSTRAINT FK_MaterialIssues_CreatedByUsers FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_MaterialIssues_PostedByUsers FOREIGN KEY (PostedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_MaterialIssues_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id)
		);
		CREATE INDEX IX_MaterialIssues_IssueDate ON MaterialIssues(IssueDate);
		CREATE INDEX IX_MaterialIssues_Status ON MaterialIssues(Status);
	END;
	IF OBJECT_ID(N'MaterialIssueLines', N'U') IS NULL
	BEGIN
		CREATE TABLE MaterialIssueLines
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, MaterialIssueId bigint NOT NULL, LineNumber int NOT NULL,
			InventoryId bigint NOT NULL, Quantity int NOT NULL, ReasonCodeId bigint NOT NULL, Notes nvarchar(2000) NULL,
			Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT UQ_MaterialIssueLines_Number UNIQUE (MaterialIssueId, LineNumber),
			CONSTRAINT UQ_MaterialIssueLines_Inventory UNIQUE (MaterialIssueId, InventoryId),
			CONSTRAINT CK_MaterialIssueLines_Quantity CHECK (Quantity > 0),
			CONSTRAINT FK_MaterialIssueLines_Issues FOREIGN KEY (MaterialIssueId) REFERENCES MaterialIssues(Id),
			CONSTRAINT FK_MaterialIssueLines_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_MaterialIssueLines_ReasonCodes FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id)
		);
		CREATE INDEX IX_MaterialIssueLines_InventoryId ON MaterialIssueLines(InventoryId);
		CREATE INDEX IX_MaterialIssueLines_ReasonCodeId ON MaterialIssueLines(ReasonCodeId);
	END;
	""";

	private const string MaterialReturnSql =
	"""
	IF OBJECT_ID(N'MaterialReturns', N'U') IS NULL
	BEGIN
		CREATE TABLE MaterialReturns
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, ReturnNumber nvarchar(50) NOT NULL UNIQUE,
			ReturnDate nvarchar(10) NOT NULL, Status int NOT NULL DEFAULT 1, RecipientOrSource nvarchar(250) NOT NULL,
			OriginalMaterialIssueId bigint NULL, Reference nvarchar(250) NULL, Notes nvarchar(4000) NULL,
			CreatedByUserId bigint NOT NULL, PostedByUserId bigint NULL, PostedAtUtc nvarchar(40) NULL,
			Version bigint NOT NULL DEFAULT 1, CONSTRAINT CK_MaterialReturns_Status CHECK (Status IN (1, 2, 3)),
			CONSTRAINT FK_MaterialReturns_OriginalIssues FOREIGN KEY (OriginalMaterialIssueId) REFERENCES MaterialIssues(Id),
			CONSTRAINT FK_MaterialReturns_CreatedByUsers FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_MaterialReturns_PostedByUsers FOREIGN KEY (PostedByUserId) REFERENCES Users(Id)
		);
		CREATE INDEX IX_MaterialReturns_ReturnDate ON MaterialReturns(ReturnDate);
		CREATE INDEX IX_MaterialReturns_Status ON MaterialReturns(Status);
		CREATE INDEX IX_MaterialReturns_OriginalMaterialIssueId ON MaterialReturns(OriginalMaterialIssueId);
	END;
	IF OBJECT_ID(N'MaterialReturnLines', N'U') IS NULL
	BEGIN
		CREATE TABLE MaterialReturnLines
		(
			Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, MaterialReturnId bigint NOT NULL, LineNumber int NOT NULL,
			InventoryId bigint NOT NULL, Quantity int NOT NULL, ReasonCodeId bigint NOT NULL, Notes nvarchar(2000) NULL,
			Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT UQ_MaterialReturnLines_Number UNIQUE (MaterialReturnId, LineNumber),
			CONSTRAINT UQ_MaterialReturnLines_Inventory UNIQUE (MaterialReturnId, InventoryId),
			CONSTRAINT CK_MaterialReturnLines_Quantity CHECK (Quantity > 0),
			CONSTRAINT FK_MaterialReturnLines_Returns FOREIGN KEY (MaterialReturnId) REFERENCES MaterialReturns(Id),
			CONSTRAINT FK_MaterialReturnLines_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_MaterialReturnLines_ReasonCodes FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id)
		);
		CREATE INDEX IX_MaterialReturnLines_InventoryId ON MaterialReturnLines(InventoryId);
		CREATE INDEX IX_MaterialReturnLines_ReasonCodeId ON MaterialReturnLines(ReasonCodeId);
	END;
	""";

	private const string SupplierReturnSql =
	"""
	IF OBJECT_ID(N'SupplierReturns', N'U') IS NULL
	BEGIN
		CREATE TABLE SupplierReturns (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, ReturnNumber nvarchar(50) NOT NULL UNIQUE, SupplierId bigint NOT NULL, ReturnDate nvarchar(10) NOT NULL, Status int NOT NULL DEFAULT 1, PurchaseOrderId bigint NOT NULL, GoodsReceiptId bigint NOT NULL, SupplierReference nvarchar(250) NULL, Notes nvarchar(4000) NULL, CreatedByUserId bigint NOT NULL, PostedByUserId bigint NULL, PostedAtUtc nvarchar(40) NULL, ReversedByUserId bigint NULL, ReversedAtUtc nvarchar(40) NULL, ReversalReason nvarchar(1000) NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT CK_SupplierReturns_Status CHECK (Status IN (1,2,3)), CONSTRAINT FK_SupplierReturns_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_SupplierReturns_Orders FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT FK_SupplierReturns_Receipts FOREIGN KEY (GoodsReceiptId) REFERENCES GoodsReceipts(Id), CONSTRAINT FK_SupplierReturns_CreatedByUsers FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id), CONSTRAINT FK_SupplierReturns_PostedByUsers FOREIGN KEY (PostedByUserId) REFERENCES Users(Id), CONSTRAINT FK_SupplierReturns_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id));
		CREATE INDEX IX_SupplierReturns_SupplierId_Status ON SupplierReturns(SupplierId, Status); CREATE INDEX IX_SupplierReturns_ReturnDate ON SupplierReturns(ReturnDate); CREATE INDEX IX_SupplierReturns_GoodsReceiptId ON SupplierReturns(GoodsReceiptId);
	END;
	IF OBJECT_ID(N'SupplierReturnLines', N'U') IS NULL
	BEGIN
		CREATE TABLE SupplierReturnLines (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierReturnId bigint NOT NULL, InventoryId bigint NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, UnitCost decimal(18,2) NOT NULL, ReasonCodeId bigint NOT NULL, GoodsReceiptLineId bigint NOT NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_SupplierReturnLines_ReceiptLine UNIQUE (SupplierReturnId, GoodsReceiptLineId), CONSTRAINT CK_SupplierReturnLines_QuantityCost CHECK (Quantity > 0 AND UnitCost >= 0), CONSTRAINT FK_SupplierReturnLines_Returns FOREIGN KEY (SupplierReturnId) REFERENCES SupplierReturns(Id), CONSTRAINT FK_SupplierReturnLines_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id), CONSTRAINT FK_SupplierReturnLines_Items FOREIGN KEY (ItemId) REFERENCES Items(Id), CONSTRAINT FK_SupplierReturnLines_Reasons FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id), CONSTRAINT FK_SupplierReturnLines_ReceiptLines FOREIGN KEY (GoodsReceiptLineId) REFERENCES GoodsReceiptLines(Id));
		CREATE INDEX IX_SupplierReturnLines_InventoryId ON SupplierReturnLines(InventoryId); CREATE INDEX IX_SupplierReturnLines_GoodsReceiptLineId ON SupplierReturnLines(GoodsReceiptLineId);
	END;
	""";

	private const string SchemaSql =
	"""
	IF OBJECT_ID(N'DatabaseInfo', N'U') IS NULL
	BEGIN
		CREATE TABLE DatabaseInfo
		(
			Id int NOT NULL CONSTRAINT PK_DatabaseInfo PRIMARY KEY,
			Version int NOT NULL,
			CONSTRAINT CK_DatabaseInfo_SingleRow CHECK (Id = 1)
		);
		INSERT INTO DatabaseInfo (Id, Version) VALUES (1, @CurrentVersion);
	END;

	IF OBJECT_ID(N'Manufacturers', N'U') IS NULL CREATE TABLE Manufacturers (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'Categories', N'U') IS NULL CREATE TABLE Categories (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'UnitsOfMeasure', N'U') IS NULL CREATE TABLE UnitsOfMeasure (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'Packagings', N'U') IS NULL CREATE TABLE Packagings (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF OBJECT_ID(N'SupplierCategories', N'U') IS NULL CREATE TABLE SupplierCategories (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL UNIQUE, Description nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1);
	IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'IT Hardware') INSERT INTO SupplierCategories (Name) VALUES (N'IT Hardware');
	IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'ProAV') INSERT INTO SupplierCategories (Name) VALUES (N'ProAV');
	IF NOT EXISTS (SELECT 1 FROM SupplierCategories WHERE Name = N'Licensing') INSERT INTO SupplierCategories (Name) VALUES (N'Licensing');
	IF OBJECT_ID(N'Suppliers', N'U') IS NULL CREATE TABLE Suppliers (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierNumber nvarchar(50) NOT NULL UNIQUE, AccountNumber bigint NOT NULL UNIQUE, CustomerNumber nvarchar(100) NULL, Name nvarchar(200) NOT NULL UNIQUE, Contact nvarchar(200) NULL, Email nvarchar(320) NULL, Phone nvarchar(100) NULL, Address nvarchar(1000) NULL, RmaTerms nvarchar(2000) NULL, Url nvarchar(500) NULL, PaymentTerm nvarchar(200) NULL, Iban nvarchar(34) NULL, AccountName nvarchar(200) NULL, SepaMandate nvarchar(200) NULL, VatNumber nvarchar(50) NULL, SupplierCategoryId bigint NULL, Loyalty int NOT NULL DEFAULT 100, Quality int NOT NULL DEFAULT 100, Notes nvarchar(4000) NULL, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_Suppliers_SupplierCategories FOREIGN KEY (SupplierCategoryId) REFERENCES SupplierCategories(Id));

	IF OBJECT_ID(N'Items', N'U') IS NULL
	BEGIN
		CREATE TABLE Items
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Items PRIMARY KEY,
			PartNumber nvarchar(200) NOT NULL CONSTRAINT UQ_Items_PartNumber UNIQUE,
			Description nvarchar(500) NOT NULL,
			Manufacturer nvarchar(200) NULL,
			Category nvarchar(200) NULL,
			ManufacturerId bigint NULL,
			CategoryId bigint NULL,
			UnitOfMeasureId bigint NULL,
			PackagingId bigint NULL,
			SupplierId bigint NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Items_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Items_Version DEFAULT 1,
			CONSTRAINT FK_Items_Manufacturers FOREIGN KEY (ManufacturerId) REFERENCES Manufacturers(Id),
			CONSTRAINT FK_Items_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id),
			CONSTRAINT FK_Items_UnitsOfMeasure FOREIGN KEY (UnitOfMeasureId) REFERENCES UnitsOfMeasure(Id),
			CONSTRAINT FK_Items_Packagings FOREIGN KEY (PackagingId) REFERENCES Packagings(Id),
			CONSTRAINT FK_Items_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
		);
		CREATE INDEX IX_Items_ManufacturerId ON Items(ManufacturerId);
		CREATE INDEX IX_Items_CategoryId ON Items(CategoryId);
		CREATE INDEX IX_Items_UnitOfMeasureId ON Items(UnitOfMeasureId);
		CREATE INDEX IX_Items_PackagingId ON Items(PackagingId);
		CREATE INDEX IX_Items_SupplierId ON Items(SupplierId);
	END;

	IF OBJECT_ID(N'SupplierItems', N'U') IS NULL
	BEGIN
		CREATE TABLE SupplierItems (Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, SupplierId bigint NOT NULL, ItemId bigint NOT NULL, SupplierPartNumber nvarchar(200) NOT NULL, PurchasePrice decimal(18,2) NOT NULL DEFAULT 0, LeadTimeDays int NOT NULL DEFAULT 0, MinimumOrderQuantity decimal(18,3) NOT NULL DEFAULT 1, IsPreferredSupplier bit NOT NULL DEFAULT 0, IsActive bit NOT NULL DEFAULT 1, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_SupplierItems_Context UNIQUE (SupplierId, ItemId), CONSTRAINT FK_SupplierItems_Suppliers FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_SupplierItems_Items FOREIGN KEY (ItemId) REFERENCES Items(Id));
		CREATE INDEX IX_SupplierItems_SupplierId ON SupplierItems(SupplierId); CREATE INDEX IX_SupplierItems_ItemId ON SupplierItems(ItemId);
	END;

	IF OBJECT_ID(N'Purposes', N'U') IS NULL
		CREATE TABLE Purposes
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Purposes PRIMARY KEY,
			Name nvarchar(200) NOT NULL CONSTRAINT UQ_Purposes_Name UNIQUE,
			Description nvarchar(500) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Purposes_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Purposes_Version DEFAULT 1
		);

	IF OBJECT_ID(N'Warehouses', N'U') IS NULL
		CREATE TABLE Warehouses
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
			Name nvarchar(200) NOT NULL CONSTRAINT UQ_Warehouses_Name UNIQUE,
			Description nvarchar(500) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Warehouses_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Warehouses_Version DEFAULT 1
		);

	IF OBJECT_ID(N'ReasonCodes', N'U') IS NULL
		CREATE TABLE ReasonCodes
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReasonCodes PRIMARY KEY,
			Code nvarchar(50) NULL,
			Name nvarchar(200) NOT NULL CONSTRAINT UQ_ReasonCodes_Name UNIQUE,
			Description nvarchar(500) NULL,
			IsSystem bit NOT NULL CONSTRAINT DF_ReasonCodes_IsSystem DEFAULT 0,
			IsActive bit NOT NULL CONSTRAINT DF_ReasonCodes_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_ReasonCodes_Version DEFAULT 1
		);

	IF OBJECT_ID(N'StorageLocations', N'U') IS NULL
		CREATE TABLE StorageLocations
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StorageLocations PRIMARY KEY,
			WarehouseId bigint NOT NULL,
			Name nvarchar(200) NOT NULL,
			Description nvarchar(500) NULL,
			IsActive bit NOT NULL CONSTRAINT DF_StorageLocations_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_StorageLocations_Version DEFAULT 1,
			CONSTRAINT UQ_StorageLocations_Warehouse_Name UNIQUE (WarehouseId, Name),
			CONSTRAINT FK_StorageLocations_Warehouses FOREIGN KEY (WarehouseId) REFERENCES Warehouses(Id)
		);

	IF OBJECT_ID(N'Inventories', N'U') IS NULL
		CREATE TABLE Inventories
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Inventories PRIMARY KEY,
			ItemId bigint NOT NULL,
			PurposeId bigint NOT NULL,
			StorageLocationId bigint NOT NULL,
			IsActive bit NOT NULL CONSTRAINT DF_Inventories_IsActive DEFAULT 1,
			Version bigint NOT NULL CONSTRAINT DF_Inventories_Version DEFAULT 1,
			CONSTRAINT UQ_Inventories_Context UNIQUE (ItemId, PurposeId, StorageLocationId),
			CONSTRAINT FK_Inventories_Items FOREIGN KEY (ItemId) REFERENCES Items(Id),
			CONSTRAINT FK_Inventories_Purposes FOREIGN KEY (PurposeId) REFERENCES Purposes(Id),
			CONSTRAINT FK_Inventories_StorageLocations FOREIGN KEY (StorageLocationId) REFERENCES StorageLocations(Id)
		);

	IF OBJECT_ID(N'Users', N'U') IS NULL
		CREATE TABLE Users
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
			Email nvarchar(320) NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
			DisplayName nvarchar(200) NOT NULL,
			PasswordHash nvarchar(500) NOT NULL,
			IsAdministrator bit NOT NULL CONSTRAINT DF_Users_IsAdministrator DEFAULT 0,
			CanApprovePurchaseOrders bit NOT NULL CONSTRAINT DF_Users_CanApprovePurchaseOrders DEFAULT 0,
			Role int NOT NULL CONSTRAINT DF_Users_Role DEFAULT 0 CONSTRAINT CK_Users_Role CHECK (Role IN (0,1,2,3,4)),
			IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
			CreatedUtc nvarchar(40) NOT NULL,
			Version bigint NOT NULL CONSTRAINT DF_Users_Version DEFAULT 1
		);

	IF OBJECT_ID(N'StockMovements', N'U') IS NULL
		CREATE TABLE StockMovements
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
			InventoryId bigint NOT NULL,
			ReasonCodeId bigint NULL,
			MovementType int NOT NULL,
			TimestampUtc nvarchar(40) NOT NULL,
			Quantity int NOT NULL,
			UnitPrice decimal(18,2) NULL,
			Reference nvarchar(200) NULL,
			Notes nvarchar(2000) NULL,
			ReversalOfMovementId bigint NULL,
			ReversalReason nvarchar(1000) NULL,
			ReversedAtUtc nvarchar(40) NULL,
			ReversedByUserId bigint NULL,
			CONSTRAINT FK_StockMovements_Inventories FOREIGN KEY (InventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_StockMovements_ReasonCodes FOREIGN KEY (ReasonCodeId) REFERENCES ReasonCodes(Id),
			CONSTRAINT FK_StockMovements_ReversalOfMovement FOREIGN KEY (ReversalOfMovementId) REFERENCES StockMovements(Id),
			CONSTRAINT FK_StockMovements_ReversedByUsers FOREIGN KEY (ReversedByUserId) REFERENCES Users(Id)
		);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovements_InventoryId_TimestampUtc')
		CREATE INDEX IX_StockMovements_InventoryId_TimestampUtc ON StockMovements(InventoryId, TimestampUtc);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovements_ReasonCodeId')
		CREATE INDEX IX_StockMovements_ReasonCodeId ON StockMovements(ReasonCodeId);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_StockMovements_ReversalOfMovementId')
		CREATE UNIQUE INDEX UX_StockMovements_ReversalOfMovementId ON StockMovements(ReversalOfMovementId) WHERE ReversalOfMovementId IS NOT NULL;

	IF OBJECT_ID(N'AuditEntries', N'U') IS NULL
		CREATE TABLE AuditEntries
		(
			Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditEntries PRIMARY KEY,
			TimestampUtc nvarchar(40) NOT NULL,
			UserId bigint NULL,
			UserEmail nvarchar(320) NOT NULL,
			EntityType nvarchar(200) NOT NULL,
			EntityId bigint NOT NULL,
			Action nvarchar(100) NOT NULL,
			BeforeJson nvarchar(max) NULL,
			AfterJson nvarchar(max) NULL,
			CONSTRAINT FK_AuditEntries_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL
		);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditEntries_TimestampUtc')
		CREATE INDEX IX_AuditEntries_TimestampUtc ON AuditEntries(TimestampUtc DESC);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditEntries_Entity')
		CREATE INDEX IX_AuditEntries_Entity ON AuditEntries(EntityType, EntityId, TimestampUtc DESC);

	IF NOT EXISTS (SELECT 1 FROM Purposes WHERE Name = N'Stock')
		INSERT INTO Purposes (Name, Description, IsActive) VALUES (N'Stock', N'Default stock purpose', 1);

	IF NOT EXISTS (SELECT 1 FROM Warehouses WHERE Name = N'Main Warehouse')
		INSERT INTO Warehouses (Name, Description, IsActive) VALUES (N'Main Warehouse', N'Default Depot warehouse', 1);

	IF NOT EXISTS (SELECT 1 FROM StorageLocations sl INNER JOIN Warehouses w ON w.Id = sl.WarehouseId WHERE w.Name = N'Main Warehouse' AND sl.Name = N'Default')
		INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive)
		SELECT Id, N'Default', N'Default storage location', 1 FROM Warehouses WHERE Name = N'Main Warehouse';

	IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = N'admin@depot.local')
		INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, IsActive, CreatedUtc)
		VALUES
		(N'admin@depot.local', N'Administrator', @DefaultPasswordHash, 1, 1, CONVERT(nvarchar(40), SYSUTCDATETIME(), 127));
	""";
}
