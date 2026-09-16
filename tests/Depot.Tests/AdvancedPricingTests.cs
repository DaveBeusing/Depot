// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class AdvancedPricingTests : IAsyncLifetime
{
	private readonly string _databasePath=Path.Combine(Path.GetTempPath(),$"depot-advanced-pricing-{Guid.NewGuid():N}.db");
	private Fixture? _fixture;
	private Fixture F=>_fixture??throw new InvalidOperationException("Fixture is not initialized.");

	[Fact]
	public async Task PercentageMarkupAndTargetGrossMarginRemainDistinct()
	{
		var item=await F.CreateCostedItemAsync("MARGIN",100m,"EUR");var list=await F.CreateListAsync("MARGIN-EUR","EUR");
		var markup=await F.Generation.PreviewAsync(Request(list.Id,item.Id,pricingMethod:BulkPricePricingMethod.PercentageMarkup,markup:25m));
		var margin=await F.Generation.PreviewAsync(Request(list.Id,item.Id,pricingMethod:BulkPricePricingMethod.TargetGrossMargin,margin:25m));
		Assert.Equal(125m,Assert.Single(markup.Rows).CalculatedNewPrice);Assert.Equal(133.33m,Assert.Single(margin.Rows).CalculatedNewPrice);Assert.Equal(BulkPricePricingMethod.TargetGrossMargin,margin.Rows[0].PricingMethod);Assert.Equal(25m,margin.Rows[0].PricingPercentage);
	}

	[Fact]
	public async Task FxResolutionIsEffectiveDatedDirectAndFailsClosedWhenMissing()
	{
		var item=await F.CreateCostedItemAsync("FX",100m,"USD");var list=await F.CreateListAsync("FX-EUR","EUR");
		await F.Generation.SaveExchangeRateAsync(new(){SourceCurrency="USD",TargetCurrency="EUR",EffectiveDate=new DateTime(2026,1,1),RateSource="ECB reference",Rate=0.90m});
		await F.Generation.SaveExchangeRateAsync(new(){SourceCurrency="USD",TargetCurrency="EUR",EffectiveDate=new DateTime(2026,7,1),RateSource="ECB reference",Rate=0.95m});
		var june=await F.Generation.PreviewAsync(Request(list.Id,item.Id,effectiveDate:new DateTime(2026,6,1)));var august=await F.Generation.PreviewAsync(Request(list.Id,item.Id,effectiveDate:new DateTime(2026,8,1)));var missing=await F.Generation.PreviewAsync(Request(list.Id,item.Id,effectiveDate:new DateTime(2025,12,31)));
		var juneRow=Assert.Single(june.Rows);Assert.Equal(90m,juneRow.ConvertedCost);Assert.Equal(90m,juneRow.CalculatedNewPrice);Assert.Equal(new DateTime(2026,1,1),juneRow.ExchangeRate?.EffectiveDate);Assert.Equal("ECB reference",juneRow.ExchangeRate?.RateSource);Assert.Equal(0.90m,juneRow.ExchangeRate?.Rate);Assert.Equal(95m,Assert.Single(august.Rows).ConvertedCost);Assert.Equal(BulkPricePreviewAction.Error,Assert.Single(missing.Rows).Action);Assert.Contains("No effective direct FX rate",missing.Rows[0].Error,StringComparison.Ordinal);
	}

	[Fact]
	public async Task CommercialRoundingStrategiesAreDeterministicAndVisible()
	{
		var item=await F.CreateCostedItemAsync("ROUND",10.02m,"EUR");var list=await F.CreateListAsync("ROUND-EUR","EUR");
		var nickel=await F.Generation.PreviewAsync(Request(list.Id,item.Id,rounding:CommercialRoundingStrategy.Nearest0_05));var ending=await F.Generation.PreviewAsync(Request(list.Id,item.Id,rounding:CommercialRoundingStrategy.Ending99));var cent=await F.Generation.PreviewAsync(Request(list.Id,item.Id,rounding:CommercialRoundingStrategy.Nearest0_01));
		Assert.Equal(10.00m,nickel.Rows[0].CalculatedNewPrice);Assert.Equal(-0.02m,nickel.Rows[0].RoundingAdjustment);Assert.Equal(10.99m,ending.Rows[0].CalculatedNewPrice);Assert.Equal(0.97m,ending.Rows[0].RoundingAdjustment);Assert.Equal(10.02m,cent.Rows[0].CalculatedNewPrice);Assert.Equal(CommercialRoundingStrategy.Nearest0_05,nickel.Rows[0].RoundingStrategy);
	}

	[Fact]
	public async Task BaseCostStrategiesAreExplicitAndMissingLastPurchaseFailsClosed()
	{
		var item=await F.CreateItemAsync("BASE-SOURCES");
		var manual=await F.Costs.SaveProfileAsync(new(){ItemId=item.Id,BaseCostSource=ItemCostBaseSource.ManualStandard,Currency="EUR",ManualStandardCost=80m});var manualResult=await F.Costs.CalculateAsync(item.Id,DateTime.Today);Assert.True(manualResult.IsSuccess);Assert.Equal(80m,manualResult.BaseCost);Assert.Equal(ItemCostBaseSource.ManualStandard,manualResult.BaseCostSource);
		manual.BaseCostSource=ItemCostBaseSource.InventoryCostReference;manual.InventoryCostReference=90m;await F.Costs.SaveProfileAsync(manual);var inventoryResult=await F.Costs.CalculateAsync(item.Id,DateTime.Today);Assert.True(inventoryResult.IsSuccess);Assert.Equal(90m,inventoryResult.BaseCost);Assert.Equal("InventoryCostReference",inventoryResult.BaseCostEvidenceType);
		manual.BaseCostSource=ItemCostBaseSource.LastPurchase;await F.Costs.SaveProfileAsync(manual);var lastPurchase=await F.Costs.CalculateAsync(item.Id,DateTime.Today);Assert.False(lastPurchase.IsSuccess);Assert.Contains("No received purchase",lastPurchase.Error,StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ApplyRejectsFxRateChangedAfterPreview()
	{
		var item=await F.CreateCostedItemAsync("FX-CONCURRENCY",100m,"USD");var list=await F.CreateListAsync("FX-CONCURRENCY-EUR","EUR");var rate=await F.Generation.SaveExchangeRateAsync(new(){SourceCurrency="USD",TargetCurrency="EUR",EffectiveDate=new DateTime(2026,1,1),RateSource="Treasury",Rate=0.90m});var preview=await F.Generation.PreviewAsync(Request(list.Id,item.Id,effectiveDate:new DateTime(2026,9,1)));
		rate.Rate=0.91m;await F.Generation.SaveExchangeRateAsync(rate);await Assert.ThrowsAsync<ConcurrencyConflictException>(()=>F.Generation.ApplyAsync(preview));
	}

	[Fact]
	public async Task SalesSchemaMigrationReachesAdvancedPricingVersion()
	{
		Assert.Equal(SalesSchemaMigration.CurrentVersion,Convert.ToInt32(await F.Data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';",CancellationToken.None)));
		Assert.Equal(1,Convert.ToInt32(await F.Data.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PricingExchangeRates';",CancellationToken.None)));
	}

	private static PriceListGenerationRequest Request(long priceListId,long itemId,BulkPricePricingMethod pricingMethod=BulkPricePricingMethod.PercentageMarkup,decimal markup=0m,decimal margin=0m,CommercialRoundingStrategy rounding=CommercialRoundingStrategy.CurrencyPrecision,DateTime? effectiveDate=null)=>new(){ExistingPriceListId=priceListId,FilterType=BulkPriceFilterType.SelectedItems,SelectedItemIds=[itemId],PricingMethod=pricingMethod,MarkupPercentage=markup,GrossMarginPercentage=margin,RoundingStrategy=rounding,ApplyMode=BulkPriceApplyMode.ReplaceCalculatedPrices,EffectiveDate=effectiveDate??DateTime.Today};

	public async Task InitializeAsync(){var factory=new SqliteConnectionFactory(_databasePath);new DepotDatabase(factory).Initialize();SalesSchemaMigration.Migrate(factory);_fixture=await Fixture.CreateAsync(factory);}
	public Task DisposeAsync(){SqliteConnection.ClearAllPools();if(File.Exists(_databasePath))File.Delete(_databasePath);return Task.CompletedTask;}

	private sealed class Fixture
	{
		private Fixture(DatabaseAccess data,ItemService items,ItemCostCalculationService costs,PriceListGenerationService generation,SalesPricingService pricing,SupplierItemService supplierItems,long supplierId){Data=data;Items=items;Costs=costs;Generation=generation;Pricing=pricing;SupplierItems=supplierItems;SupplierId=supplierId;}
		public DatabaseAccess Data{get;}public ItemService Items{get;}public ItemCostCalculationService Costs{get;}public PriceListGenerationService Generation{get;}public SalesPricingService Pricing{get;}public SupplierItemService SupplierItems{get;}public long SupplierId{get;}
		public static async Task<Fixture> CreateAsync(IDatabaseConnectionFactory factory)
		{
			var data=new DatabaseAccess(factory);var authorization=new AuthorizationService();var userRepository=new UserRepository(data);var admin=await userRepository.GetByEmailAsync("admin@depot.local",CancellationToken.None)??throw new InvalidOperationException("Default administrator missing.");authorization.SignIn(admin,PermissionCatalog.All);var auditRepository=new AuditRepository(data);var audit=new AuditService(auditRepository,authorization);var runner=new DatabaseTransactionRunner(data);var categories=new CategoryService(new CategoryRepository(data),audit);var manufacturers=new ManufacturerService(new ManufacturerRepository(data),audit);var units=new UnitOfMeasureService(new UnitOfMeasureRepository(data),audit);var packagings=new PackagingService(new PackagingRepository(data),audit);var supplierItemsRepository=new SupplierItemRepository(data);var itemRepository=new ItemRepository(data);var items=new ItemService(itemRepository,audit,manufacturers,categories,units,packagings,supplierItemsRepository);var supplierCategoryRepository=new SupplierCategoryRepository(data);var supplierCategories=new SupplierCategoryService(supplierCategoryRepository,audit);var supplierRepository=new SupplierRepository(data);var suppliers=new SupplierService(supplierRepository,supplierItemsRepository,supplierCategoryRepository,audit);var supplierItems=new SupplierItemService(supplierItemsRepository,supplierRepository,itemRepository,audit);var supplierCategory=await supplierCategories.SaveAsync(0,0,"Advanced Pricing Supplier",null);var supplier=await suppliers.SaveAsync(new Supplier{Name="Advanced Pricing Supplier",CustomerNumber="ADV-001",Contact="Pricing",Email="pricing@example.com",Phone="+49 30 123456",Address="Pricing Street 1",PaymentTerm="30 days net",SupplierCategoryId=supplierCategory.Id,Loyalty=100,Quality=100});var priceRepository=new SalesPriceListRepository(data);var pricing=new SalesPricingService(runner,priceRepository,auditRepository,audit,authorization);var costRepository=new ItemCostRepository(data);var costs=new ItemCostCalculationService(runner,costRepository,auditRepository,audit,authorization);var generation=new PriceListGenerationService(runner,costRepository,costs,priceRepository,pricing,auditRepository,audit,authorization);return new(data,items,costs,generation,pricing,supplierItems,supplier.Id);
		}
		public Task<Item> CreateItemAsync(string part)=>Items.CreateItemWithReferencesAsync(part,$"Item {part}",null,null,null,null,CancellationToken.None);
		public async Task<Item> CreateCostedItemAsync(string part,decimal purchasePrice,string currency){var item=await CreateItemAsync(part);await SupplierItems.SaveAsync(new(){SupplierId=SupplierId,ItemId=item.Id,SupplierPartNumber=$"SUP-{part}",PurchasePrice=purchasePrice,LeadTimeDays=1,MinimumOrderQuantity=1,IsPreferredSupplier=true,IsActive=true});await Costs.SaveProfileAsync(new(){ItemId=item.Id,Currency=currency});return item;}
		public Task<SalesPriceList> CreateListAsync(string code,string currency)=>Pricing.SaveAsync(new(){Code=code,Name=code,Scope=SalesPriceListScope.Global,Currency=currency,IsActive=false});
	}
}
