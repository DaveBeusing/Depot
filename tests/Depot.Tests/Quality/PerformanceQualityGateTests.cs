// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Depot.Tests.Quality;

public sealed class PerformanceQualityGateTests
{
	[Fact]
	[Trait("QualityGate", "Performance")]
	public async Task Sqlite_OneHundredThousandRecords_RemainsWithinBaselineLimits()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		await using (var create = connection.CreateCommand())
		{
			create.CommandText = "CREATE TABLE quality_records (id INTEGER PRIMARY KEY, code TEXT NOT NULL, name TEXT NOT NULL, quantity INTEGER NOT NULL); CREATE INDEX ix_quality_records_code ON quality_records(code);";
			await create.ExecuteNonQueryAsync();
		}

		var insertWatch = Stopwatch.StartNew();
		await using (var transaction = await connection.BeginTransactionAsync())
		await using (var insert = connection.CreateCommand())
		{
			insert.Transaction = (SqliteTransaction)transaction;
			insert.CommandText = "INSERT INTO quality_records (id, code, name, quantity) VALUES ($id, $code, $name, $quantity);";
			var id = insert.Parameters.Add("$id", SqliteType.Integer);
			var code = insert.Parameters.Add("$code", SqliteType.Text);
			var name = insert.Parameters.Add("$name", SqliteType.Text);
			var quantity = insert.Parameters.Add("$quantity", SqliteType.Integer);
			for (var i = 1; i <= 100_000; i++)
			{
				id.Value = i;
				code.Value = $"ITEM-{i:D6}";
				name.Value = $"Quality record {i:D6}";
				quantity.Value = i % 1000;
				await insert.ExecuteNonQueryAsync();
			}
			await transaction.CommitAsync();
		}
		insertWatch.Stop();

		var queryWatch = Stopwatch.StartNew();
		await using (var query = connection.CreateCommand())
		{
			query.CommandText = "SELECT id, code, name, quantity FROM quality_records WHERE code >= $start ORDER BY code LIMIT 100 OFFSET 50000;";
			query.Parameters.AddWithValue("$start", "ITEM-000001");
			await using var reader = await query.ExecuteReaderAsync();
			var count = 0;
			while (await reader.ReadAsync()) count++;
			Assert.Equal(100, count);
		}
		queryWatch.Stop();

		Assert.True(insertWatch.Elapsed < TimeSpan.FromSeconds(30), $"100k insert baseline exceeded: {insertWatch.Elapsed}.");
		Assert.True(queryWatch.Elapsed < TimeSpan.FromSeconds(2), $"Paged 100k query baseline exceeded: {queryWatch.Elapsed}.");
	}
}
