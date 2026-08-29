// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class ItemCostSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection=connectionFactory.CreateConnection();connection.Open();using var transaction=connectionFactory.BeginWriteTransaction(connection);using var command=connection.CreateCommand();command.Transaction=transaction;
		var statements=connectionFactory.Provider switch { DatabaseProvider.Local=>Sqlite,DatabaseProvider.SqlServer=>SqlServer,DatabaseProvider.MySql=>MySql,_=>throw new NotSupportedException($"Item cost schema is not supported for provider '{connectionFactory.Provider}'.") };
		foreach(var statement in statements){command.CommandText=statement;command.ExecuteNonQuery();} transaction.Commit();
	}
	private static readonly string[] Sqlite=[
		"CREATE TABLE ItemCostProfiles (Id INTEGER PRIMARY KEY AUTOINCREMENT, ItemId INTEGER NOT NULL UNIQUE, BaseCostSource INTEGER NOT NULL DEFAULT 0 CHECK(BaseCostSource=0), Currency TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"CREATE TABLE ItemCostComponents (Id INTEGER PRIMARY KEY AUTOINCREMENT, ItemId INTEGER NOT NULL, Name TEXT NOT NULL, CalculationType INTEGER NOT NULL CHECK(CalculationType IN (0,1)), Value NUMERIC NOT NULL CHECK(Value>=0), CalculationBase INTEGER NOT NULL DEFAULT 0 CHECK(CalculationBase IN (0,1)), Sequence INTEGER NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1, ValidFrom TEXT NULL, ValidUntil TEXT NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"CREATE INDEX IX_ItemCostComponents_ItemSequence ON ItemCostComponents(ItemId,Sequence,Id);"
	];
	private static readonly string[] SqlServer=[
		"CREATE TABLE ItemCostProfiles (Id bigint IDENTITY(1,1) PRIMARY KEY, ItemId bigint NOT NULL UNIQUE, BaseCostSource int NOT NULL CONSTRAINT DF_ItemCostProfiles_Source DEFAULT 0, Currency nvarchar(3) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_ItemCostProfiles_Version DEFAULT 1, CONSTRAINT CK_ItemCostProfiles_Source CHECK(BaseCostSource=0), CONSTRAINT FK_ItemCostProfiles_Item FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"CREATE TABLE ItemCostComponents (Id bigint IDENTITY(1,1) PRIMARY KEY, ItemId bigint NOT NULL, Name nvarchar(200) NOT NULL, CalculationType int NOT NULL, Value decimal(18,6) NOT NULL, CalculationBase int NOT NULL CONSTRAINT DF_ItemCostComponents_Base DEFAULT 0, Sequence int NOT NULL, IsActive bit NOT NULL CONSTRAINT DF_ItemCostComponents_Active DEFAULT 1, ValidFrom date NULL, ValidUntil date NULL, Version bigint NOT NULL CONSTRAINT DF_ItemCostComponents_Version DEFAULT 1, CONSTRAINT CK_ItemCostComponents_Type CHECK(CalculationType IN (0,1)), CONSTRAINT CK_ItemCostComponents_Value CHECK(Value>=0), CONSTRAINT CK_ItemCostComponents_Base CHECK(CalculationBase IN (0,1)), CONSTRAINT FK_ItemCostComponents_Item FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"CREATE INDEX IX_ItemCostComponents_ItemSequence ON ItemCostComponents(ItemId,Sequence,Id);"
	];
	private static readonly string[] MySql=[
		"CREATE TABLE ItemCostProfiles (Id BIGINT AUTO_INCREMENT PRIMARY KEY, ItemId BIGINT NOT NULL UNIQUE, BaseCostSource INT NOT NULL DEFAULT 0, Currency VARCHAR(3) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT CK_ItemCostProfiles_Source CHECK(BaseCostSource=0), FOREIGN KEY(ItemId) REFERENCES Items(Id)) ENGINE=InnoDB;",
		"CREATE TABLE ItemCostComponents (Id BIGINT AUTO_INCREMENT PRIMARY KEY, ItemId BIGINT NOT NULL, Name VARCHAR(200) NOT NULL, CalculationType INT NOT NULL, Value DECIMAL(18,6) NOT NULL, CalculationBase INT NOT NULL DEFAULT 0, Sequence INT NOT NULL, IsActive BOOLEAN NOT NULL DEFAULT TRUE, ValidFrom DATE NULL, ValidUntil DATE NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT CK_ItemCostComponents_Type CHECK(CalculationType IN (0,1)), CONSTRAINT CK_ItemCostComponents_Value CHECK(Value>=0), CONSTRAINT CK_ItemCostComponents_Base CHECK(CalculationBase IN (0,1)), INDEX IX_ItemCostComponents_ItemSequence(ItemId,Sequence,Id), FOREIGN KEY(ItemId) REFERENCES Items(Id)) ENGINE=InnoDB;"
	];
}
