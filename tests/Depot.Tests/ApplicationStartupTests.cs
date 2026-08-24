// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class ApplicationStartupTests
{
	[Fact]
	public void AppStartup_DoesNotSynchronouslyBlockAdministratorBootstrap()
	{
		var repositoryRoot = FindRepositoryRoot();
		var appSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Depot", "App.xaml.cs"));

		Assert.Contains("async void OnStartup", appSource, StringComparison.Ordinal);
		Assert.Contains("await EnsureAdministratorAsync", appSource, StringComparison.Ordinal);
		Assert.DoesNotContain("GetAwaiter().GetResult()", appSource, StringComparison.Ordinal);
		Assert.DoesNotContain(".Result", appSource, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
			directory = directory.Parent;
		}
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
