// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class AdvancedPricingSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection=connectionFactory.CreateConnection();connection.Open();using var transaction=connectionFactory.BeginWriteTransaction(connection);using var command=connection.CreateCommand();command.Transaction=transaction;
		var statements=connectionFactory.Provider switch
		{
			DatabaseProvider.Local=>Sqlite,
			DatabaseProvider.SqlServer=>SqlServer,
			DatabaseProvider.MySql=>MySql,
			_=>throw new NotSupportedException($"Advanced pricing schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		foreach(var statement in statements){command.CommandText=statement;command.ExecuteNonQuery();}
		transaction.Commit();
	}

	private static readonly string[] Sqlite=[
		"CREATE TABLE ItemCostProfiles_v12 (Id INTEGER PRIMARY KEY AUTOINCREMENT, ItemId INTEGER NOT NULL UNIQUE, BaseCostSource INTEGER NOT NULL DEFAULT 0 CHECK(BaseCostSource IN (0,1,2,3)), Currency TEXT NOT NULL, ManualStandardCost NUMERIC NULL CHECK(ManualStandardCost IS NULL OR ManualStandardCost>=0), InventoryCostReference NUMERIC NULL CHECK(InventoryCostReference IS NULL OR InventoryCostReference>=0), Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(ItemId) REFERENCES Items(Id));",
		"INSERT INTO ItemCostProfiles_v12 (Id,ItemId,BaseCostSource,Currency,Version) SELECT Id,ItemId,BaseCostSource,Currency,Version FROM ItemCostProfiles;",
		"DROP TABLE ItemCostProfiles;",
		"ALTER TABLE ItemCostProfiles_v12 RENAME TO ItemCostProfiles;",
		"CREATE TABLE PricingExchangeRates (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceCurrency TEXT NOT NULL, TargetCurrency TEXT NOT NULL, EffectiveDate TEXT NOT NULL, RateSource TEXT NOT NULL, Rate NUMERIC NOT NULL CHECK(Rate>0), Version INTEGER NOT NULL DEFAULT 1, CHECK(SourceCurrency<>TargetCurrency), UNIQUE(SourceCurrency,TargetCurrency,EffectiveDate));",
		"CREATE INDEX IX_PricingExchangeRates_Lookup ON PricingExchangeRates(SourceCurrency,TargetCurrency,EffectiveDate DESC);"
	];

	private static readonly string[] SqlServer=[
		"ALTER TABLE ItemCostProfiles DROP CONSTRAINT CK_ItemCostProfiles_Source;",
		"ALTER TABLE ItemCostProfiles ADD ManualStandardCost decimal(18,6) NULL, InventoryCostReference decimal(18,6) NULL;",
		"ALTER TABLE ItemCostProfiles ADD CONSTRAINT CK_ItemCostProfiles_Source_v12 CHECK(BaseCostSource IN (0,1,2,3)), CONSTRAINT CK_ItemCostProfiles_Manual_v12 CHECK(ManualStandardCost IS NULL OR ManualStandardCost>=0), CONSTRAINT CK_ItemCostProfiles_Inventory_v12 CHECK(InventoryCostReference IS NULL OR InventoryCostReference>=0);",
		"CREATE TABLE PricingExchangeRates (Id bigint IDENTITY(1,1) PRIMARY KEY, SourceCurrency nvarchar(3) NOT NULL, TargetCurrency nvarchar(3) NOT NULL, EffectiveDate date NOT NULL, RateSource nvarchar(200) NOT NULL, Rate decimal(18,8) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_PricingExchangeRates_Version DEFAULT 1, CONSTRAINT CK_PricingExchangeRates_Rate CHECK(Rate>0), CONSTRAINT CK_PricingExchangeRates_Currencies CHECK(SourceCurrency<>TargetCurrency), CONSTRAINT UQ_PricingExchangeRates_Effective UNIQUE(SourceCurrency,TargetCurrency,EffectiveDate));",
		"CREATE INDEX IX_PricingExchangeRates_Lookup ON PricingExchangeRates(SourceCurrency,TargetCurrency,EffectiveDate DESC);"
	];

	private static readonly string[] MySql=[
		"CREATE TABLE ItemCostProfiles_v12 (Id BIGINT AUTO_INCREMENT PRIMARY KEY, ItemId BIGINT NOT NULL UNIQUE, BaseCostSource INT NOT NULL DEFAULT 0, Currency VARCHAR(3) NOT NULL, ManualStandardCost DECIMAL(18,6) NULL, InventoryCostReference DECIMAL(18,6) NULL, Version BIGINT NOT NULL DEFAULT 1, CHECK(BaseCostSource IN (0,1,2,3)), CHECK(ManualStandardCost IS NULL OR ManualStandardCost>=0), CHECK(InventoryCostReference IS NULL OR InventoryCostReference>=0), FOREIGN KEY(ItemId) REFERENCES Items(Id)) ENGINE=InnoDB;",
		"INSERT INTO ItemCostProfiles_v12 (Id,ItemId,BaseCostSource,Currency,Version) SELECT Id,ItemId,BaseCostSource,Currency,Version FROM ItemCostProfiles;",
		"DROP TABLE ItemCostProfiles;",
		"RENAME TABLE ItemCostProfiles_v12 TO ItemCostProfiles;",
		"CREATE TABLE PricingExchangeRates (Id BIGINT AUTO_INCREMENT PRIMARY KEY, SourceCurrency VARCHAR(3) NOT NULL, TargetCurrency VARCHAR(3) NOT NULL, EffectiveDate DATE NOT NULL, RateSource VARCHAR(200) NOT NULL, Rate DECIMAL(18,8) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CHECK(Rate>0), CHECK(SourceCurrency<>TargetCurrency), UNIQUE KEY UQ_PricingExchangeRates_Effective(SourceCurrency,TargetCurrency,EffectiveDate), INDEX IX_PricingExchangeRates_Lookup(SourceCurrency,TargetCurrency,EffectiveDate)) ENGINE=InnoDB;"
	];
}
