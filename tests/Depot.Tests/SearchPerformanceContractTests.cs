// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;

using Microsoft.Data.Sqlite;

using Xunit;
using Xunit.Abstractions;

namespace Depot.Tests;

public sealed class SearchPerformanceContractTests
{
	private readonly ITestOutputHelper _output;

	public SearchPerformanceContractTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	public void AppDataGridKeepsVirtualizationAndRecyclingContract()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "DataGrid.xaml"));

		Assert.Contains("ScrollViewer.CanContentScroll\" Value=\"True\"", xaml, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.IsVirtualizing\" Value=\"True\"", xaml, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\"", xaml, StringComparison.Ordinal);
		Assert.Contains("EnableRowVirtualization\" Value=\"True\"", xaml, StringComparison.Ordinal);
		Assert.Contains("EnableColumnVirtualization\" Value=\"True\"", xaml, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(10_000)]
	[InlineData(100_000)]
	public void SearchShapeBenchmarkCapturesExactPrefixAndContainsCosts(int rowCount)
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		using (var create = connection.CreateCommand())
		{
			create.CommandText = "CREATE TABLE SearchProbe (Id INTEGER PRIMARY KEY, Identifier TEXT NOT NULL, DisplayText TEXT NOT NULL);";
			create.ExecuteNonQuery();
		}

		using (var transaction = connection.BeginTransaction())
		using (var insert = connection.CreateCommand())
		{
			insert.Transaction = transaction;
			insert.CommandText = "INSERT INTO SearchProbe (Identifier, DisplayText) VALUES ($Identifier, $DisplayText);";
			var identifier = insert.Parameters.Add("$Identifier", SqliteType.Text);
			var display = insert.Parameters.Add("$DisplayText", SqliteType.Text);
			for (var index = 1; index <= rowCount; index++)
			{
				identifier.Value = $"SO-{index:000000}";
				display.Value = $"Order {index:000000} sample description";
				insert.ExecuteNonQuery();
			}
			transaction.Commit();
		}

		var exact = Measure(connection, "SELECT COUNT(*) FROM SearchProbe WHERE Identifier = $Search;", "SO-050000");
		var prefix = Measure(connection, "SELECT COUNT(*) FROM SearchProbe WHERE Identifier LIKE $Search;", "SO-05%");
		var contains = Measure(connection, "SELECT COUNT(*) FROM SearchProbe WHERE Identifier LIKE $Search OR DisplayText LIKE $Search;", "%500%");

		Assert.InRange(exact.Count, 0, 1);
		Assert.True(prefix.Count >= exact.Count);
		Assert.True(contains.Count >= 0);
		_output.WriteLine($"rows={rowCount:N0} exactMs={exact.Elapsed.TotalMilliseconds:F3} prefixMs={prefix.Elapsed.TotalMilliseconds:F3} containsMs={contains.Elapsed.TotalMilliseconds:F3} exactCount={exact.Count} prefixCount={prefix.Count} containsCount={contains.Count}");
	}

	private static (long Count, TimeSpan Elapsed) Measure(SqliteConnection connection, string sql, string search)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.AddWithValue("$Search", search);
		var stopwatch = Stopwatch.StartNew();
		var count = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
		stopwatch.Stop();
		return (count, stopwatch.Elapsed);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
