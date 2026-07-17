// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SupplierManagementTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-suppliers-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task SupplierItemsEnforceReferencesAndSinglePreferredSupplier()
	{
		var context = CreateContext();
		var category = await context.SupplierCategoryService.SaveAsync(0, 0, "Electronic Suppliers", null);
		var first = await context.SupplierService.SaveAsync(CreateSupplier("First Supplier", "CUSTOMER-100", category.Id));
		var second = await context.SupplierService.SaveAsync(CreateSupplier("Second Supplier", "CUSTOMER-200", category.Id));
		Assert.True(first.AccountNumber > 0);
		Assert.NotEqual(first.AccountNumber, second.AccountNumber);
		Assert.Equal("CUSTOMER-100", first.CustomerNumber);
		Assert.Equal(100, first.Loyalty);
		Assert.Equal(100, first.Quality);
		var persistedFirst = Assert.Single(await context.SupplierService.SearchAsync("CUSTOMER-100"), value => value.Id == first.Id);
		Assert.Equal(first.AccountNumber, persistedFirst.AccountNumber);
		Assert.Equal("MANDATE-100", persistedFirst.SepaMandate);
		Assert.Equal("Electronic Suppliers", persistedFirst.SupplierCategoryName);
		var item = await context.ItemService.CreateItemWithReferencesAsync("PART-100", "Supplier test item", null, null, null, null, CancellationToken.None);

		var firstAssignment = await context.SupplierItemService.SaveAsync(new SupplierItem
		{
			SupplierId = first.Id, ItemId = item.Id, SupplierPartNumber = "FIRST-100", PurchasePrice = 12.50m,
			LeadTimeDays = 5, MinimumOrderQuantity = 10, IsPreferredSupplier = true
		});
		var secondAssignment = await context.SupplierItemService.SaveAsync(new SupplierItem
		{
			SupplierId = second.Id, ItemId = item.Id, SupplierPartNumber = "SECOND-100", PurchasePrice = 11.75m,
			LeadTimeDays = 7, MinimumOrderQuantity = 5, IsPreferredSupplier = true
		});

		Assert.False(Assert.Single(await context.SupplierItemService.SearchAsync(first.Id, null)).IsPreferredSupplier);
		Assert.True(Assert.Single(await context.SupplierItemService.SearchAsync(second.Id, null)).IsPreferredSupplier);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.SupplierService.SetActiveAsync(second.Id, second.Version, false));
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.SupplierCategoryService.SetActiveAsync(category.Id, category.Version, false));
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.ItemService.DeactivateItemAsync(item.Id, item.Version, CancellationToken.None));

		await context.SupplierItemService.SetActiveAsync(secondAssignment.Id, secondAssignment.Version, false);
		var deactivated = await context.SupplierService.SetActiveAsync(second.Id, second.Version, false);
		Assert.False(deactivated.IsActive);
		Assert.True(firstAssignment.IsActive);
	}

	[Fact]
	public void VersionElevenDirectSupplierLinksMigrateToSupplierItems()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		using (var connection = factory.CreateConnection())
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				INSERT INTO Suppliers (SupplierNumber, AccountNumber, Name) VALUES ('SUP-LEGACY', 999, 'Legacy Supplier');
				INSERT INTO Items (PartNumber, Description, SupplierId) VALUES ('LEGACY-ITEM', 'Legacy supplier item', last_insert_rowid());
				UPDATE DatabaseInfo SET Version = 11;
				""";
			command.ExecuteNonQuery();
		}

		new DepotDatabase(factory).Initialize();
		using var verification = factory.CreateConnection();
		verification.Open();
		using var verify = verification.CreateCommand();
		verify.CommandText = "SELECT COUNT(*) FROM SupplierItems WHERE SupplierPartNumber = 'LEGACY-ITEM' AND IsPreferredSupplier = 1;";
		Assert.Equal(1L, Convert.ToInt64(verify.ExecuteScalar()));
	}

	private TestContext CreateContext()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		var database = new DatabaseAccess(factory);
		var authorization = new AuthorizationService();
		var administrator = new UserRepository(database).GetByEmail("admin@depot.local")
			?? throw new InvalidOperationException("The test administrator was not initialized.");
		authorization.SignIn(administrator);
		var audit = new AuditService(new AuditRepository(database), authorization);
		var categoryRepository = new CategoryRepository(database);
		var categoryService = new CategoryService(categoryRepository, audit);
		var supplierCategoryRepository = new SupplierCategoryRepository(database);
		var supplierCategoryService = new SupplierCategoryService(supplierCategoryRepository, audit);
		var supplierRepository = new SupplierRepository(database);
		var supplierItemRepository = new SupplierItemRepository(database);
		var supplierService = new SupplierService(supplierRepository, supplierItemRepository, supplierCategoryRepository, audit);
		var itemRepository = new ItemRepository(database);
		var itemService = new ItemService(
			itemRepository, audit, new ManufacturerService(new ManufacturerRepository(database), audit), categoryService,
			new UnitOfMeasureService(new UnitOfMeasureRepository(database), audit),
			new PackagingService(new PackagingRepository(database), audit), supplierItemRepository);
		return new TestContext(
			supplierCategoryService,
			supplierService,
			new SupplierItemService(supplierItemRepository, supplierRepository, itemRepository, audit),
			itemService);
	}

	private static Supplier CreateSupplier(string name, string customerNumber, long categoryId) => new()
	{
		Name = name, CustomerNumber = customerNumber, Contact = "Purchasing", Email = "purchasing@example.com", Phone = "+49 30 123456",
		Address = "Example Street 1\n10115 Berlin", RmaTerms = "RMA approval required", Url = "https://example.com",
		PaymentTerm = "30 days net", Iban = "DE89370400440532013000", AccountName = name,
		SepaMandate = "MANDATE-100", VatNumber = "DE123456789", SupplierCategoryId = categoryId, Loyalty = 100, Quality = 100, Notes = "Preferred commercial terms"
	};

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}

	private sealed record TestContext(
		SupplierCategoryService SupplierCategoryService,
		SupplierService SupplierService,
		SupplierItemService SupplierItemService,
		ItemService ItemService);
}
