// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

public static class ProductionSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection=connectionFactory.CreateConnection();
		connection.Open();
		using var command=connection.CreateCommand();
		command.CommandText=connectionFactory.Provider switch
		{
			DatabaseProvider.Local=>Sqlite,
			DatabaseProvider.SqlServer=>SqlServer,
			DatabaseProvider.MySql=>MySql,
			_=>throw new NotSupportedException($"Production persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private const string Sqlite="""
		CREATE TABLE IF NOT EXISTS ProductionBillsOfMaterial (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			FinishedItemId INTEGER NOT NULL,
			Revision TEXT NOT NULL,
			Status INTEGER NOT NULL,
			EffectiveFromUtc TEXT NULL,
			EffectiveUntilUtc TEXT NULL,
			Version INTEGER NOT NULL DEFAULT 1,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			UNIQUE(FinishedItemId, Revision),
			FOREIGN KEY(FinishedItemId) REFERENCES Items(Id),
			FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			CHECK(Status BETWEEN 1 AND 3));
		CREATE INDEX IF NOT EXISTS IX_ProductionBoms_Item_Status ON ProductionBillsOfMaterial(FinishedItemId, Status, EffectiveFromUtc, EffectiveUntilUtc);
		CREATE TABLE IF NOT EXISTS ProductionBillOfMaterialLines (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			BillOfMaterialId INTEGER NOT NULL,
			ComponentItemId INTEGER NOT NULL,
			Quantity NUMERIC NOT NULL,
			Sequence INTEGER NOT NULL,
			UNIQUE(BillOfMaterialId, ComponentItemId),
			FOREIGN KEY(BillOfMaterialId) REFERENCES ProductionBillsOfMaterial(Id) ON DELETE CASCADE,
			FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
			CHECK(Quantity > 0),
			CHECK(Sequence >= 0));
		CREATE INDEX IF NOT EXISTS IX_ProductionBomLines_Component ON ProductionBillOfMaterialLines(ComponentItemId, BillOfMaterialId);
		CREATE TABLE IF NOT EXISTS ProductionOrders (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			OrderNumber TEXT NOT NULL UNIQUE,
			FinishedItemId INTEGER NOT NULL,
			BillOfMaterialId INTEGER NOT NULL,
			BillOfMaterialRevision TEXT NOT NULL,
			PlannedQuantity INTEGER NOT NULL,
			CompletedQuantity INTEGER NOT NULL DEFAULT 0,
			WarehouseId INTEGER NOT NULL,
			FinishedInventoryId INTEGER NOT NULL,
			Status INTEGER NOT NULL,
			OwnerUserId INTEGER NULL,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			ReleasedAtUtc TEXT NULL,
			ReleasedByUserId INTEGER NULL,
			CompletedAtUtc TEXT NULL,
			CompletedByUserId INTEGER NULL,
			ReversedAtUtc TEXT NULL,
			ReversedByUserId INTEGER NULL,
			Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(FinishedItemId) REFERENCES Items(Id),
			FOREIGN KEY(BillOfMaterialId) REFERENCES ProductionBillsOfMaterial(Id),
			FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id),
			FOREIGN KEY(FinishedInventoryId) REFERENCES Inventories(Id),
			FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(ReleasedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(CompletedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(ReversedByUserId) REFERENCES Users(Id),
			CHECK(PlannedQuantity > 0),
			CHECK(CompletedQuantity >= 0 AND CompletedQuantity <= PlannedQuantity),
			CHECK(Status BETWEEN 1 AND 6));
		CREATE INDEX IF NOT EXISTS IX_ProductionOrders_Status_Warehouse ON ProductionOrders(Status, WarehouseId, Id);
		CREATE TABLE IF NOT EXISTS ProductionOrderRequirements (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			ProductionOrderId INTEGER NOT NULL,
			SourceBillOfMaterialLineId INTEGER NOT NULL,
			ComponentItemId INTEGER NOT NULL,
			ComponentPartNumber TEXT NOT NULL,
			ComponentDescription TEXT NOT NULL,
			UnitOfMeasure TEXT NULL,
			RequiredQuantity INTEGER NOT NULL,
			IssuedQuantity INTEGER NOT NULL DEFAULT 0,
			Sequence INTEGER NOT NULL,
			UNIQUE(ProductionOrderId, SourceBillOfMaterialLineId),
			FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
			FOREIGN KEY(SourceBillOfMaterialLineId) REFERENCES ProductionBillOfMaterialLines(Id),
			FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
			CHECK(RequiredQuantity > 0),
			CHECK(IssuedQuantity >= 0 AND IssuedQuantity <= RequiredQuantity));
		CREATE INDEX IF NOT EXISTS IX_ProductionRequirements_Order_Component ON ProductionOrderRequirements(ProductionOrderId, ComponentItemId);
		CREATE TABLE IF NOT EXISTS ProductionMaterialMovements (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			ProductionOrderId INTEGER NOT NULL,
			RequirementId INTEGER NULL,
			StockMovementId INTEGER NOT NULL UNIQUE,
			Kind INTEGER NOT NULL,
			Quantity INTEGER NOT NULL,
			OperationKey TEXT NOT NULL UNIQUE,
			CreatedAtUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
			FOREIGN KEY(RequirementId) REFERENCES ProductionOrderRequirements(Id),
			FOREIGN KEY(StockMovementId) REFERENCES StockMovements(Id),
			FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			CHECK(Kind BETWEEN 1 AND 4),
			CHECK(Quantity > 0));
		CREATE INDEX IF NOT EXISTS IX_ProductionMovements_Order_Kind ON ProductionMaterialMovements(ProductionOrderId, Kind, Id);
		CREATE TABLE IF NOT EXISTS ProductionAssemblyCostEvidence (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			ProductionOrderId INTEGER NOT NULL,
			RequirementId INTEGER NOT NULL,
			ComponentItemId INTEGER NOT NULL,
			Quantity INTEGER NOT NULL,
			UnitCost NUMERIC NOT NULL,
			ExtendedCost NUMERIC NOT NULL,
			Currency TEXT NOT NULL,
			EvidenceVersion TEXT NOT NULL,
			CalculatedAtUtc TEXT NOT NULL,
			UNIQUE(ProductionOrderId, RequirementId),
			FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
			FOREIGN KEY(RequirementId) REFERENCES ProductionOrderRequirements(Id),
			FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
			CHECK(Quantity > 0),
			CHECK(UnitCost >= 0),
			CHECK(ExtendedCost >= 0));
		""";

	private const string SqlServer="""
		IF OBJECT_ID(N'ProductionBillsOfMaterial', N'U') IS NULL BEGIN
			CREATE TABLE ProductionBillsOfMaterial (
				Id bigint IDENTITY(1,1) PRIMARY KEY, FinishedItemId bigint NOT NULL, Revision nvarchar(80) NOT NULL, Status int NOT NULL,
				EffectiveFromUtc nvarchar(40) NULL, EffectiveUntilUtc nvarchar(40) NULL, Version bigint NOT NULL DEFAULT 1,
				CreatedAtUtc nvarchar(40) NOT NULL, CreatedByUserId bigint NOT NULL,
				CONSTRAINT UQ_ProductionBoms_Item_Revision UNIQUE(FinishedItemId,Revision),
				CONSTRAINT FK_ProductionBoms_Item FOREIGN KEY(FinishedItemId) REFERENCES Items(Id),
				CONSTRAINT FK_ProductionBoms_User FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
				CONSTRAINT CK_ProductionBoms_Status CHECK(Status BETWEEN 1 AND 3));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductionBoms_Item_Status' AND object_id=OBJECT_ID(N'ProductionBillsOfMaterial')) CREATE INDEX IX_ProductionBoms_Item_Status ON ProductionBillsOfMaterial(FinishedItemId,Status,EffectiveFromUtc,EffectiveUntilUtc);
		IF OBJECT_ID(N'ProductionBillOfMaterialLines', N'U') IS NULL BEGIN
			CREATE TABLE ProductionBillOfMaterialLines (
				Id bigint IDENTITY(1,1) PRIMARY KEY, BillOfMaterialId bigint NOT NULL, ComponentItemId bigint NOT NULL,
				Quantity decimal(18,6) NOT NULL, Sequence int NOT NULL,
				CONSTRAINT UQ_ProductionBomLines_Component UNIQUE(BillOfMaterialId,ComponentItemId),
				CONSTRAINT FK_ProductionBomLines_Bom FOREIGN KEY(BillOfMaterialId) REFERENCES ProductionBillsOfMaterial(Id) ON DELETE CASCADE,
				CONSTRAINT FK_ProductionBomLines_Item FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
				CONSTRAINT CK_ProductionBomLines_Quantity CHECK(Quantity>0), CONSTRAINT CK_ProductionBomLines_Sequence CHECK(Sequence>=0));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductionBomLines_Component' AND object_id=OBJECT_ID(N'ProductionBillOfMaterialLines')) CREATE INDEX IX_ProductionBomLines_Component ON ProductionBillOfMaterialLines(ComponentItemId,BillOfMaterialId);
		IF OBJECT_ID(N'ProductionOrders', N'U') IS NULL BEGIN
			CREATE TABLE ProductionOrders (
				Id bigint IDENTITY(1,1) PRIMARY KEY, OrderNumber nvarchar(40) NOT NULL UNIQUE, FinishedItemId bigint NOT NULL,
				BillOfMaterialId bigint NOT NULL, BillOfMaterialRevision nvarchar(80) NOT NULL, PlannedQuantity int NOT NULL,
				CompletedQuantity int NOT NULL DEFAULT 0, WarehouseId bigint NOT NULL, FinishedInventoryId bigint NOT NULL,
				Status int NOT NULL, OwnerUserId bigint NULL, CreatedAtUtc nvarchar(40) NOT NULL, CreatedByUserId bigint NOT NULL,
				ReleasedAtUtc nvarchar(40) NULL, ReleasedByUserId bigint NULL, CompletedAtUtc nvarchar(40) NULL, CompletedByUserId bigint NULL,
				ReversedAtUtc nvarchar(40) NULL, ReversedByUserId bigint NULL, Version bigint NOT NULL DEFAULT 1,
				CONSTRAINT FK_ProductionOrders_Item FOREIGN KEY(FinishedItemId) REFERENCES Items(Id),
				CONSTRAINT FK_ProductionOrders_Bom FOREIGN KEY(BillOfMaterialId) REFERENCES ProductionBillsOfMaterial(Id),
				CONSTRAINT FK_ProductionOrders_Warehouse FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id),
				CONSTRAINT FK_ProductionOrders_Inventory FOREIGN KEY(FinishedInventoryId) REFERENCES Inventories(Id),
				CONSTRAINT FK_ProductionOrders_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
				CONSTRAINT FK_ProductionOrders_CreatedUser FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
				CONSTRAINT FK_ProductionOrders_ReleasedUser FOREIGN KEY(ReleasedByUserId) REFERENCES Users(Id),
				CONSTRAINT FK_ProductionOrders_CompletedUser FOREIGN KEY(CompletedByUserId) REFERENCES Users(Id),
				CONSTRAINT FK_ProductionOrders_ReversedUser FOREIGN KEY(ReversedByUserId) REFERENCES Users(Id),
				CONSTRAINT CK_ProductionOrders_Planned CHECK(PlannedQuantity>0),
				CONSTRAINT CK_ProductionOrders_Completed CHECK(CompletedQuantity>=0 AND CompletedQuantity<=PlannedQuantity),
				CONSTRAINT CK_ProductionOrders_Status CHECK(Status BETWEEN 1 AND 6));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductionOrders_Status_Warehouse' AND object_id=OBJECT_ID(N'ProductionOrders')) CREATE INDEX IX_ProductionOrders_Status_Warehouse ON ProductionOrders(Status,WarehouseId,Id);
		IF OBJECT_ID(N'ProductionOrderRequirements', N'U') IS NULL BEGIN
			CREATE TABLE ProductionOrderRequirements (
				Id bigint IDENTITY(1,1) PRIMARY KEY, ProductionOrderId bigint NOT NULL, SourceBillOfMaterialLineId bigint NOT NULL,
				ComponentItemId bigint NOT NULL, ComponentPartNumber nvarchar(200) NOT NULL, ComponentDescription nvarchar(1000) NOT NULL,
				UnitOfMeasure nvarchar(100) NULL, RequiredQuantity int NOT NULL, IssuedQuantity int NOT NULL DEFAULT 0, Sequence int NOT NULL,
				CONSTRAINT UQ_ProductionRequirements_Source UNIQUE(ProductionOrderId,SourceBillOfMaterialLineId),
				CONSTRAINT FK_ProductionRequirements_Order FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
				CONSTRAINT FK_ProductionRequirements_BomLine FOREIGN KEY(SourceBillOfMaterialLineId) REFERENCES ProductionBillOfMaterialLines(Id),
				CONSTRAINT FK_ProductionRequirements_Item FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
				CONSTRAINT CK_ProductionRequirements_Required CHECK(RequiredQuantity>0),
				CONSTRAINT CK_ProductionRequirements_Issued CHECK(IssuedQuantity>=0 AND IssuedQuantity<=RequiredQuantity));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductionRequirements_Order_Component' AND object_id=OBJECT_ID(N'ProductionOrderRequirements')) CREATE INDEX IX_ProductionRequirements_Order_Component ON ProductionOrderRequirements(ProductionOrderId,ComponentItemId);
		IF OBJECT_ID(N'ProductionMaterialMovements', N'U') IS NULL BEGIN
			CREATE TABLE ProductionMaterialMovements (
				Id bigint IDENTITY(1,1) PRIMARY KEY, ProductionOrderId bigint NOT NULL, RequirementId bigint NULL, StockMovementId bigint NOT NULL UNIQUE,
				Kind int NOT NULL, Quantity int NOT NULL, OperationKey nvarchar(64) NOT NULL UNIQUE, CreatedAtUtc nvarchar(40) NOT NULL, CreatedByUserId bigint NOT NULL,
				CONSTRAINT FK_ProductionMovements_Order FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
				CONSTRAINT FK_ProductionMovements_Requirement FOREIGN KEY(RequirementId) REFERENCES ProductionOrderRequirements(Id),
				CONSTRAINT FK_ProductionMovements_Stock FOREIGN KEY(StockMovementId) REFERENCES StockMovements(Id),
				CONSTRAINT FK_ProductionMovements_User FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
				CONSTRAINT CK_ProductionMovements_Kind CHECK(Kind BETWEEN 1 AND 4), CONSTRAINT CK_ProductionMovements_Quantity CHECK(Quantity>0));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductionMovements_Order_Kind' AND object_id=OBJECT_ID(N'ProductionMaterialMovements')) CREATE INDEX IX_ProductionMovements_Order_Kind ON ProductionMaterialMovements(ProductionOrderId,Kind,Id);
		IF OBJECT_ID(N'ProductionAssemblyCostEvidence', N'U') IS NULL BEGIN
			CREATE TABLE ProductionAssemblyCostEvidence (
				Id bigint IDENTITY(1,1) PRIMARY KEY, ProductionOrderId bigint NOT NULL, RequirementId bigint NOT NULL, ComponentItemId bigint NOT NULL,
				Quantity int NOT NULL, UnitCost decimal(19,6) NOT NULL, ExtendedCost decimal(19,6) NOT NULL, Currency char(3) NOT NULL,
				EvidenceVersion nvarchar(2000) NOT NULL, CalculatedAtUtc nvarchar(40) NOT NULL,
				CONSTRAINT UQ_ProductionCost_Order_Requirement UNIQUE(ProductionOrderId,RequirementId),
				CONSTRAINT FK_ProductionCost_Order FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
				CONSTRAINT FK_ProductionCost_Requirement FOREIGN KEY(RequirementId) REFERENCES ProductionOrderRequirements(Id),
				CONSTRAINT FK_ProductionCost_Item FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
				CONSTRAINT CK_ProductionCost_Quantity CHECK(Quantity>0), CONSTRAINT CK_ProductionCost_Unit CHECK(UnitCost>=0), CONSTRAINT CK_ProductionCost_Extended CHECK(ExtendedCost>=0));
		END;
		""";

	private const string MySql="""
		CREATE TABLE IF NOT EXISTS ProductionBillsOfMaterial (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, FinishedItemId bigint NOT NULL, Revision varchar(80) NOT NULL, Status int NOT NULL,
			EffectiveFromUtc varchar(40) NULL, EffectiveUntilUtc varchar(40) NULL, Version bigint NOT NULL DEFAULT 1,
			CreatedAtUtc varchar(40) NOT NULL, CreatedByUserId bigint NOT NULL,
			UNIQUE KEY UQ_ProductionBoms_Item_Revision(FinishedItemId,Revision),
			INDEX IX_ProductionBoms_Item_Status(FinishedItemId,Status,EffectiveFromUtc,EffectiveUntilUtc),
			CONSTRAINT FK_ProductionBoms_Item FOREIGN KEY(FinishedItemId) REFERENCES Items(Id),
			CONSTRAINT FK_ProductionBoms_User FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT CK_ProductionBoms_Status CHECK(Status BETWEEN 1 AND 3)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProductionBillOfMaterialLines (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, BillOfMaterialId bigint NOT NULL, ComponentItemId bigint NOT NULL,
			Quantity decimal(18,6) NOT NULL, Sequence int NOT NULL,
			UNIQUE KEY UQ_ProductionBomLines_Component(BillOfMaterialId,ComponentItemId),
			INDEX IX_ProductionBomLines_Component(ComponentItemId,BillOfMaterialId),
			CONSTRAINT FK_ProductionBomLines_Bom FOREIGN KEY(BillOfMaterialId) REFERENCES ProductionBillsOfMaterial(Id) ON DELETE CASCADE,
			CONSTRAINT FK_ProductionBomLines_Item FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
			CONSTRAINT CK_ProductionBomLines_Quantity CHECK(Quantity>0), CONSTRAINT CK_ProductionBomLines_Sequence CHECK(Sequence>=0)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProductionOrders (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, OrderNumber varchar(40) NOT NULL, FinishedItemId bigint NOT NULL, BillOfMaterialId bigint NOT NULL,
			BillOfMaterialRevision varchar(80) NOT NULL, PlannedQuantity int NOT NULL, CompletedQuantity int NOT NULL DEFAULT 0,
			WarehouseId bigint NOT NULL, FinishedInventoryId bigint NOT NULL, Status int NOT NULL, OwnerUserId bigint NULL,
			CreatedAtUtc varchar(40) NOT NULL, CreatedByUserId bigint NOT NULL, ReleasedAtUtc varchar(40) NULL, ReleasedByUserId bigint NULL,
			CompletedAtUtc varchar(40) NULL, CompletedByUserId bigint NULL, ReversedAtUtc varchar(40) NULL, ReversedByUserId bigint NULL, Version bigint NOT NULL DEFAULT 1,
			UNIQUE KEY UQ_ProductionOrders_Number(OrderNumber), INDEX IX_ProductionOrders_Status_Warehouse(Status,WarehouseId,Id),
			CONSTRAINT FK_ProductionOrders_Item FOREIGN KEY(FinishedItemId) REFERENCES Items(Id),
			CONSTRAINT FK_ProductionOrders_Bom FOREIGN KEY(BillOfMaterialId) REFERENCES ProductionBillsOfMaterial(Id),
			CONSTRAINT FK_ProductionOrders_Warehouse FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id),
			CONSTRAINT FK_ProductionOrders_Inventory FOREIGN KEY(FinishedInventoryId) REFERENCES Inventories(Id),
			CONSTRAINT FK_ProductionOrders_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ProductionOrders_CreatedUser FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ProductionOrders_ReleasedUser FOREIGN KEY(ReleasedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ProductionOrders_CompletedUser FOREIGN KEY(CompletedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ProductionOrders_ReversedUser FOREIGN KEY(ReversedByUserId) REFERENCES Users(Id),
			CONSTRAINT CK_ProductionOrders_Planned CHECK(PlannedQuantity>0),
			CONSTRAINT CK_ProductionOrders_Completed CHECK(CompletedQuantity>=0 AND CompletedQuantity<=PlannedQuantity),
			CONSTRAINT CK_ProductionOrders_Status CHECK(Status BETWEEN 1 AND 6)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProductionOrderRequirements (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ProductionOrderId bigint NOT NULL, SourceBillOfMaterialLineId bigint NOT NULL,
			ComponentItemId bigint NOT NULL, ComponentPartNumber varchar(200) NOT NULL, ComponentDescription varchar(1000) NOT NULL,
			UnitOfMeasure varchar(100) NULL, RequiredQuantity int NOT NULL, IssuedQuantity int NOT NULL DEFAULT 0, Sequence int NOT NULL,
			UNIQUE KEY UQ_ProductionRequirements_Source(ProductionOrderId,SourceBillOfMaterialLineId),
			INDEX IX_ProductionRequirements_Order_Component(ProductionOrderId,ComponentItemId),
			CONSTRAINT FK_ProductionRequirements_Order FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
			CONSTRAINT FK_ProductionRequirements_BomLine FOREIGN KEY(SourceBillOfMaterialLineId) REFERENCES ProductionBillOfMaterialLines(Id),
			CONSTRAINT FK_ProductionRequirements_Item FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
			CONSTRAINT CK_ProductionRequirements_Required CHECK(RequiredQuantity>0),
			CONSTRAINT CK_ProductionRequirements_Issued CHECK(IssuedQuantity>=0 AND IssuedQuantity<=RequiredQuantity)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProductionMaterialMovements (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ProductionOrderId bigint NOT NULL, RequirementId bigint NULL, StockMovementId bigint NOT NULL,
			Kind int NOT NULL, Quantity int NOT NULL, OperationKey varchar(64) NOT NULL, CreatedAtUtc varchar(40) NOT NULL, CreatedByUserId bigint NOT NULL,
			UNIQUE KEY UQ_ProductionMovements_Stock(StockMovementId), UNIQUE KEY UQ_ProductionMovements_Operation(OperationKey),
			INDEX IX_ProductionMovements_Order_Kind(ProductionOrderId,Kind,Id),
			CONSTRAINT FK_ProductionMovements_Order FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
			CONSTRAINT FK_ProductionMovements_Requirement FOREIGN KEY(RequirementId) REFERENCES ProductionOrderRequirements(Id),
			CONSTRAINT FK_ProductionMovements_Stock FOREIGN KEY(StockMovementId) REFERENCES StockMovements(Id),
			CONSTRAINT FK_ProductionMovements_User FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT CK_ProductionMovements_Kind CHECK(Kind BETWEEN 1 AND 4), CONSTRAINT CK_ProductionMovements_Quantity CHECK(Quantity>0)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProductionAssemblyCostEvidence (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ProductionOrderId bigint NOT NULL, RequirementId bigint NOT NULL, ComponentItemId bigint NOT NULL,
			Quantity int NOT NULL, UnitCost decimal(19,6) NOT NULL, ExtendedCost decimal(19,6) NOT NULL, Currency char(3) NOT NULL,
			EvidenceVersion text NOT NULL, CalculatedAtUtc varchar(40) NOT NULL,
			UNIQUE KEY UQ_ProductionCost_Order_Requirement(ProductionOrderId,RequirementId),
			CONSTRAINT FK_ProductionCost_Order FOREIGN KEY(ProductionOrderId) REFERENCES ProductionOrders(Id),
			CONSTRAINT FK_ProductionCost_Requirement FOREIGN KEY(RequirementId) REFERENCES ProductionOrderRequirements(Id),
			CONSTRAINT FK_ProductionCost_Item FOREIGN KEY(ComponentItemId) REFERENCES Items(Id),
			CONSTRAINT CK_ProductionCost_Quantity CHECK(Quantity>0), CONSTRAINT CK_ProductionCost_Unit CHECK(UnitCost>=0), CONSTRAINT CK_ProductionCost_Extended CHECK(ExtendedCost>=0)) ENGINE=InnoDB;
		""";
}
