// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Diagnostics;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class MyWorkPerformanceDiagnosticsTests
{
	[Fact]
	public async Task DatabaseCommandScopeCountsExecutedReadCommands()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-query-evidence-{Guid.NewGuid():N}.db");
		try
		{
			var database = new DatabaseAccess(new SqliteConnectionFactory(path));
			using var scope = DatabaseQueryDiagnostics.BeginScope();

			await database.ExecuteAsync("CREATE TABLE Evidence (Id INTEGER PRIMARY KEY);", CancellationToken.None);
			await database.ExecuteScalarAsync("SELECT COUNT(*) FROM Evidence;", CancellationToken.None);
			await database.QueryAsync("SELECT Id FROM Evidence;", reader => reader.GetInt64(0), CancellationToken.None);

			Assert.Equal(3, scope.CommandCount);
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[Fact]
	public async Task MyWorkMeasurementContainsOnlyStructuralEvidence()
	{
		var measured = await MyWorkPerformanceDiagnostics.MeasureAsync(
			"Provider Test",
			() => Task.FromResult<IReadOnlyList<MyWorkItem>>(
			[
				new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.PurchaseOrder, 42, "SECRET-42", "Title", "Context", "Open", null, null, null, MyWorkPriority.Normal, "route", "Open", 9)
			]),
			items => items.Count);

		Assert.Equal("Provider Test", measured.Measurement.Provider);
		Assert.True(measured.Measurement.Eligible);
		Assert.Equal(1, measured.Measurement.ReturnedRows);
		Assert.False(measured.Measurement.Failed);
		Assert.Equal(0, measured.Measurement.QueryCount);
		Assert.DoesNotContain("SECRET-42", measured.Measurement.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("Context", measured.Measurement.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task CancellationIsNotConvertedIntoProviderFailure()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			MyWorkPerformanceDiagnostics.MeasureAsync<IReadOnlyList<MyWorkItem>>(
				"Cancelled",
				() => Task.FromCanceled<IReadOnlyList<MyWorkItem>>(cancellation.Token),
				items => items.Count));
	}

	[Fact]
	public void IneligibleProviderEvidenceHasNoDataOrQueries()
	{
		var measurement = MyWorkPerformanceDiagnostics.MeasureIneligible("Restricted");

		Assert.False(measurement.Eligible);
		Assert.Equal(0, measurement.ReturnedRows);
		Assert.Equal(0, measurement.QueryCount);
		Assert.False(measurement.Failed);
	}
}
