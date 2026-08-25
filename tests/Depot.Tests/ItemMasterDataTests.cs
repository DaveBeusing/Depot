// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ItemMasterDataTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-item-master-data-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task MasterDataFieldsRoundTripThroughRepository()
	{
		var factory = InitializeDatabase();
		var repository = new ItemRepository(new DatabaseAccess(factory));
		var item = new Item
		{
			PartNumber = "MASTER-1",
			Description = "Master data test item",
			Gtin = "4006381333931",
			ItemType = ItemType.StockItem,
			LifecycleStatus = ItemLifecycleStatus.EndOfLife,
			Revision = "A02",
			Model = "Vertex Compute",
			ProductFamily = "Vertex",
			CountryOfOrigin = "DE",
			CustomsTariffNumber = "84733080",
			Eccn = "5A992.C",
			TrackingMode = ItemTrackingMode.SerialNumber,
			NetWeightKg = 1.25m,
			GrossWeightKg = 1.50m,
			LengthMm = 100m,
			WidthMm = 75m,
			HeightMm = 25m,
			IsDangerousGoods = true,
			UnNumber = "UN3481",
			ContainsBattery = true,
			RohsStatus = ItemComplianceStatus.Compliant,
			ReachStatus = ItemComplianceStatus.Compliant,
			IntroductionDate = new DateTime(2026, 1, 10),
			EndOfLifeDate = new DateTime(2028, 6, 30),
			LastBuyDate = new DateTime(2028, 9, 30),
			EndOfSupportDate = new DateTime(2031, 6, 30),
			Notes = "Repository round-trip",
			IsActive = true
		};

		item.Id = await repository.CreateMasterDataAsync(item, CancellationToken.None);
		var loaded = await repository.GetMasterDataByIdAsync(item.Id, CancellationToken.None)
			?? throw new InvalidOperationException("Created item was not found.");

		Assert.Equal(item.Gtin, loaded.Gtin);
		Assert.Equal(ItemLifecycleStatus.EndOfLife, loaded.LifecycleStatus);
		Assert.Equal("A02", loaded.Revision);
		Assert.Equal("Vertex Compute", loaded.Model);
		Assert.Equal("Vertex", loaded.ProductFamily);
		Assert.Equal("DE", loaded.CountryOfOrigin);
		Assert.Equal("84733080", loaded.CustomsTariffNumber);
		Assert.Equal("5A992.C", loaded.Eccn);
		Assert.Equal(ItemTrackingMode.SerialNumber, loaded.TrackingMode);
		Assert.Equal(1.25m, loaded.NetWeightKg);
		Assert.Equal(1.50m, loaded.GrossWeightKg);
		Assert.Equal(100m, loaded.LengthMm);
		Assert.Equal(75m, loaded.WidthMm);
		Assert.Equal(25m, loaded.HeightMm);
		Assert.True(loaded.IsDangerousGoods);
		Assert.Equal("UN3481", loaded.UnNumber);
		Assert.True(loaded.ContainsBattery);
		Assert.Equal(ItemComplianceStatus.Compliant, loaded.RohsStatus);
		Assert.Equal(ItemComplianceStatus.Compliant, loaded.ReachStatus);
		Assert.Equal(new DateTime(2026, 1, 10), loaded.IntroductionDate);
		Assert.Equal(new DateTime(2028, 6, 30), loaded.EndOfLifeDate);
		Assert.Equal(new DateTime(2028, 9, 30), loaded.LastBuyDate);
		Assert.Equal(new DateTime(2031, 6, 30), loaded.EndOfSupportDate);
		Assert.Equal("Repository round-trip", loaded.Notes);
	}

	[Fact]
	public async Task GtinIsUniqueAtDatabaseBoundary()
	{
		var factory = InitializeDatabase();
		var repository = new ItemRepository(new DatabaseAccess(factory));
		var first = new Item
		{
			PartNumber = "GTIN-1",
			Description = "First",
			Gtin = "4006381333931",
			IsActive = true
		};
		var second = new Item
		{
			PartNumber = "GTIN-2",
			Description = "Second",
			Gtin = "4006381333931",
			IsActive = true
		};

		await repository.CreateMasterDataAsync(first, CancellationToken.None);
		await Assert.ThrowsAsync<SqliteException>(() => repository.CreateMasterDataAsync(second, CancellationToken.None));
	}

	[Fact]
	public void ItemMasterDataSchemaInitializationIsIdempotent()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		var initializer = DatabaseProviderFactory.CreateInitializer(factory);

		initializer.Initialize();
		initializer.Initialize();

		using var connection = factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Items') WHERE name IN ('GrossWeightKg', 'Eccn', 'EndOfSupportDate');";
		Assert.Equal(3, Convert.ToInt32(command.ExecuteScalar()));
	}

	private SqliteConnectionFactory InitializeDatabase()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();
		return factory;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
