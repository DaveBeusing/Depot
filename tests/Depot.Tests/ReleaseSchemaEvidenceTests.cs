// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class ReleaseSchemaEvidenceTests
{
	[Fact]
	public void ReleaseEvidenceIncludesEveryCurrentSchemaDimension()
	{
		var root = FindRepositoryRoot();
		var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-integrity.yml"));
		var expectedVersionSources = new[]
		{
			"src/Depot/Data/DatabaseVersion.cs",
			"src/Depot/Data/SalesSchemaMigration.cs",
			"src/Depot/Data/FinanceInventoryAccountingSchemaMigration.cs",
			"src/Depot/Data/UserSessionSchemaMigration.cs",
			"src/Depot/Data/SecurityEventSchemaMigration.cs",
			"src/Depot/Data/UserPreferenceSchemaMigration.cs",
			"src/Depot/Data/DocumentTemplateSchemaMigration.cs",
			"src/Depot/Data/EnterpriseIdentitySchemaMigration.cs"
		};

		foreach (var path in expectedVersionSources)
			Assert.Contains($"Read-CurrentVersion '{path}'", workflow, StringComparison.Ordinal);
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
