// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class MultiUserInventoryTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-test-{Guid.NewGuid():N}.db");
	private readonly SqliteConnectionFactory _factory;
	private readonly ItemRepository _itemRepository;
	private readonly InventoryRepository _inventoryRepository;
	private readonly StockMovementRepository _movementRepository;
	private readonly ReasonCodeRepository _reasonCodeRepository;
	private readonly ItemService _itemService;
	private readonly PurposeService _purposeService;
	private readonly WarehouseService _warehouseService;
	private readonly StorageLocationService _storageLocationService;
	private readonly InventoryManagementService _inventoryService;
	private readonly MovementService _movementService;
	private readonly ReasonCodeService _reasonCodeService;

	public MultiUserInventoryTests()
	{
		_factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(_factory).Initialize();
		var database = new DatabaseAccess(_factory);
		_itemRepository = new ItemRepository(database);
		_inventoryRepository = new InventoryRepository(database);
		_movementRepository = new StockMovementRepository(database);
		var purposeRepository = new PurposeRepository(database);
		var warehouseRepository = new WarehouseRepository(database);
		var storageLocationRepository = new StorageLocationRepository(database);
		_reasonCodeRepository = new ReasonCodeRepository(database);
		var authorization = new AuthorizationService();
		var administrator = new UserRepository(database).GetByEmail("admin@depot.local")
			?? throw new InvalidOperationException("The test administrator was not initialized.");
		authorization.SignIn(administrator);
		var audit = new AuditService(new AuditRepository(database), authorization);
		_itemService = new ItemService(_itemRepository, audit);
		_purposeService = new PurposeService(purposeRepository, audit);
		_warehouseService = new WarehouseService(warehouseRepository, storageLocationRepository, audit);
		_storageLocationService = new StorageLocationService(storageLocationRepository, warehouseRepository, audit);
		_reasonCodeService = new ReasonCodeService(_reasonCodeRepository, audit);
		_inventoryService = new InventoryManagementService(_inventoryRepository, audit);
		_movementService = new MovementService(
			_itemRepository,
			_inventoryRepository,
			purposeRepository,
			storageLocationRepository,
			warehouseRepository,
			_reasonCodeRepository,
			_movementRepository,
			audit);
	}

	[Fact]
	public async Task ConcurrentWithdrawalsCannotProduceNegativeStock()
	{
		var inventory = CreateInventory();
		_movementService.AddPurchase(inventory.Id, 10, 1m, null, null, null);

		var attempts = new[]
		{
			Task.Run(() => Withdraw(inventory.Id, 7)),
			Task.Run(() => Withdraw(inventory.Id, 7))
		};

		var results = await Task.WhenAll(attempts);
		Assert.Single(results, result => result);
		Assert.Equal(3, _movementRepository.GetByInventoryId(inventory.Id).Sum(movement => movement.Quantity));
	}

	[Fact]
	public void StaleItemUpdateIsRejected()
	{
		var item = _itemService.CreateItem("CON-1", "Initial", null, null);
		_itemService.UpdateItem(item.Id, item.Version, "First", null, null);

		Assert.Throws<ConcurrencyConflictException>(
			() => _itemService.UpdateItem(item.Id, 1, "Stale", null, null));
	}

	[Fact]
	public void MovementAndAuditAreCommittedTogether()
	{
		var inventory = CreateInventory();
		_movementService.AddPurchase(inventory.Id, 5, 2m, null, "TEST", null);

		using var connection = _factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'StockMovement' AND EntityId IN (SELECT Id FROM StockMovements);";
		Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
	}

	[Fact]
	public async Task ReasonCodeIsPersistedAndInactiveCodesCannotBeUsedForNewMovements()
	{
		var inventory = CreateInventory();
		var defaultNames = _reasonCodeRepository.GetAll().Select(item => item.Name).OrderBy(name => name).ToArray();
		Assert.Equal(
			new[]
			{
				"Consumed", "Damaged", "Demo", "Goods Issue", "Goods Receipt", "Inventory Correction",
				"Lost", "Repair", "Returned", "Transfer"
			},
			defaultNames);
		var reasonCode = Assert.Single(
			_reasonCodeRepository.GetAll(),
			item => item.Name == "Goods Receipt");

		_movementService.AddPurchase(inventory.Id, 5, 2m, reasonCode.Id, "REASON", null);

		var movement = Assert.Single(_movementRepository.GetByInventoryId(inventory.Id));
		Assert.Equal(reasonCode.Id, movement.ReasonCodeId);
		var overview = await _movementService.SearchAsync("Goods Receipt", 1, 10, CancellationToken.None);
		Assert.Contains(overview.Items, item => item.MovementId == movement.Id && item.ReasonCodeName == reasonCode.Name);

		reasonCode = await _reasonCodeService.SetActiveAsync(
			reasonCode.Id,
			reasonCode.Version,
			false);

		Assert.False(reasonCode.IsActive);
		Assert.Throws<InvalidOperationException>(
			() => _movementService.AddPurchase(inventory.Id, 1, 2m, reasonCode.Id, null, null));
	}

	private Inventory CreateInventory()
	{
		var suffix = Guid.NewGuid().ToString("N")[..8];
		var item = _itemService.CreateItem($"ITEM-{suffix}", "Test item", null, null);
		var purpose = _purposeService.GetOrCreatePurpose($"Purpose-{suffix}");
		var warehouse = _warehouseService.GetOrCreateAsync($"Warehouse-{suffix}").GetAwaiter().GetResult();
		var location = _storageLocationService.GetOrCreateAsync(warehouse.Id, $"Location-{suffix}").GetAwaiter().GetResult();
		return _inventoryService.GetOrCreateInventory(item.Id, purpose.Id, location.Id);
	}

	private bool Withdraw(long inventoryId, int quantity)
	{
		try
		{
			_movementService.AddWithdrawal(inventoryId, quantity, null, null, null);
			return true;
		}
		catch (InsufficientStockException)
		{
			return false;
		}
	}

	public void Dispose()
	{
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath))
		{
			File.Delete(_databasePath);
		}
	}
}
