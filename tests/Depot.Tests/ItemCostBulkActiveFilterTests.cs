// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class ItemCostBulkActiveFilterTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-item-cost-filter-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task AllActiveItemsIncludesEveryActiveLifecycleAndExcludesInactiveItems()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		ItemMasterDataSchema.Ensure(factory);
		SalesSchemaMigration.Migrate(factory);
		var data = new DatabaseAccess(factory);
		var active = await InsertItemAsync(data, "ACTIVE", true, ItemLifecycleStatus.Active);
		var endOfLife = await InsertItemAsync(data, "END-OF-LIFE", true, ItemLifecycleStatus.EndOfLife);
		_ = await InsertItemAsync(data, "INACTIVE", false, ItemLifecycleStatus.Active);

		var authorization = new AuthorizationService();
		var admin = await new UserRepository(data).GetByEmailAsync("admin@depot.local", CancellationToken.None)
			?? throw new InvalidOperationException("Default administrator missing.");
		authorization.SignIn(admin, PermissionCatalog.All);
		var auditRepository = new AuditRepository(data);
		var audit = new AuditService(auditRepository, authorization);
		var runner = new DatabaseTransactionRunner(data);
		var costRepository = new ItemCostRepository(data);
		var costs = new ItemCostCalculationService(runner, costRepository, auditRepository, audit, authorization);
		var priceRepository = new SalesPriceListRepository(data);
		var pricing = new SalesPricingService(runner, priceRepository, auditRepository, audit, authorization);
		var generation = new PriceListGenerationService(runner, costRepository, costs, priceRepository, pricing, auditRepository, audit, authorization);

		var preview = await generation.PreviewAsync(new PriceListGenerationRequest
		{
			NewPriceList = new SalesPriceList { Code = "ACTIVE-FILTER", Name = "Active filter", Scope = SalesPriceListScope.Global, Currency = "EUR", IsActive = false },
			FilterType = BulkPriceFilterType.AllActiveItems,
			MarkupPercentage = 25m
		});

		Assert.Equal([active, endOfLife], preview.Rows.Select(row => row.ItemId).Order().ToArray());
		Assert.All(preview.Rows, row => Assert.Equal(BulkPricePreviewAction.Error, row.Action));
	}

	private static Task<long> InsertItemAsync(DatabaseAccess data, string partNumber, bool isActive, ItemLifecycleStatus lifecycleStatus) =>
		data.InsertAsync(
			"INSERT INTO Items (PartNumber,Description,IsActive,LifecycleStatus) VALUES ($PartNumber,$Description,$IsActive,$LifecycleStatus);",
			CancellationToken.None,
			new DatabaseParameter("$PartNumber", partNumber),
			new DatabaseParameter("$Description", $"Filter item {partNumber}"),
			new DatabaseParameter("$IsActive", isActive),
			new DatabaseParameter("$LifecycleStatus", (int)lifecycleStatus));

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
