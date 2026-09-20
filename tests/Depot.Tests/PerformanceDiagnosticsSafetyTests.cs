// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class PerformanceDiagnosticsSafetyTests
{
	[Fact]
	public void PerformanceDiagnosticsAreBestEffortAndDoNotOwnBusinessControlFlow()
	{
		var root = FindRepositoryRoot();
		var myWork = File.ReadAllText(Path.Combine(root, "src", "Depot", "Diagnostics", "MyWorkPerformanceDiagnostics.cs"));
		var home = File.ReadAllText(Path.Combine(root, "src", "Depot", "Diagnostics", "HomeProgressiveLoadTrace.cs"));

		Assert.Contains("catch (IOException)", myWork, StringComparison.Ordinal);
		Assert.Contains("catch (UnauthorizedAccessException)", myWork, StringComparison.Ordinal);
		Assert.Contains("catch (IOException)", home, StringComparison.Ordinal);
		Assert.Contains("catch (UnauthorizedAccessException)", home, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
