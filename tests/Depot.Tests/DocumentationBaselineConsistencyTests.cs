// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Depot.Data;
using Xunit;

namespace Depot.Tests;

public sealed class DocumentationBaselineConsistencyTests
{
	[Fact]
	public void CanonicalDocumentationBaselinesMatchAuthoritativeSources()
	{
		var root = FindRepositoryRoot();
		var application = ReadApplicationVersionIdentity(root);
		var managerVersion = ReadDepotManagerVersion(root);
		var helpVersion = ReadHelpManifestVersion(root);

		var commonBoldMarkers = new[]
		{
			$"- Application: **{application.DevelopmentLine}**",
			$"- Core database schema: **{DatabaseVersion.CurrentVersion}**",
			$"- Sales feature schema: **{SalesSchemaMigration.CurrentVersion}**",
			$"- Finance feature schema: **{FinanceInventoryAccountingSchemaMigration.CurrentVersion}**",
			$"- User Sessions feature schema: **{UserSessionSchemaMigration.CurrentVersion}**",
			$"- Security Events feature schema: **{SecurityEventSchemaMigration.CurrentVersion}**",
			$"- User Preferences feature schema: **{UserPreferenceSchemaMigration.CurrentVersion}**",
			$"- Document Templates feature schema: **{DocumentTemplateSchemaMigration.CurrentVersion}**",
			$"- Help manifest: **{helpVersion}**"
		};

		AssertMarkers(root, "README.md", commonBoldMarkers);
		AssertMarkers(root, "docs/Versioning.md", commonBoldMarkers);
		AssertMarkers(root, "docs/Architecture.md", commonBoldMarkers);
		AssertMarkers(root, "docs/ComplianceOverview.md", commonBoldMarkers);
		AssertMarkers(root, "docs/CurrentStatus.md", commonBoldMarkers.Append($"- DepotManager: **{managerVersion}**"));

		AssertMarkers(root, "docs/DocumentationStatus.md",
		[
			$"- Application: `{application.DevelopmentLine}`",
			$"- Help manifest: `{helpVersion}`",
			$"- Core database schema: `{DatabaseVersion.CurrentVersion}`",
			$"- Sales feature schema: `{SalesSchemaMigration.CurrentVersion}`",
			$"- Finance feature schema: `{FinanceInventoryAccountingSchemaMigration.CurrentVersion}`",
			$"- User Sessions feature schema: `{UserSessionSchemaMigration.CurrentVersion}`",
			$"- Security Events feature schema: `{SecurityEventSchemaMigration.CurrentVersion}`",
			$"- User Preferences feature schema: `{UserPreferenceSchemaMigration.CurrentVersion}`",
			$"- Document Templates feature schema: `{DocumentTemplateSchemaMigration.CurrentVersion}`"
		]);

		AssertMarkers(root, "docs/UserFacingChanges.md",
		[
			$"- Application: **{application.DevelopmentLine}**",
			$"- Core database schema: **{DatabaseVersion.CurrentVersion}**",
			$"- Sales schema: **{SalesSchemaMigration.CurrentVersion}**",
			$"- Finance schema: **{FinanceInventoryAccountingSchemaMigration.CurrentVersion}**",
			$"- User Sessions schema: **{UserSessionSchemaMigration.CurrentVersion}**",
			$"- Security Events schema: **{SecurityEventSchemaMigration.CurrentVersion}**",
			$"- User Preferences schema: **{UserPreferenceSchemaMigration.CurrentVersion}**",
			$"- Document Templates schema: **{DocumentTemplateSchemaMigration.CurrentVersion}**",
			$"- Help manifest: **{helpVersion}**"
		]);

		AssertMarkers(root, "docs/Release1.0.md",
		[
			$"`{application.DevelopmentLine}` line",
			$"Core database schema **{DatabaseVersion.CurrentVersion}**",
			$"Sales feature schema **{SalesSchemaMigration.CurrentVersion}**",
			$"Finance feature schema **{FinanceInventoryAccountingSchemaMigration.CurrentVersion}**",
			$"User Sessions schema **{UserSessionSchemaMigration.CurrentVersion}**",
			$"Security Events schema **{SecurityEventSchemaMigration.CurrentVersion}**",
			$"User Preferences schema **{UserPreferenceSchemaMigration.CurrentVersion}**",
			$"Document Templates schema **{DocumentTemplateSchemaMigration.CurrentVersion}**",
			$"Help manifest **{helpVersion}**"
		]);

		AssertMarkers(root, "docs/FinanceArchitecture.md",
		[
			$"- Core database schema: **{DatabaseVersion.CurrentVersion}**",
			$"- Sales feature schema: **{SalesSchemaMigration.CurrentVersion}**",
			$"- Finance feature schema: **{FinanceInventoryAccountingSchemaMigration.CurrentVersion}**"
		]);

		AssertMarkers(root, "docs/SalesPricing.md",
		[
			$"current Sales feature schema is {SalesSchemaMigration.CurrentVersion}"
		]);
	}

