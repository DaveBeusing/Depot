// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class DeploymentSizingToolingTests
{
	[Fact]
	public void DeploymentSizingScriptCapturesRequiredOperationalEvidence()
	{
		var root = FindRepositoryRoot();
		var path = Path.Combine(root, "scripts", "performance", "Measure-DeploymentSizing.ps1");
		Assert.True(File.Exists(path), "Deployment sizing script must remain part of the repository.");

		var source = File.ReadAllText(path);
		foreach (var scenario in new[]
		{
			"Startup",
			"Home",
			"Navigation",
			"Search",
			"My Work",
			"Large List",
			"Finance Report",
			"Export",
			"Import",
			"Document/PDF",
			"Backup Window"
		})
			Assert.Contains($"'{scenario}'", source, StringComparison.Ordinal);

		Assert.Contains("P50Ms", source, StringComparison.Ordinal);
		Assert.Contains("P95Ms", source, StringComparison.Ordinal);
		Assert.Contains("AverageCpuPercent", source, StringComparison.Ordinal);
		Assert.Contains("PeakWorkingSetMb", source, StringComparison.Ordinal);
		Assert.Contains("NetworkLatencyMs", source, StringComparison.Ordinal);
		Assert.Contains("ConcurrentUsers", source, StringComparison.Ordinal);
		Assert.Contains("DataVolumeRecords", source, StringComparison.Ordinal);
		Assert.Contains("CommitSha", source, StringComparison.Ordinal);
		Assert.Contains("DepotVersion", source, StringComparison.Ordinal);
		Assert.Contains("RequireCompleteProfile", source, StringComparison.Ordinal);
		Assert.Contains("MissingRequiredScenarios", source, StringComparison.Ordinal);
	}

	[Fact]
	public void DeploymentSizingScriptConsumesStructuralRuntimeEvidenceOnly()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "scripts", "performance", "Measure-DeploymentSizing.ps1"));

		Assert.Contains("home firstContent=", source, StringComparison.Ordinal);
		Assert.Contains("my-work provider=", source, StringComparison.Ordinal);
		Assert.DoesNotContain("CustomerName", source, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("SupplierName", source, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("DocumentNumber", source, StringComparison.OrdinalIgnoreCase);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
