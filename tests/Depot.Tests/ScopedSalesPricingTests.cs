// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ScopedSalesPricingTests : IAsyncLifetime
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-scoped-pricing-{Guid.NewGuid():N}.db");
	private PricingFixture? _fixture;

	[Fact]
	public async Task ResolvesCustomerRegionAndGlobalPerItem()
	{
		var fixture = Fixture;
		var region = await fixture.Pricing.SaveRegionAsync(new SalesRegion { Code = "DACH", Name = "DACH" });
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Scoped Customer", Currency = "EUR", SalesRegionId = region.Id });
		var global = await fixture.CreateListAsync("GLOBAL", "Global Standard", SalesPriceListScope.Global);
		var regional = await fixture.CreateListAsync("DACH", "DACH Standard", SalesPriceListScope.Region, region.Id);
		var customerList = await fixture.CreateListAsync("SPECIAL", "Customer Special", SalesPriceListScope.Customer);
		await fixture.Pricing.AssignCustomerAsync(customer.Id, customerList.Id);

		await fixture.AddPriceAsync(global.Id, fixture.ItemA, 100m);
		await fixture.AddPriceAsync(regional.Id, fixture.ItemA, 95m);
		await fixture.AddPriceAsync(customerList.Id, fixture.ItemA, 90m);
		await fixture.AddPriceAsync(global.Id, fixture.ItemB, 120m);
		await fixture.AddPriceAsync(regional.Id, fixture.ItemB, 110m);
		await fixture.AddPriceAsync(global.Id, fixture.ItemC, 150m);

		AssertPrice(await fixture.ResolveAsync(customer.Id, fixture.ItemA), 90m, SalesPriceListScope.Customer, "Customer Special");
		AssertPrice(await fixture.ResolveAsync(customer.Id, fixture.ItemB), 110m, SalesPriceListScope.Region, "DACH Standard");
		AssertPrice(await fixture.ResolveAsync(customer.Id, fixture.ItemC), 150m, SalesPriceListScope.Global, "Global Standard");
		Assert.Null(await fixture.ResolveAsync(customer.Id, fixture.ItemD));
	}

	[Fact]
	public async Task MissingCustomerAssignmentOrRegionContinuesWithAvailableDefaults()
	{
		var fixture = Fixture;
		var region = await fixture.Pricing.SaveRegionAsync(new SalesRegion { Code = "DACH", Name = "DACH" });
		var regionalCustomer = await fixture.Customers.SaveAsync(new Customer { Name = "Regional Customer", Currency = "EUR", SalesRegionId = region.Id });
		var globalCustomer = await fixture.Customers.SaveAsync(new Customer { Name = "Global Customer", Currency = "EUR" });
		var global = await fixture.CreateListAsync("GLOBAL", "Global Standard", SalesPriceListScope.Global);
		var regional = await fixture.CreateListAsync("DACH", "DACH Standard", SalesPriceListScope.Region, region.Id);
		await fixture.AddPriceAsync(global.Id, fixture.ItemA, 100m);
		await fixture.AddPriceAsync(regional.Id, fixture.ItemA, 95m);

		AssertPrice(await fixture.ResolveAsync(regionalCustomer.Id, fixture.ItemA), 95m, SalesPriceListScope.Region, "DACH Standard");
		AssertPrice(await fixture.ResolveAsync(globalCustomer.Id, fixture.ItemA), 100m, SalesPriceListScope.Global, "Global Standard");
	}

	[Fact]
	public async Task InvalidHigherScopesFallBackAndInvalidGlobalReturnsNoPrice()
	{
		var fixture = Fixture;
		var region = await fixture.Pricing.SaveRegionAsync(new SalesRegion { Code = "DACH", Name = "DACH" });
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Validity Customer", Currency = "EUR", SalesRegionId = region.Id });
		var global = await fixture.CreateListAsync("GLOBAL", "Global Standard", SalesPriceListScope.Global);
		var regional = await fixture.CreateListAsync("DACH", "DACH Standard", SalesPriceListScope.Region, region.Id);
		var expiredCustomer = await fixture.CreateListAsync("EXPIRED", "Expired Customer", SalesPriceListScope.Customer, validTo: DateTime.Today.AddDays(-1));
		await fixture.Pricing.AssignCustomerAsync(customer.Id, expiredCustomer.Id);
		await fixture.AddPriceAsync(global.Id, fixture.ItemA, 100m);
		await fixture.AddPriceAsync(regional.Id, fixture.ItemA, 95m);
		await fixture.AddPriceAsync(expiredCustomer.Id, fixture.ItemA, 90m);

		AssertPrice(await fixture.ResolveAsync(customer.Id, fixture.ItemA), 95m, SalesPriceListScope.Region, "DACH Standard");

		regional.IsActive = false;
		await fixture.Pricing.SaveAsync(regional);
		AssertPrice(await fixture.ResolveAsync(customer.Id, fixture.ItemA), 100m, SalesPriceListScope.Global, "Global Standard");

		global.ValidTo = DateTime.Today.AddDays(-1);
		await fixture.Pricing.SaveAsync(global);
		Assert.Null(await fixture.ResolveAsync(customer.Id, fixture.ItemA));
	}

	[Fact]
	public async Task CurrencyAndItemStatusArePartOfPriceValidity()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Currency Customer", Currency = "EUR" });
		var global = await fixture.CreateListAsync("GLOBAL", "Global Standard", SalesPriceListScope.Global);
		var customerUsd = await fixture.CreateListAsync("USD-SPECIAL", "USD Customer", SalesPriceListScope.Customer, currency: "USD");
		await fixture.Pricing.AssignCustomerAsync(customer.Id, customerUsd.Id);
		await fixture.AddPriceAsync(global.Id, fixture.ItemA, 100m);
		await fixture.AddPriceAsync(customerUsd.Id, fixture.ItemA, 80m);

		AssertPrice(await fixture.ResolveAsync(customer.Id, fixture.ItemA), 100m, SalesPriceListScope.Global, "Global Standard");
		await fixture.Data.ExecuteAsync("UPDATE Items SET IsActive=0 WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", fixture.ItemA));
		Assert.Null(await fixture.ResolveAsync(customer.Id, fixture.ItemA));
	}

	[Fact]
	public async Task DefaultActivationIsUniqueUnderConcurrentWrites()
	{
		var fixture = Fixture;
		var first = await fixture.CreateListAsync("GLOBAL-A", "Global A", SalesPriceListScope.Global, active: false);
		var second = await fixture.CreateListAsync("GLOBAL-B", "Global B", SalesPriceListScope.Global, active: false);
		first.IsActive = true;
		second.IsActive = true;

		var outcomes = await Task.WhenAll(TryActivateAsync(fixture.Pricing, first), TryActivateAsync(fixture.Pricing, second));

		Assert.Single(outcomes, value => value);
		Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM SalesPriceLists WHERE Scope=0 AND IsActive=1;"));
	}

	[Fact]
	public async Task RegionalDefaultsAreUniquePerRegionButIndependentAcrossRegions()
	{
		var fixture = Fixture;
		var dach = await fixture.Pricing.SaveRegionAsync(new SalesRegion { Code = "DACH", Name = "DACH" });
		var apac = await fixture.Pricing.SaveRegionAsync(new SalesRegion { Code = "APAC", Name = "APAC" });
		await fixture.CreateListAsync("DACH-A", "DACH A", SalesPriceListScope.Region, dach.Id);
		await fixture.CreateListAsync("APAC-A", "APAC A", SalesPriceListScope.Region, apac.Id);

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateListAsync("DACH-B", "DACH B", SalesPriceListScope.Region, dach.Id));
		Assert.Equal(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM SalesPriceLists WHERE Scope=1 AND IsActive=1;"));
	}

	[Fact]
	public async Task PriceListUpdatesUseOptimisticConcurrency()
	{
		var fixture = Fixture;
		var saved = await fixture.CreateListAsync("CUSTOMER", "Customer Pricing", SalesPriceListScope.Customer, active: false);
		var first = Clone(saved);
		var stale = Clone(saved);
		first.Name = "First Update";
		stale.Name = "Stale Update";

		await fixture.Pricing.SaveAsync(first);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => fixture.Pricing.SaveAsync(stale));
	}

	[Fact]
	public async Task ResolutionHonorsCancellationAndRejectsInvalidQuantity()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Cancellation Customer", Currency = "EUR" });
		using var source = new CancellationTokenSource();
		source.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Pricing.ResolveAsync(customer.Id, fixture.ItemA, 1, DateTime.Today, "EUR", source.Token));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Pricing.ResolveAsync(customer.Id, fixture.ItemA, 0, DateTime.Today, "EUR"));
	}

	[Fact]
	public async Task ScopedPricingMutationsAreAuditedAndRequireManagementPermission()
	{
		var fixture = Fixture;
		var region = await fixture.Pricing.SaveRegionAsync(new SalesRegion { Code = "AUDIT", Name = "Audit Region" });
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Audit Customer", Currency = "EUR", SalesRegionId = region.Id });
		var list = await fixture.CreateListAsync("AUDIT-CUSTOMER", "Audit Customer Pricing", SalesPriceListScope.Customer);
		await fixture.AddPriceAsync(list.Id, fixture.ItemA, 42m);
		await fixture.Pricing.AssignCustomerAsync(customer.Id, list.Id);

		Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType='SalesRegion' AND EntityId=$Id;", new DatabaseParameter("$Id", region.Id)));
		Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType='SalesPriceList' AND EntityId=$Id;", new DatabaseParameter("$Id", list.Id)));
		Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType='CustomerPriceListAssignment' AND EntityId=$Id;", new DatabaseParameter("$Id", customer.Id)));

		var readOnlyAuthorization = new AuthorizationService();
		readOnlyAuthorization.SignIn(new User { Id = fixture.AdministratorId, Email = "read-only@depot.test", IsActive = true }, [ApplicationPermission.SalesPricingView]);
		var auditRepository = new AuditRepository(fixture.Data);
		var readOnlyPricing = new SalesPricingService(new DatabaseTransactionRunner(fixture.Data), new SalesPriceListRepository(fixture.Data), auditRepository, new AuditService(auditRepository, readOnlyAuthorization), readOnlyAuthorization);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => readOnlyPricing.SaveAsync(new SalesPriceList { Code = "DENIED", Name = "Denied", Scope = SalesPriceListScope.Global }));
	}

	[Fact]
	public async Task AuditFailureRollsBackPriceListMutation()
	{
		var fixture = Fixture;
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = long.MaxValue, Email = "missing-audit-user@depot.test", IsActive = true }, PermissionCatalog.All);
		var auditRepository = new AuditRepository(fixture.Data);
		var pricing = new SalesPricingService(new DatabaseTransactionRunner(fixture.Data), new SalesPriceListRepository(fixture.Data), auditRepository, new AuditService(auditRepository, authorization), authorization);
		var before = await fixture.ScalarAsync("SELECT COUNT(*) FROM SalesPriceLists;");

		await Assert.ThrowsAnyAsync<Exception>(() => pricing.SaveAsync(new SalesPriceList { Code = "ROLLBACK", Name = "Rollback", Scope = SalesPriceListScope.Global }));

		Assert.Equal(before, await fixture.ScalarAsync("SELECT COUNT(*) FROM SalesPriceLists;"));
	}

	[Fact]
	public async Task SalesOrdersRefreshOnlyAutomaticDraftPricesAndPreserveSubmittedSnapshots()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Snapshot Customer", Currency = "EUR" });
		var global = await fixture.CreateListAsync("GLOBAL", "Global Standard", SalesPriceListScope.Global);
		var item = await fixture.AddPriceAsync(global.Id, fixture.ItemA, 100m);
		var resolved = await fixture.ResolveAsync(customer.Id, fixture.ItemA) ?? throw new InvalidOperationException();
		var line = new SalesOrderLine { ItemId = fixture.ItemA, Quantity = 2, TaxRate = 19m };
		resolved.ApplyTo(line);
		var draft = await fixture.Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Currency = "EUR", Lines = [line] });
		AssertPriceSource(Assert.Single(draft.Lines), 100m, SalesPriceListScope.Global, "Global Standard");

		item.UnitPrice = 125m;
		await fixture.Pricing.SaveItemAsync(item);
		var storedBeforeRefresh = await fixture.Orders.GetByIdAsync(draft.Id) ?? throw new InvalidOperationException();
		AssertPriceSource(Assert.Single(storedBeforeRefresh.Lines), 100m, SalesPriceListScope.Global, "Global Standard");

		var refreshed = await fixture.Orders.SaveDraftAsync(storedBeforeRefresh);
		AssertPriceSource(Assert.Single(refreshed.Lines), 125m, SalesPriceListScope.Global, "Global Standard");
		var submitted = await fixture.Orders.SubmitAsync(refreshed.Id, refreshed.Version);

		item.UnitPrice = 150m;
		await fixture.Pricing.SaveItemAsync(item);
		var historical = await fixture.Orders.GetByIdAsync(submitted.Id) ?? throw new InvalidOperationException();
		AssertPriceSource(Assert.Single(historical.Lines), 125m, SalesPriceListScope.Global, "Global Standard");
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Orders.SaveDraftAsync(historical));
	}

	public async Task InitializeAsync()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		_fixture = await PricingFixture.CreateAsync(factory);
	}

	public Task DisposeAsync()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
		return Task.CompletedTask;
	}

	private PricingFixture Fixture => _fixture ?? throw new InvalidOperationException("Pricing fixture is not initialized.");

	private static void AssertPrice(SalesPriceResult? actual, decimal price, SalesPriceListScope scope, string source)
	{
		Assert.NotNull(actual);
		Assert.Equal(price, actual.UnitPrice);
		Assert.Equal(scope, actual.Scope);
		Assert.Equal(source, actual.PriceListName);
		Assert.Equal("EUR", actual.Currency);
	}

	private static void AssertPriceSource(SalesOrderLine actual, decimal price, SalesPriceListScope scope, string source)
	{
		Assert.Equal(price, actual.UnitPrice);
		Assert.NotNull(actual.PriceSourceListId);
		Assert.Equal(scope, actual.PriceSourceScope);
		Assert.Equal(source, actual.PriceSourceName);
		Assert.Equal("EUR", actual.PriceSourceCurrency);
	}

	private static async Task<bool> TryActivateAsync(SalesPricingService pricing, SalesPriceList list)
	{
		try { await pricing.SaveAsync(list); return true; }
		catch (InvalidOperationException) { return false; }
		catch (SqliteException) { return false; }
	}

	private static SalesPriceList Clone(SalesPriceList source) => new()
	{
		Id = source.Id,
		Code = source.Code,
		Name = source.Name,
		Scope = source.Scope,
		RegionId = source.RegionId,
		RegionName = source.RegionName,
		Currency = source.Currency,
		ValidFrom = source.ValidFrom,
		ValidTo = source.ValidTo,
		IsActive = source.IsActive,
		Version = source.Version
	};

	private sealed class PricingFixture
	{
		private PricingFixture(DatabaseAccess data, CustomerService customers, SalesPricingService pricing, SalesOrderService orders, long administratorId, long[] itemIds)
		{
			Data = data;
			Customers = customers;
			Pricing = pricing;
			Orders = orders;
			AdministratorId = administratorId;
			ItemA = itemIds[0];
			ItemB = itemIds[1];
			ItemC = itemIds[2];
			ItemD = itemIds[3];
		}

		public DatabaseAccess Data { get; }
		public CustomerService Customers { get; }
		public SalesPricingService Pricing { get; }
		public SalesOrderService Orders { get; }
		public long AdministratorId { get; }
		public long ItemA { get; }
		public long ItemB { get; }
		public long ItemC { get; }
		public long ItemD { get; }

		public static async Task<PricingFixture> CreateAsync(IDatabaseConnectionFactory factory)
		{
			var data = new DatabaseAccess(factory);
			var authorization = new AuthorizationService();
			var roles = new RoleRepository(data);
			var users = new UserRepository(data);
			var admin = await users.GetByEmailAsync("admin@depot.local", CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
			admin.Roles = await roles.GetUserRolesAsync(admin.Id, CancellationToken.None);
			admin.EffectivePermissions = PermissionCatalog.All;
			authorization.SignIn(admin, PermissionCatalog.All);
			var itemIds = new long[4];
			for (var index = 0; index < itemIds.Length; index++)
			{
				itemIds[index] = await data.InsertAsync(
					"INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($PartNumber,$Description,1);",
					CancellationToken.None,
					new DatabaseParameter("$PartNumber", $"PRICE-{index}-{Guid.NewGuid():N}"),
					new DatabaseParameter("$Description", $"Scoped price item {index}"));
			}
			var auditRepository = new AuditRepository(data);
			var audit = new AuditService(auditRepository, authorization);
			var runner = new DatabaseTransactionRunner(data);
			var pricing = new SalesPricingService(runner, new SalesPriceListRepository(data), auditRepository, audit, authorization);
			var customerRepository = new CustomerRepository(data);
			var customers = new CustomerService(customerRepository, audit, authorization);
			var notifications = new NotificationService(runner, new NotificationRepository(data), authorization);
			var orders = new SalesOrderService(
				runner,
				new SalesOrderRepository(data),
				customerRepository,
				new ItemRepository(data),
				new InventoryRepository(data),
				new InventoryReservationRepository(data),
				new StockMovementRepository(data),
				auditRepository,
				audit,
				authorization,
				notifications,
				pricing: pricing);
			return new PricingFixture(data, customers, pricing, orders, admin.Id, itemIds);
		}

		public Task<SalesPriceResult?> ResolveAsync(long customerId, long itemId) => Pricing.ResolveAsync(customerId, itemId, 1, DateTime.Today, "EUR");

		public Task<SalesPriceList> CreateListAsync(string code, string name, SalesPriceListScope scope, long? regionId = null, bool active = true, DateTime? validTo = null, string currency = "EUR") => Pricing.SaveAsync(new SalesPriceList
		{
			Code = code,
			Name = name,
			Scope = scope,
			RegionId = regionId,
			Currency = currency,
			ValidTo = validTo,
			IsActive = active
		});

		public Task<SalesPriceListItem> AddPriceAsync(long listId, long itemId, decimal price) => Pricing.SaveItemAsync(new SalesPriceListItem
		{
			SalesPriceListId = listId,
			ItemId = itemId,
			UnitPrice = price
		});

		public async Task<long> ScalarAsync(string sql, params DatabaseParameter[] parameters)
		{
			var value = await Data.ExecuteScalarAsync(sql, CancellationToken.None, parameters);
			return Convert.ToInt64(value);
		}
	}
}
