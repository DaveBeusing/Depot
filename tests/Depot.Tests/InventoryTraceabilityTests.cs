// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class InventoryTraceabilityTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-traceability-{Guid.NewGuid():N}.db");

	[Fact]
	public void ParserSupportsSerialAndLotCaptureSyntax()
	{
		var serials = TrackingAllocationTextParser.ParseUnspecified("SN-001\nSN-002|2027-12-31");
		var lots = TrackingAllocationTextParser.ParseUnspecified("LOT-A|12\nLOT-B|8|2027-10-15");

		Assert.Collection(serials,
			first => { Assert.Equal("SN-001", first.Code); Assert.Equal(1, first.Quantity); Assert.Null(first.ExpiryDate); },
			second => { Assert.Equal("SN-002", second.Code); Assert.Equal(1, second.Quantity); Assert.Equal(new DateTime(2027, 12, 31), second.ExpiryDate); });
		Assert.Collection(lots,
			first => { Assert.Equal("LOT-A", first.Code); Assert.Equal(12, first.Quantity); },
			second => { Assert.Equal("LOT-B", second.Code); Assert.Equal(8, second.Quantity); Assert.Equal(new DateTime(2027, 10, 15), second.ExpiryDate); });
	}

	[Fact]
	public void NewCaptureReplacesStaleCaptureForSameWorkflowInventory()
	{
		const string scope = "material-issue";
		const long oldLine = 910001;
		const long currentLine = 910002;
		TrackingCaptureSession.Set(scope, oldLine, "SN-OLD", 42);
		TrackingCaptureSession.Set(scope, currentLine, "SN-CURRENT", 42);
		try
		{
			var allocation = Assert.Single(TrackingCaptureSession.ResolveForInventory(scope, 42, -1));
			Assert.Equal("SN-CURRENT", allocation.Code);
			Assert.Null(TrackingCaptureSession.GetText(scope, oldLine));
		}
		finally
		{
			TrackingCaptureSession.Clear(scope, [oldLine, currentLine]);
		}
	}

	[Fact]
	public void OperationalPoliciesRejectInvalidPhysicalAndLifecycleUse()
	{
		var serviceItem = new InventoryItemPolicy { PartNumber = "SERVICE-1", ItemType = ItemType.Service, LifecycleStatus = ItemLifecycleStatus.Active };
		var obsolete = new InventoryItemPolicy { PartNumber = "OLD-1", ItemType = ItemType.StockItem, LifecycleStatus = ItemLifecycleStatus.Obsolete, ReplacementPartNumber = "NEW-1" };
		var lastBuy = new InventoryItemPolicy { PartNumber = "LAST-1", ItemType = ItemType.StockItem, LifecycleStatus = ItemLifecycleStatus.Active, LastBuyDate = new DateTime(2026, 8, 1) };

		Assert.Throws<InvalidOperationException>(() => ItemTraceabilityService.EnsurePhysicalStockItem(serviceItem, "inventory movement"));
		Assert.Contains("NEW-1", Assert.Throws<InvalidOperationException>(() => ItemTraceabilityService.EnsureSellable(obsolete)).Message, StringComparison.Ordinal);
		Assert.Throws<InvalidOperationException>(() => ItemTraceabilityService.EnsurePurchasable(lastBuy, new DateTime(2026, 8, 2)));
	}

	[Fact]
	public void TraceabilitySchemaInitializationIsIdempotent()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		var initializer = DatabaseProviderFactory.CreateInitializer(factory);

		initializer.Initialize();
		initializer.Initialize();

		using var connection = factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('ItemTrackingUnits','StockMovementTracking');";
		Assert.Equal(2, Convert.ToInt32(command.ExecuteScalar()));
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('UX_ItemTrackingUnits_Item_Mode_Code','IX_StockMovementTracking_TrackingUnit');";
		Assert.Equal(2, Convert.ToInt32(command.ExecuteScalar()));
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
