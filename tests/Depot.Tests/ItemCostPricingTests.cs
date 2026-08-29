// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class ItemCostPricingTests : IAsyncLifetime
{
	private readonly string _databasePath=Path.Combine(Path.GetTempPath(),$"depot-item-cost-{Guid.NewGuid():N}.db");
	private CostingFixture? _fixture;
	private CostingFixture Fixture=>_fixture??throw new InvalidOperationException("Fixture is not initialized.");

	[Fact]
	public async Task CostBuildUpSupportsAbsoluteBaseAndRunningPercentagesDeterministically()
	{
		var item=await Fixture.CreateCostedItemAsync("COST-1000",1000m,"EUR");
		await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Overhead",CalculationType=ItemCostCalculationType.Percentage,CalculationBase=ItemCostCalculationBase.RunningTotal,Value=3m,Sequence=40});
		await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Handling",CalculationType=ItemCostCalculationType.Absolute,Value=15m,Sequence=30});
		await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Customs",CalculationType=ItemCostCalculationType.Percentage,CalculationBase=ItemCostCalculationBase.BaseCost,Value=4m,Sequence=20});
		await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Freight",CalculationType=ItemCostCalculationType.Absolute,Value=50m,Sequence=10});

		var result=await Fixture.Costs.CalculateAsync(item.Id,DateTime.Today,"EUR");

		Assert.True(result.IsSuccess);Assert.Equal(1000m,result.BaseCost);Assert.Equal(1138.15m,result.CalculatedCost);Assert.Equal(["Freight","Customs","Handling","Overhead"],result.Components.Select(c=>c.Name).ToArray());Assert.Equal(40m,result.Components[1].AppliedAmount);Assert.Equal(33.15m,result.Components[3].AppliedAmount);
	}

	[Fact]
	public async Task CostBuildUpHonorsValidityActivityStableSequenceAndCurrency()
	{
		var item=await Fixture.CreateCostedItemAsync("COST-VALID",100m,"EUR");
		var first=await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Stable A",CalculationType=ItemCostCalculationType.Absolute,Value=1m,Sequence=10});
		var second=await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Stable B",CalculationType=ItemCostCalculationType.Percentage,CalculationBase=ItemCostCalculationBase.RunningTotal,Value=10m,Sequence=10});
		await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Inactive",CalculationType=ItemCostCalculationType.Absolute,Value=99m,Sequence=1,IsActive=false});
		await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Future",CalculationType=ItemCostCalculationType.Absolute,Value=99m,Sequence=2,ValidFrom=DateTime.Today.AddDays(1)});
		await Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Expired",CalculationType=ItemCostCalculationType.Absolute,Value=99m,Sequence=3,ValidUntil=DateTime.Today.AddDays(-1)});

		var result=await Fixture.Costs.CalculateAsync(item.Id,DateTime.Today,"EUR");
		var mismatch=await Fixture.Costs.CalculateAsync(item.Id,DateTime.Today,"USD");

		Assert.Equal([first.Id,second.Id],result.Components.Select(c=>c.ComponentId).ToArray());Assert.Equal(111.10m,result.CalculatedCost);Assert.False(mismatch.IsSuccess);Assert.Contains("does not match",mismatch.Error,StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task InvalidComponentsAndCancellationFailClosed()
	{
		var item=await Fixture.CreateCostedItemAsync("COST-GUARD",80m,"EUR");
		await Assert.ThrowsAsync<ArgumentException>(()=>Fixture.Costs.SaveComponentAsync(new(){ItemId=item.Id,Name="Negative",CalculationType=ItemCostCalculationType.Absolute,Value=-1m,Sequence=10}));
		using var source=new CancellationTokenSource();source.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Fixture.Costs.CalculateAsync(item.Id,DateTime.Today,"EUR",source.Token));
		var noBase=await Fixture.CreateItemAsync("NO-BASE");await Fixture.Costs.SaveProfileAsync(new(){ItemId=noBase.Id,Currency="EUR"});var result=await Fixture.Costs.CalculateAsync(noBase.Id,DateTime.Today,"EUR");Assert.False(result.IsSuccess);Assert.Contains("preferred supplier",result.Error,StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task BulkPreviewUsesCalculatedCostMarkupFiltersAndApplyModes()
	{
		var category=await Fixture.Categories.SaveAsync(0,0,"Bulk Category",null);var manufacturer=await Fixture.Manufacturers.SaveAsync(0,0,"Bulk Manufacturer",null);
		var a=await Fixture.CreateCostedItemAsync("BULK-A",100m,"EUR",category.Id,manufacturer.Id);var b=await Fixture.CreateCostedItemAsync("BULK-B",80m,"EUR",category.Id,null);var c=await Fixture.CreateCostedItemAsync("BULK-C",120m,"EUR",null,manufacturer.Id);
		var list=await Fixture.Pricing.SaveAsync(new(){Code="BULK",Name="Bulk target",Scope=SalesPriceListScope.Global,Currency="EUR",IsActive=false});
		await Fixture.Pricing.SaveItemAsync(new(){SalesPriceListId=list.Id,ItemId=a.Id,UnitPrice=99m});await Fixture.Pricing.SaveItemAsync(new(){SalesPriceListId=list.Id,ItemId=b.Id,UnitPrice=110m});

		var categoryPreview=await Fixture.Generation.PreviewAsync(new(){ExistingPriceListId=list.Id,FilterType=BulkPriceFilterType.Category,FilterId=category.Id,MarkupPercentage=25m,ApplyMode=BulkPriceApplyMode.OnlyIncreasePrices});
		var manufacturerPreview=await Fixture.Generation.PreviewAsync(new(){ExistingPriceListId=list.Id,FilterType=BulkPriceFilterType.Manufacturer,FilterId=manufacturer.Id,MarkupPercentage=25m,ApplyMode=BulkPriceApplyMode.ReplaceCalculatedPrices});
		var selectedPreview=await Fixture.Generation.PreviewAsync(new(){ExistingPriceListId=list.Id,FilterType=BulkPriceFilterType.SelectedItems,SelectedItemIds=[c.Id],MarkupPercentage=25m,ApplyMode=BulkPriceApplyMode.OnlyCreateMissingPrices});

		Assert.Equal(2,categoryPreview.Rows.Count);Assert.Equal(125m,categoryPreview.Rows.Single(r=>r.ItemId==a.Id).CalculatedNewPrice);Assert.Equal(BulkPricePreviewAction.Update,categoryPreview.Rows.Single(r=>r.ItemId==a.Id).Action);Assert.Equal(BulkPricePreviewAction.Skip,categoryPreview.Rows.Single(r=>r.ItemId==b.Id).Action);Assert.Equal([a.Id,c.Id],manufacturerPreview.Rows.Select(r=>r.ItemId).Order().ToArray());Assert.Single(selectedPreview.Rows);Assert.Equal(BulkPricePreviewAction.Create,selectedPreview.Rows[0].Action);Assert.Equal(150m,selectedPreview.Rows[0].CalculatedNewPrice);
	}

	[Fact]
	public async Task BulkApplyMatchesPreviewIsAtomicAndDetectsConcurrency()
	{
		var item=await Fixture.CreateCostedItemAsync("APPLY",100m,"EUR");var list=await Fixture.Pricing.SaveAsync(new(){Code="APPLY",Name="Apply target",Scope=SalesPriceListScope.Global,Currency="EUR",IsActive=false});var existing=await Fixture.Pricing.SaveItemAsync(new(){SalesPriceListId=list.Id,ItemId=item.Id,UnitPrice=110m});
		var preview=await Fixture.Generation.PreviewAsync(new(){ExistingPriceListId=list.Id,FilterType=BulkPriceFilterType.SelectedItems,SelectedItemIds=[item.Id],MarkupPercentage=25m,ApplyMode=BulkPriceApplyMode.ReplaceCalculatedPrices});
		Assert.Equal(125m,preview.Rows[0].CalculatedNewPrice);
		existing.UnitPrice=111m;await Fixture.Pricing.SaveItemAsync(existing);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(()=>Fixture.Generation.ApplyAsync(preview));
		var fresh=await Fixture.Generation.PreviewAsync(preview.Request);var result=await Fixture.Generation.ApplyAsync(fresh);Assert.Equal(1,result.Updated);var stored=(await Fixture.Pricing.ListAsync()).Single(p=>p.Id==list.Id).Items.Single(i=>i.ItemId==item.Id);Assert.Equal(125m,stored.UnitPrice);
		Assert.Equal(1L,await Fixture.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType='PriceListGenerationAuditRecord' AND EntityId=$Id;",new DatabaseParameter("$Id",list.Id)));
	}

	[Fact]
	public async Task BulkPreviewReportsMissingCostAndCurrencyMismatchAndCanCreateScopedTarget()
	{
		var costed=await Fixture.CreateCostedItemAsync("NEW-LIST",100m,"EUR");var missing=await Fixture.CreateItemAsync("MISSING-COST");
		var errorPreview=await Fixture.Generation.PreviewAsync(new(){NewPriceList=new(){Code="USD",Name="USD preview",Scope=SalesPriceListScope.Customer,Currency="USD",IsActive=false},FilterType=BulkPriceFilterType.SelectedItems,SelectedItemIds=[costed.Id,missing.Id],MarkupPercentage=25m});Assert.Equal(2,errorPreview.ErrorCount);await Assert.ThrowsAsync<InvalidOperationException>(()=>Fixture.Generation.ApplyAsync(errorPreview));
		var preview=await Fixture.Generation.PreviewAsync(new(){NewPriceList=new(){Code="CUSTOMER-BULK",Name="Customer bulk",Scope=SalesPriceListScope.Customer,Currency="EUR",IsActive=false},FilterType=BulkPriceFilterType.SelectedItems,SelectedItemIds=[costed.Id],MarkupPercentage=25m});var applied=await Fixture.Generation.ApplyAsync(preview);Assert.Equal(1,applied.Created);var stored=(await Fixture.Pricing.ListAsync()).Single(p=>p.Id==applied.PriceListId);Assert.Equal(SalesPriceListScope.Customer,stored.Scope);Assert.Equal(125m,Assert.Single(stored.Items).UnitPrice);
	}

	public async Task InitializeAsync(){var factory=new SqliteConnectionFactory(_databasePath);new DepotDatabase(factory).Initialize();SalesSchemaMigration.Migrate(factory);_fixture=await CostingFixture.CreateAsync(factory);}
	public Task DisposeAsync(){SqliteConnection.ClearAllPools();if(File.Exists(_databasePath))File.Delete(_databasePath);return Task.CompletedTask;}

	private sealed class CostingFixture
	{
		private CostingFixture(DatabaseAccess data,ItemService items,ItemCostCalculationService costs,PriceListGenerationService generation,SalesPricingService pricing,SupplierItemService supplierItems,long supplierId,CategoryService categories,ManufacturerService manufacturers){Data=data;Items=items;Costs=costs;Generation=generation;Pricing=pricing;SupplierItems=supplierItems;SupplierId=supplierId;Categories=categories;Manufacturers=manufacturers;}
		public DatabaseAccess Data{get;}public ItemService Items{get;}public ItemCostCalculationService Costs{get;}public PriceListGenerationService Generation{get;}public SalesPricingService Pricing{get;}public SupplierItemService SupplierItems{get;}public long SupplierId{get;}public CategoryService Categories{get;}public ManufacturerService Manufacturers{get;}
		public static async Task<CostingFixture> CreateAsync(IDatabaseConnectionFactory factory)
		{
			var data=new DatabaseAccess(factory);var authorization=new AuthorizationService();var userRepository=new UserRepository(data);var admin=await userRepository.GetByEmailAsync("admin@depot.local",CancellationToken.None)??throw new InvalidOperationException("Default administrator missing.");authorization.SignIn(admin,PermissionCatalog.All);var auditRepository=new AuditRepository(data);var audit=new AuditService(auditRepository,authorization);var runner=new DatabaseTransactionRunner(data);var categories=new CategoryService(new CategoryRepository(data),audit);var manufacturers=new ManufacturerService(new ManufacturerRepository(data),audit);var units=new UnitOfMeasureService(new UnitOfMeasureRepository(data),audit);var packagings=new PackagingService(new PackagingRepository(data),audit);var supplierItemsRepository=new SupplierItemRepository(data);var itemRepository=new ItemRepository(data);var items=new ItemService(itemRepository,audit,manufacturers,categories,units,packagings,supplierItemsRepository);var supplierCategoryRepository=new SupplierCategoryRepository(data);var supplierCategories=new SupplierCategoryService(supplierCategoryRepository,audit);var supplierRepository=new SupplierRepository(data);var suppliers=new SupplierService(supplierRepository,supplierItemsRepository,supplierCategoryRepository,audit);var supplierItems=new SupplierItemService(supplierItemsRepository,supplierRepository,itemRepository,audit);var supplierCategory=await supplierCategories.SaveAsync(0,0,"Cost Supplier",null);var supplier=await suppliers.SaveAsync(new Supplier{Name="Cost Supplier",CustomerNumber="COST-001",Contact="Purchasing",Email="cost@example.com",Phone="+49 30 123456",Address="Cost Street 1",PaymentTerm="30 days net",SupplierCategoryId=supplierCategory.Id,Loyalty=100,Quality=100});var priceRepository=new SalesPriceListRepository(data);var pricing=new SalesPricingService(runner,priceRepository,auditRepository,audit,authorization);var costRepository=new ItemCostRepository(data);var costs=new ItemCostCalculationService(runner,costRepository,auditRepository,audit,authorization);var generation=new PriceListGenerationService(runner,costRepository,costs,priceRepository,pricing,auditRepository,audit,authorization);return new(data,items,costs,generation,pricing,supplierItems,supplier.Id,categories,manufacturers);
		}
		public Task<Item> CreateItemAsync(string part,long? categoryId=null,long? manufacturerId=null)=>Items.CreateItemWithReferencesAsync(part,$"Item {part}",manufacturerId,categoryId,null,null,CancellationToken.None);
		public async Task<Item> CreateCostedItemAsync(string part,decimal purchasePrice,string currency,long? categoryId=null,long? manufacturerId=null){var item=await CreateItemAsync(part,categoryId,manufacturerId);await SupplierItems.SaveAsync(new(){SupplierId=SupplierId,ItemId=item.Id,SupplierPartNumber=$"SUP-{part}",PurchasePrice=purchasePrice,LeadTimeDays=1,MinimumOrderQuantity=1,IsPreferredSupplier=true,IsActive=true});await Costs.SaveProfileAsync(new(){ItemId=item.Id,Currency=currency});return item;}
		public async Task<long> ScalarAsync(string sql,params DatabaseParameter[] parameters)=>Convert.ToInt64(await Data.ExecuteScalarAsync(sql,CancellationToken.None,parameters));
	}
}
