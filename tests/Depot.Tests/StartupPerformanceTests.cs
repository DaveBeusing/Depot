// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Diagnostics;

using Xunit;

namespace Depot.Tests;

public sealed class StartupPerformanceTests
{
	[Fact]
	public void TraceRecordsEveryCheckpointOnceInMonotonicOrder()
	{
		var trace = new StartupPerformanceTrace();
		var checkpoints = Enum.GetValues<StartupPerformanceCheckpoint>();

		foreach (var checkpoint in checkpoints) trace.Mark(checkpoint);
		trace.Mark(StartupPerformanceCheckpoint.ApplicationStartup);

		var samples = trace.Snapshot();
		Assert.Equal(checkpoints.Length, samples.Count);
		Assert.Equal(checkpoints, samples.Select(sample => sample.Checkpoint));
		for (var index = 1; index < samples.Count; index++)
			Assert.True(samples[index].Elapsed >= samples[index - 1].Elapsed);

		var summary = trace.FormatSummary();
		Assert.Contains("application-startup=", summary, StringComparison.Ordinal);
		Assert.Contains("first-interactive-shell=", summary, StringComparison.Ordinal);
		Assert.DoesNotContain("Password=", summary, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("Data Source=", summary, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void StartupSourcesContainAllRequiredPerformanceCheckpoints()
	{
		var root = FindRepositoryRoot();
		var source = string.Join(
			Environment.NewLine,
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Program.cs")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "App.xaml.cs")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Composition", "DatabaseComposition.cs")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml.cs")));

		foreach (var checkpoint in Enum.GetValues<StartupPerformanceCheckpoint>())
			Assert.Contains($"StartupPerformanceCheckpoint.{checkpoint}", source, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
