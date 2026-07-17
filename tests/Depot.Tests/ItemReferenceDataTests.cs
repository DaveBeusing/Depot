// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ItemReferenceDataTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-reference-data-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionTenItemStringsAreMigratedToForeignKeys()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		using (var connection = factory.CreateConnection())
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				INSERT INTO Items (PartNumber, Description, Manufacturer, Category) VALUES ('MIG-1', 'Migrated item', 'Legacy Maker', 'Legacy Category');
				UPDATE DatabaseInfo SET Version = 10;
				""";
			command.ExecuteNonQuery();
		}

		new DepotDatabase(factory).Initialize();
		var migrated = new ItemRepository(new DatabaseAccess(factory)).GetByPartNumber("MIG-1")
			?? throw new InvalidOperationException("Migrated item was not found.");
		Assert.Equal("Legacy Maker", migrated.Manufacturer);
		Assert.Equal("Legacy Category", migrated.Category);
		Assert.NotNull(migrated.ManufacturerId);
		Assert.NotNull(migrated.CategoryId);
	}

	[Fact]
	public async Task ReferencedMasterDataCannotBeDeactivated()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		var database = new DatabaseAccess(factory);
		var authorization = new AuthorizationService();
		var administrator = new UserRepository(database).GetByEmail("admin@depot.local")
			?? throw new InvalidOperationException("The test administrator was not initialized.");
		authorization.SignIn(administrator);
		var audit = new AuditService(new AuditRepository(database), authorization);
		var manufacturerService = new ManufacturerService(new ManufacturerRepository(database), audit);
		var categoryService = new CategoryService(new CategoryRepository(database), audit);
		var unitService = new UnitOfMeasureService(new UnitOfMeasureRepository(database), audit);
		var packagingService = new PackagingService(new PackagingRepository(database), audit);
		var itemService = new ItemService(
			new ItemRepository(database),
			audit,
			manufacturerService,
			categoryService,
			unitService,
			packagingService,
			new SupplierItemRepository(database));

		var manufacturer = await manufacturerService.SaveAsync(0, 0, "Contoso", "Test manufacturer");
		var item = await itemService.CreateItemWithReferencesAsync(
			"REF-1",
			"Reference test item",
			manufacturer.Id,
			null,
			null,
			null,
			CancellationToken.None);

		Assert.Equal(manufacturer.Id, item.ManufacturerId);
		Assert.Equal("Contoso", item.Manufacturer);
		await Assert.ThrowsAsync<InvalidOperationException>(
			() => manufacturerService.SetActiveAsync(manufacturer.Id, manufacturer.Version, false));

		item = await itemService.UpdateItemWithReferencesAsync(
			item.Id,
			item.Version,
			item.Description,
			null,
			null,
			null,
			null,
			CancellationToken.None);
		var deactivated = await manufacturerService.SetActiveAsync(manufacturer.Id, manufacturer.Version, false);
		Assert.False(deactivated.IsActive);
	}

	public void Dispose()
	{
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