	[Fact]
	public void CanonicalDocumentationDoesNotContainConflictingHelpManifestVersions()
	{
		var root = FindRepositoryRoot();
		var helpVersion = ReadHelpManifestVersion(root);
		var documents = new[]
		{
			"README.md",
			"docs/CurrentStatus.md",
			"docs/Release1.0.md",
			"docs/Versioning.md",
			"docs/Architecture.md",
			"docs/ComplianceOverview.md",
			"docs/DocumentationStatus.md",
			"docs/UserFacingChanges.md",
			"docs/HelpCenter.md"
		};
		var pattern = new Regex(@"Help manifest(?:\s*:)?\s*(?:\*\*|`)?(?<version>\d+\.\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		foreach (var relativePath in documents)
		{
			var content = File.ReadAllText(Path.Combine(root, relativePath));
			foreach (Match match in pattern.Matches(content))
				Assert.Equal(helpVersion, match.Groups["version"].Value);
		}
	}

	[Fact]
	public void CanonicalBaselineDocumentsDoNotPinDepotPreviewPatchVersions()
	{
		var root = FindRepositoryRoot();
		var application = ReadApplicationVersionIdentity(root);
		var exactPreviewVersion = new Regex(
			$@"\b{Regex.Escape(application.Prefix)}\.\d+-{Regex.Escape(application.Suffix)}\b",
			RegexOptions.CultureInvariant);
		var documents = new[]
		{
			"README.md",
			"docs/CurrentStatus.md",
			"docs/Release1.0.md",
			"docs/Versioning.md",
			"docs/Architecture.md",
			"docs/ComplianceOverview.md",
			"docs/DocumentationStatus.md",
			"docs/UserFacingChanges.md"
		};

		foreach (var relativePath in documents)
		{
			var content = File.ReadAllText(Path.Combine(root, relativePath));
			Assert.False(exactPreviewVersion.IsMatch(content), $"Canonical baseline document '{relativePath}' pins an exact Depot preview patch version. Use the development line and keep the exact patch authoritative in Directory.Build.props.");
		}
	}

	private static void AssertMarkers(string root, string relativePath, IEnumerable<string> markers)
	{
		var content = File.ReadAllText(Path.Combine(root, relativePath));
		foreach (var marker in markers)
			Assert.Contains(marker, content, StringComparison.Ordinal);
	}

	private static ApplicationVersionIdentity ReadApplicationVersionIdentity(string root)
	{
		var document = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
		var properties = document.Root?.Element("PropertyGroup") ?? throw new InvalidDataException("Directory.Build.props has no PropertyGroup.");
		var major = properties.Element("DepotVersionMajor")?.Value ?? throw new InvalidDataException("DepotVersionMajor is missing.");
		var minor = properties.Element("DepotVersionMinor")?.Value ?? throw new InvalidDataException("DepotVersionMinor is missing.");
		var suffix = properties.Element("DepotVersionSuffix")?.Value ?? throw new InvalidDataException("DepotVersionSuffix is missing.");
		var prefix = $"{major}.{minor}";
		return new ApplicationVersionIdentity($"{prefix}.x-{suffix}", prefix, suffix);
	}

	private static string ReadDepotManagerVersion(string root)
	{
		var document = XDocument.Load(Path.Combine(root, "src", "DepotManager", "DepotManager.Version.props"));
		var properties = document.Root?.Element("PropertyGroup") ?? throw new InvalidDataException("DepotManager.Version.props has no PropertyGroup.");
		var major = properties.Element("DepotManagerVersionMajor")?.Value ?? throw new InvalidDataException("DepotManagerVersionMajor is missing.");
		var minor = properties.Element("DepotManagerVersionMinor")?.Value ?? throw new InvalidDataException("DepotManagerVersionMinor is missing.");
		var patch = properties.Element("DepotManagerVersionPatch")?.Value ?? throw new InvalidDataException("DepotManagerVersionPatch is missing.");
		var suffix = properties.Element("DepotManagerVersionSuffix")?.Value ?? throw new InvalidDataException("DepotManagerVersionSuffix is missing.");
		return $"{major}.{minor}.{patch}-{suffix}";
	}

	private static string ReadHelpManifestVersion(string root)
	{
		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Depot", "Help", "manifest.json")));
		return document.RootElement.GetProperty("version").GetString() ?? throw new InvalidDataException("Help manifest version is missing.");
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

	private sealed record ApplicationVersionIdentity(string DevelopmentLine, string Prefix, string Suffix);
}
