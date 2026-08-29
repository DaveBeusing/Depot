// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
public sealed class ScopedSalesPricingProviderTests
{
	[SqlServerProcurementFact]
	public Task SqlServerResolvesScopedPriceFallback()
	{
		var settings = ProcurementProviderConfiguration.GetSqlServerSettings();
		var factory = new SqlServerConnectionFactory(settings);
		return VerifyScopedResolutionAsync(factory, new SqlServerDatabase(factory));
	}

	[MySqlProcurementFact]
	public Task MySqlOrMariaDbResolvesScopedPriceFallback()
	{
		var settings = ProcurementProviderConfiguration.GetMySqlSettings();
		var factory = new MySqlConnectionFactory(settings);
		return VerifyScopedResolutionAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyScopedResolutionAsync(IDatabaseConnectionFactory factory, IDatabaseInitializer initializer)
	{
		initializer.Initialize();
		SalesSchemaMigration.Migrate(factory);
		var data = new DatabaseAccess(factory);
		var authorization = new AuthorizationService();
		var roles = new RoleRepository(data);
		var users = new UserRepository(data);
		var admin = await users.GetByEmailAsync("admin@depot.local", CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		admin.Roles = await roles.GetUserRolesAsync(admin.Id, CancellationToken.None);
		admin.EffectivePermissions = PermissionCatalog.All;
		authorization.SignIn(admin, PermissionCatalog.All);
		var auditRepository = new AuditRepository(data);
		var audit = new AuditService(auditRepository, authorization);
		var pricing = new SalesPricingService(new DatabaseTransactionRunner(data), new SalesPriceListRepository(data), auditRepository, audit, authorization);
		var customers = new CustomerService(new CustomerRepository(data), audit, authorization);
		var suffix = Guid.NewGuid().ToString("N");
		var itemWithRegionalPrice = await InsertItemAsync(data, $"PROVIDER-REGION-{suffix}");
		var itemWithGlobalPrice = await InsertItemAsync(data, $"PROVIDER-GLOBAL-{suffix}");
		var region = await pricing.SaveRegionAsync(new SalesRegion { Code = $"R-{suffix}", Name = $"Provider Region {suffix}" });
		var customer = await customers.SaveAsync(new Customer { Name = $"Provider Pricing Customer {suffix}", Currency = "EUR", SalesRegionId = region.Id });
		var global = await pricing.SaveAsync(new SalesPriceList { Code = $"G-{suffix}", Name = $"Provider Global {suffix}", Scope = SalesPriceListScope.Global, Currency = "EUR" });
		var regional = await pricing.SaveAsync(new SalesPriceList { Code = $"RPL-{suffix}", Name = $"Provider Regional {suffix}", Scope = SalesPriceListScope.Region, RegionId = region.Id, Currency = "EUR" });
		var customerList = await pricing.SaveAsync(new SalesPriceList { Code = $"C-{suffix}", Name = $"Provider Customer {suffix}", Scope = SalesPriceListScope.Customer, Currency = "EUR", IsActive = false });
		await pricing.AssignCustomerAsync(customer.Id, customerList.Id);
		customerList.IsActive = true;
		await pricing.SaveAsync(customerList);
		await pricing.SaveItemAsync(new SalesPriceListItem { SalesPriceListId = regional.Id, ItemId = itemWithRegionalPrice, UnitPrice = 81m });
		await pricing.SaveItemAsync(new SalesPriceListItem { SalesPriceListId = global.Id, ItemId = itemWithRegionalPrice, UnitPrice = 91m });
		await pricing.SaveItemAsync(new SalesPriceListItem { SalesPriceListId = global.Id, ItemId = itemWithGlobalPrice, UnitPrice = 101m });

		var regionalResult = await pricing.ResolveAsync(customer.Id, itemWithRegionalPrice, 1, DateTime.Today, "EUR");
		var globalResult = await pricing.ResolveAsync(customer.Id, itemWithGlobalPrice, 1, DateTime.Today, "EUR");

		Assert.NotNull(regionalResult);
		Assert.Equal(81m, regionalResult.UnitPrice);
		Assert.Equal(SalesPriceListScope.Region, regionalResult.Scope);
		Assert.NotNull(globalResult);
		Assert.Equal(101m, globalResult.UnitPrice);
		Assert.Equal(SalesPriceListScope.Global, globalResult.Scope);
	}

	private static Task<long> InsertItemAsync(DatabaseAccess data, string partNumber) => data.InsertAsync(
		"INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($PartNumber,$Description,1);",
		CancellationToken.None,
		new DatabaseParameter("$PartNumber", partNumber),
		new DatabaseParameter("$Description", "Provider scoped-pricing item"));
}

[CollectionDefinition("Provider database", DisableParallelization = true)]
public sealed class ProviderDatabaseCollection;
