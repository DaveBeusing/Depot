// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class AsyncDataAccessTests : IDisposable
{
	private readonly string _databasePath =
		Path.Combine(Path.GetTempPath(), $"depot-async-{Guid.NewGuid():N}.db");

	private readonly DatabaseAccess _database;

	public AsyncDataAccessTests()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		_database = new DatabaseAccess(factory);
	}

	[Fact]
	public async Task QueryPageAsyncReturnsOnlyRequestedServerPage()
	{
		for (var index = 1; index <= 205; index++)
		{
			await _database.ExecuteAsync(
				"INSERT INTO Items (PartNumber, Description) VALUES ($PartNumber, $Description);",
				CancellationToken.None,
				new DatabaseParameter("$PartNumber", $"PAGE-{index:000}"),
				new DatabaseParameter("$Description", $"Paged item {index}"));
		}

		var page = await _database.QueryPageAsync(
			"SELECT Id, PartNumber FROM Items WHERE PartNumber LIKE 'PAGE-%' ORDER BY PartNumber",
			"SELECT COUNT(*) FROM Items WHERE PartNumber LIKE 'PAGE-%'",
			reader => reader.GetString(1),
			pageNumber: 3,
			pageSize: 100,
			CancellationToken.None);

		Assert.Equal(205, page.TotalCount);
		Assert.Equal(5, page.Items.Count);
		Assert.Equal("PAGE-201", page.Items[0]);
		Assert.False(page.HasNextPage);
	}

	[Fact]
	public async Task AsyncQueriesHonorCancellationBeforeOpeningConnection()
	{
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => _database.QueryAsync(
				"SELECT Id FROM Items;",
				reader => reader.GetInt64(0),
				cancellation.Token));
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath))
		{
			File.Delete(_databasePath);
		}
	}
}
