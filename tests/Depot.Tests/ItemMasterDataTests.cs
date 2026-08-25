// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using Xunit;

namespace Depot.Tests;

public sealed class ItemMasterDataTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-item-master-data-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task MasterDataFieldsRoundTripThroughRepository()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();
		var repository = new ItemRepository(new DatabaseAccess(factory));
		var item = new Item
		{
			PartNumber = "MASTER-1",
			Description = "Master data test item",
			Gtin = "4006381333931",
			ItemType = ItemType.StockItem,
			LifecycleStatus = ItemLifecycleStatus.New,
			CountryOfOrigin = "DE",
			CustomsTariffNumber = "84733080",
			TrackingMode = ItemTrackingMode.SerialNumber,
			NetWeight = 1.25m,
			Length = 100m,
			Width = 75m,
			Height = 25m,
			Notes = "Repository round-trip",
			IsActive = true
		};

		item.Id = await repository.CreateMasterDataAsync(item, CancellationToken.None);
		var loaded = await repository.GetMasterDataByIdAsync(item.Id, CancellationToken.None)
			?? throw new InvalidOperationException("Created item was not found.");

		Assert.Equal(item.Gtin, loaded.Gtin);
		Assert.Equal(ItemLifecycleStatus.New, loaded.LifecycleStatus);
		Assert.Equal("DE", loaded.CountryOfOrigin);
		Assert.Equal("84733080", loaded.CustomsTariffNumber);
		Assert.Equal(ItemTrackingMode.SerialNumber, loaded.TrackingMode);
		Assert.Equal(1.25m, loaded.NetWeight);
		Assert.Equal(100m, loaded.Length);
		Assert.Equal("Repository round-trip", loaded.Notes);
	}

	public void Dispose()
	{
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
