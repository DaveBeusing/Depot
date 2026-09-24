// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

public static class InventoryReplenishmentSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Inventory-replenishment persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private const string Sqlite = """
		CREATE TABLE IF NOT EXISTS ReplenishmentPolicies (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, ItemId INTEGER NOT NULL, WarehouseId INTEGER NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1,
			ReorderPoint INTEGER NOT NULL, SafetyStock INTEGER NOT NULL, TargetStock INTEGER NOT NULL, PreferredSupplierId INTEGER NULL,
			Version INTEGER NOT NULL DEFAULT 1, UNIQUE(ItemId, WarehouseId),
			FOREIGN KEY(ItemId) REFERENCES Items(Id), FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id), FOREIGN KEY(PreferredSupplierId) REFERENCES Suppliers(Id),
			CHECK(ReorderPoint >= 0), CHECK(SafetyStock >= 0), CHECK(TargetStock >= 0), CHECK(ReorderPoint >= SafetyStock), CHECK(TargetStock >= ReorderPoint));
		CREATE INDEX IF NOT EXISTS IX_ReplenishmentPolicies_Active ON ReplenishmentPolicies(IsActive, ItemId, WarehouseId);
		CREATE TABLE IF NOT EXISTS ReplenishmentRequirementSnapshots (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, PolicyId INTEGER NOT NULL, SnapshotKey TEXT NOT NULL, CalculatedAtUtc TEXT NOT NULL,
			OnHandQuantity INTEGER NOT NULL, ReservedQuantity INTEGER NOT NULL, BackorderedQuantity INTEGER NOT NULL, EligibleInboundQuantity INTEGER NOT NULL,
			ProjectedAvailableQuantity INTEGER NOT NULL, ReorderPoint INTEGER NOT NULL, SafetyStock INTEGER NOT NULL, TargetStock INTEGER NOT NULL,
			RequiredReplenishmentQuantity INTEGER NOT NULL, SuggestedPurchaseQuantity INTEGER NOT NULL,
			PlanningSupplierId INTEGER NULL, SupplierItemId INTEGER NULL, SupplierLeadTimeDays INTEGER NULL, SupplierMinimumOrderQuantity NUMERIC NULL,
			IsBlocked INTEGER NOT NULL DEFAULT 0, BlockReason TEXT NULL, Explanation TEXT NOT NULL,
			UNIQUE(PolicyId, SnapshotKey), FOREIGN KEY(PolicyId) REFERENCES ReplenishmentPolicies(Id),
			FOREIGN KEY(PlanningSupplierId) REFERENCES Suppliers(Id), FOREIGN KEY(SupplierItemId) REFERENCES SupplierItems(Id));
		CREATE INDEX IF NOT EXISTS IX_ReplenishmentSnapshots_Policy_Calculated ON ReplenishmentRequirementSnapshots(PolicyId, CalculatedAtUtc);
		CREATE TABLE IF NOT EXISTS ReplenishmentSuggestions (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, PolicyId INTEGER NOT NULL, SnapshotId INTEGER NOT NULL UNIQUE, Status INTEGER NOT NULL,
			SuggestedQuantity INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, ReviewedAtUtc TEXT NULL, ReviewedByUserId INTEGER NULL,
			ConvertedPurchaseRequisitionId INTEGER NULL UNIQUE, Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(PolicyId) REFERENCES ReplenishmentPolicies(Id), FOREIGN KEY(SnapshotId) REFERENCES ReplenishmentRequirementSnapshots(Id),
			FOREIGN KEY(ReviewedByUserId) REFERENCES Users(Id), FOREIGN KEY(ConvertedPurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id),
			CHECK(Status BETWEEN 1 AND 5), CHECK(SuggestedQuantity >= 0));
		CREATE INDEX IF NOT EXISTS IX_ReplenishmentSuggestions_Status_Policy ON ReplenishmentSuggestions(Status, PolicyId, Id);
		""";

	private const string SqlServer = """
		IF OBJECT_ID(N'ReplenishmentPolicies', N'U') IS NULL BEGIN
			CREATE TABLE ReplenishmentPolicies (
				Id bigint IDENTITY(1,1) PRIMARY KEY, ItemId bigint NOT NULL, WarehouseId bigint NOT NULL, IsActive bit NOT NULL DEFAULT 1,
				ReorderPoint int NOT NULL, SafetyStock int NOT NULL, TargetStock int NOT NULL, PreferredSupplierId bigint NULL, Version bigint NOT NULL DEFAULT 1,
				CONSTRAINT UQ_ReplenishmentPolicies_Context UNIQUE(ItemId, WarehouseId),
				CONSTRAINT FK_ReplenishmentPolicies_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
				CONSTRAINT FK_ReplenishmentPolicies_Warehouse FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id),
				CONSTRAINT FK_ReplenishmentPolicies_Supplier FOREIGN KEY(PreferredSupplierId) REFERENCES Suppliers(Id),
				CONSTRAINT CK_ReplenishmentPolicies_Thresholds CHECK(ReorderPoint >= 0 AND SafetyStock >= 0 AND TargetStock >= 0 AND ReorderPoint >= SafetyStock AND TargetStock >= ReorderPoint));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ReplenishmentPolicies_Active' AND object_id=OBJECT_ID(N'ReplenishmentPolicies')) CREATE INDEX IX_ReplenishmentPolicies_Active ON ReplenishmentPolicies(IsActive, ItemId, WarehouseId);
		IF OBJECT_ID(N'ReplenishmentRequirementSnapshots', N'U') IS NULL BEGIN
			CREATE TABLE ReplenishmentRequirementSnapshots (
				Id bigint IDENTITY(1,1) PRIMARY KEY, PolicyId bigint NOT NULL, SnapshotKey char(64) NOT NULL, CalculatedAtUtc nvarchar(40) NOT NULL,
				OnHandQuantity bigint NOT NULL, ReservedQuantity bigint NOT NULL, BackorderedQuantity bigint NOT NULL, EligibleInboundQuantity bigint NOT NULL,
				ProjectedAvailableQuantity bigint NOT NULL, ReorderPoint int NOT NULL, SafetyStock int NOT NULL, TargetStock int NOT NULL,
				RequiredReplenishmentQuantity bigint NOT NULL, SuggestedPurchaseQuantity int NOT NULL,
				PlanningSupplierId bigint NULL, SupplierItemId bigint NULL, SupplierLeadTimeDays int NULL, SupplierMinimumOrderQuantity decimal(18,4) NULL,
				IsBlocked bit NOT NULL DEFAULT 0, BlockReason nvarchar(2000) NULL, Explanation nvarchar(4000) NOT NULL,
				CONSTRAINT UQ_ReplenishmentSnapshots_Key UNIQUE(PolicyId, SnapshotKey),
				CONSTRAINT FK_ReplenishmentSnapshots_Policy FOREIGN KEY(PolicyId) REFERENCES ReplenishmentPolicies(Id),
				CONSTRAINT FK_ReplenishmentSnapshots_Supplier FOREIGN KEY(PlanningSupplierId) REFERENCES Suppliers(Id),
				CONSTRAINT FK_ReplenishmentSnapshots_SupplierItem FOREIGN KEY(SupplierItemId) REFERENCES SupplierItems(Id));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ReplenishmentSnapshots_Policy_Calculated' AND object_id=OBJECT_ID(N'ReplenishmentRequirementSnapshots')) CREATE INDEX IX_ReplenishmentSnapshots_Policy_Calculated ON ReplenishmentRequirementSnapshots(PolicyId, CalculatedAtUtc);
		IF OBJECT_ID(N'ReplenishmentSuggestions', N'U') IS NULL BEGIN
			CREATE TABLE ReplenishmentSuggestions (
				Id bigint IDENTITY(1,1) PRIMARY KEY, PolicyId bigint NOT NULL, SnapshotId bigint NOT NULL UNIQUE, Status int NOT NULL,
				SuggestedQuantity int NOT NULL, CreatedAtUtc nvarchar(40) NOT NULL, ReviewedAtUtc nvarchar(40) NULL, ReviewedByUserId bigint NULL,
				ConvertedPurchaseRequisitionId bigint NULL, Version bigint NOT NULL DEFAULT 1,
				CONSTRAINT FK_ReplenishmentSuggestions_Policy FOREIGN KEY(PolicyId) REFERENCES ReplenishmentPolicies(Id),
				CONSTRAINT FK_ReplenishmentSuggestions_Snapshot FOREIGN KEY(SnapshotId) REFERENCES ReplenishmentRequirementSnapshots(Id),
				CONSTRAINT FK_ReplenishmentSuggestions_User FOREIGN KEY(ReviewedByUserId) REFERENCES Users(Id),
				CONSTRAINT FK_ReplenishmentSuggestions_Requisition FOREIGN KEY(ConvertedPurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id),
				CONSTRAINT CK_ReplenishmentSuggestions_Status CHECK(Status BETWEEN 1 AND 5),
				CONSTRAINT CK_ReplenishmentSuggestions_Quantity CHECK(SuggestedQuantity >= 0));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ReplenishmentSuggestions_Requisition' AND object_id=OBJECT_ID(N'ReplenishmentSuggestions')) CREATE UNIQUE INDEX UX_ReplenishmentSuggestions_Requisition ON ReplenishmentSuggestions(ConvertedPurchaseRequisitionId) WHERE ConvertedPurchaseRequisitionId IS NOT NULL;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ReplenishmentSuggestions_Status_Policy' AND object_id=OBJECT_ID(N'ReplenishmentSuggestions')) CREATE INDEX IX_ReplenishmentSuggestions_Status_Policy ON ReplenishmentSuggestions(Status, PolicyId, Id);
		""";

	private const string MySql = """
		CREATE TABLE IF NOT EXISTS ReplenishmentPolicies (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ItemId bigint NOT NULL, WarehouseId bigint NOT NULL, IsActive boolean NOT NULL DEFAULT true,
			ReorderPoint int NOT NULL, SafetyStock int NOT NULL, TargetStock int NOT NULL, PreferredSupplierId bigint NULL, Version bigint NOT NULL DEFAULT 1,
			UNIQUE KEY UQ_ReplenishmentPolicies_Context(ItemId, WarehouseId), INDEX IX_ReplenishmentPolicies_Active(IsActive, ItemId, WarehouseId),
			CONSTRAINT FK_ReplenishmentPolicies_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT FK_ReplenishmentPolicies_Warehouse FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id),
			CONSTRAINT FK_ReplenishmentPolicies_Supplier FOREIGN KEY(PreferredSupplierId) REFERENCES Suppliers(Id),
			CONSTRAINT CK_ReplenishmentPolicies_Thresholds CHECK(ReorderPoint >= 0 AND SafetyStock >= 0 AND TargetStock >= 0 AND ReorderPoint >= SafetyStock AND TargetStock >= ReorderPoint)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ReplenishmentRequirementSnapshots (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, PolicyId bigint NOT NULL, SnapshotKey char(64) NOT NULL, CalculatedAtUtc varchar(40) NOT NULL,
			OnHandQuantity bigint NOT NULL, ReservedQuantity bigint NOT NULL, BackorderedQuantity bigint NOT NULL, EligibleInboundQuantity bigint NOT NULL,
			ProjectedAvailableQuantity bigint NOT NULL, ReorderPoint int NOT NULL, SafetyStock int NOT NULL, TargetStock int NOT NULL,
			RequiredReplenishmentQuantity bigint NOT NULL, SuggestedPurchaseQuantity int NOT NULL,
			PlanningSupplierId bigint NULL, SupplierItemId bigint NULL, SupplierLeadTimeDays int NULL, SupplierMinimumOrderQuantity decimal(18,4) NULL,
			IsBlocked boolean NOT NULL DEFAULT false, BlockReason text NULL, Explanation text NOT NULL,
			UNIQUE KEY UQ_ReplenishmentSnapshots_Key(PolicyId, SnapshotKey), INDEX IX_ReplenishmentSnapshots_Policy_Calculated(PolicyId, CalculatedAtUtc),
			CONSTRAINT FK_ReplenishmentSnapshots_Policy FOREIGN KEY(PolicyId) REFERENCES ReplenishmentPolicies(Id),
			CONSTRAINT FK_ReplenishmentSnapshots_Supplier FOREIGN KEY(PlanningSupplierId) REFERENCES Suppliers(Id),
			CONSTRAINT FK_ReplenishmentSnapshots_SupplierItem FOREIGN KEY(SupplierItemId) REFERENCES SupplierItems(Id)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ReplenishmentSuggestions (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, PolicyId bigint NOT NULL, SnapshotId bigint NOT NULL, Status int NOT NULL,
			SuggestedQuantity int NOT NULL, CreatedAtUtc varchar(40) NOT NULL, ReviewedAtUtc varchar(40) NULL, ReviewedByUserId bigint NULL,
			ConvertedPurchaseRequisitionId bigint NULL, Version bigint NOT NULL DEFAULT 1,
			UNIQUE KEY UQ_ReplenishmentSuggestions_Snapshot(SnapshotId), UNIQUE KEY UQ_ReplenishmentSuggestions_Requisition(ConvertedPurchaseRequisitionId),
			INDEX IX_ReplenishmentSuggestions_Status_Policy(Status, PolicyId, Id),
			CONSTRAINT FK_ReplenishmentSuggestions_Policy FOREIGN KEY(PolicyId) REFERENCES ReplenishmentPolicies(Id),
			CONSTRAINT FK_ReplenishmentSuggestions_Snapshot FOREIGN KEY(SnapshotId) REFERENCES ReplenishmentRequirementSnapshots(Id),
			CONSTRAINT FK_ReplenishmentSuggestions_User FOREIGN KEY(ReviewedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_ReplenishmentSuggestions_Requisition FOREIGN KEY(ConvertedPurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id),
			CONSTRAINT CK_ReplenishmentSuggestions_Status CHECK(Status BETWEEN 1 AND 5),
			CONSTRAINT CK_ReplenishmentSuggestions_Quantity CHECK(SuggestedQuantity >= 0)) ENGINE=InnoDB;
		""";
}
