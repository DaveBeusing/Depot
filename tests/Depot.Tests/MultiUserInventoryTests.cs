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
	private readonly ItemService _itemService;
	private readonly PurposeService _purposeService;
	private readonly LocationService _locationService;
	private readonly InventoryManagementService _inventoryService;
	private readonly MovementService _movementService;

	public MultiUserInventoryTests()
	{
		_factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(_factory).Initialize();
		_itemRepository = new ItemRepository(_factory);
		_inventoryRepository = new InventoryRepository(_factory);
		_movementRepository = new StockMovementRepository(_factory);
		var purposeRepository = new PurposeRepository(_factory);
		var locationRepository = new LocationRepository(_factory);
		var authorization = new AuthorizationService();
		authorization.SignIn(new UserRepository(_factory).GetByEmail("admin@depot.local")!);
		var audit = new AuditService(new AuditRepository(_factory), authorization);
		_itemService = new ItemService(_itemRepository, audit);
		_purposeService = new PurposeService(purposeRepository, audit);
		_locationService = new LocationService(locationRepository, audit);
		_inventoryService = new InventoryManagementService(_inventoryRepository, audit);
		_movementService = new MovementService(
			_itemRepository,
			_inventoryRepository,
			purposeRepository,
			locationRepository,
			_movementRepository,
			audit);
	}

	[Fact]
	public async Task ConcurrentWithdrawalsCannotProduceNegativeStock()
	{
		var inventory = CreateInventory();
		_movementService.AddPurchase(inventory.Id, 10, 1m, null, null);

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
		_movementService.AddPurchase(inventory.Id, 5, 2m, "TEST", null);

		using var connection = _factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'StockMovement' AND EntityId IN (SELECT Id FROM StockMovements);";
		Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
	}

	private Inventory CreateInventory()
	{
		var suffix = Guid.NewGuid().ToString("N")[..8];
		var item = _itemService.CreateItem($"ITEM-{suffix}", "Test item", null, null);
		var purpose = _purposeService.GetOrCreatePurpose($"Purpose-{suffix}");
		var location = _locationService.GetOrCreateLocation($"Location-{suffix}");
		return _inventoryService.GetOrCreateInventory(item.Id, purpose.Id, location.Id);
	}

	private bool Withdraw(long inventoryId, int quantity)
	{
		try
		{
			_movementService.AddWithdrawal(inventoryId, quantity, null, null);
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
