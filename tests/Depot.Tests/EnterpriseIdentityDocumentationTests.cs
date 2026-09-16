// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Xunit;

namespace Depot.Tests;

public sealed class EnterpriseIdentityDocumentationTests
{
	[Fact]
	public void CanonicalIdentityDocumentationMatchesEnterpriseIdentitySchema()
	{
		var root = FindRepositoryRoot();
		var boldMarker = $"- Enterprise Identity feature schema: **{EnterpriseIdentitySchemaMigration.CurrentVersion}**";
		Assert.Contains(boldMarker, File.ReadAllText(Path.Combine(root, "docs", "CurrentStatus.md")), StringComparison.Ordinal);
		Assert.Contains(boldMarker, File.ReadAllText(Path.Combine(root, "docs", "Versioning.md")), StringComparison.Ordinal);
		Assert.Contains(
			$"- Enterprise Identity feature schema: `{EnterpriseIdentitySchemaMigration.CurrentVersion}`",
			File.ReadAllText(Path.Combine(root, "docs", "DocumentationStatus.md")),
			StringComparison.Ordinal);
		Assert.Contains(
			$"Enterprise Identity schema **{EnterpriseIdentitySchemaMigration.CurrentVersion}**",
			File.ReadAllText(Path.Combine(root, "docs", "EnterpriseIdentity.md")),
			StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx")))
				return directory.FullName;
		}
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
