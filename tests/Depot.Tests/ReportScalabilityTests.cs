// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ReportScalabilityTests
{
	[Fact]
	public async Task InventoryReportUsesStablePagesAndDatabaseAggregates()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await AddStockAsync(context, context.InventoryId, 10, 2m);
		await AddStockAsync(context, context.SecondInventoryId, 5, 4m);
		var reports = CreateService(context);

		var first = await reports.GetInventoryValueReportPageAsync("TEST-ITEM-", 1, 1, CancellationToken.None);
		var second = await reports.GetInventoryValueReportPageAsync("TEST-ITEM-", 2, 1, CancellationToken.None);
		var grouped = await reports.GetGroupedInventoryReportAsync("TEST-ITEM-", GroupedInventoryReportType.Warehouse, CancellationToken.None);

		Assert.Equal(2, first.TotalCount);
		Assert.Equal(2, first.TotalInventoryRows);
		Assert.Equal(2, first.TotalItems);
		Assert.Equal(15, first.TotalStockQuantity);
		Assert.Equal(40m, first.TotalInventoryValue);
		Assert.Single(first.Items);
		Assert.Single(second.Items);
		Assert.NotEqual(first.Items[0].InventoryId, second.Items[0].InventoryId);
		Assert.Single(grouped.Items);
		Assert.Equal(2, grouped.Items[0].InventoryRows);
		Assert.Equal(15, grouped.Items[0].TotalStockQuantity);
	}

	[Fact]
	public async Task InventoryExportReadsBoundedSlicesAndReportsProgress()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await AddStockAsync(context, context.InventoryId, 3, 7m);
		var reports = CreateService(context);
		var progress = new CapturingProgress();
		var path = Path.Combine(Path.GetTempPath(), $"depot-report-{Guid.NewGuid():N}.xlsx");
		try
		{
			await reports.ExportInventoryValueReportAsync("TEST-ITEM-", path, progress, CancellationToken.None);

			Assert.True(File.Exists(path));
			Assert.NotEmpty(progress.Values);
			Assert.Equal(100, progress.Values[^1].Percentage);
			Assert.Equal(progress.Values[^1].TotalRows, progress.Values[^1].ProcessedRows);
		}
		finally
		{
			if (File.Exists(path)) File.Delete(path);
		}
	}

	private static ReportService CreateService(ProcurementTestContext context) =>
		new(new StockService(new InventoryRepository(context.Data), new StockMovementRepository(context.Data)), context.Authorization);

	private static Task<long> AddStockAsync(ProcurementTestContext context, long inventoryId, int quantity, decimal unitPrice) =>
		context.Data.InsertAsync(
			"INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference) VALUES ($InventoryId, $MovementType, $TimestampUtc, $Quantity, $UnitPrice, $Reference);",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", inventoryId),
			new DatabaseParameter("$MovementType", (int)StockMovementType.Purchase),
			new DatabaseParameter("$TimestampUtc", DateTime.UtcNow.ToString("O")),
			new DatabaseParameter("$Quantity", quantity),
			new DatabaseParameter("$UnitPrice", unitPrice),
			new DatabaseParameter("$Reference", "REPORT-TEST"));

	private sealed class CapturingProgress : IProgress<ReportExportProgress>
	{
		public List<ReportExportProgress> Values { get; } = [];

		public void Report(ReportExportProgress value) => Values.Add(value);
	}
}
