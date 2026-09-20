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
			$"- Enterprise Identity feature schema: **{EnterpriseIdentitySchemaMigration.CurrentVersion}**",
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
			$"- Document Templates feature schema: `{DocumentTemplateSchemaMigration.CurrentVersion}`",
			$"- Enterprise Identity feature schema: `{EnterpriseIdentitySchemaMigration.CurrentVersion}`"
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
			$"- Enterprise Identity feature schema: **{EnterpriseIdentitySchemaMigration.CurrentVersion}**",
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
			$"Enterprise Identity feature schema **{EnterpriseIdentitySchemaMigration.CurrentVersion}**",
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
	public void CanonicalStatusDoesNotRegressToHistoricalGovernanceProductivityOrFinanceHelpClaims()
	{
		var root = FindRepositoryRoot();
		var current = File.ReadAllText(Path.Combine(root, "docs", "CurrentStatus.md"));
		var roadmap = File.ReadAllText(Path.Combine(root, "docs", "Roadmap.md"));
		var release = File.ReadAllText(Path.Combine(root, "docs", "Release1.0.md"));
		var readiness = File.ReadAllText(Path.Combine(root, "docs", "ReleaseCandidateReadiness.md"));
		var uiStatus = File.ReadAllText(Path.Combine(root, "docs", "UiUxRolloutStatus.md"));
		var uiPackages = File.ReadAllText(Path.Combine(root, "docs", "UiUxRolloutWorkPackages.md"));
		var financeFoundation = File.ReadAllText(Path.Combine(root, "src", "Depot", "Help", "finance", "foundation.md"));
		var receivables = File.ReadAllText(Path.Combine(root, "src", "Depot", "Help", "finance", "receivables.md"));
		var generalLedger = File.ReadAllText(Path.Combine(root, "src", "Depot", "Help", "finance", "general-ledger.md"));
		var trackA = File.ReadAllText(Path.Combine(root, "docs", "TrackAAcceptanceClosure.md"));
		var securityRoadmap = File.ReadAllText(Path.Combine(root, "docs", "SecurityRoadmap.md"));
		var repositoryGovernance = File.ReadAllText(Path.Combine(root, "docs", "RepositoryGovernance.md"));

		Assert.Contains("H1 Repository Governance: `BLOCKED`", current, StringComparison.Ordinal);
		Assert.Contains("H1 Repository Governance: `BLOCKED`", roadmap, StringComparison.Ordinal);
		Assert.Contains("H1 is currently `BLOCKED`", release, StringComparison.Ordinal);
		Assert.Contains("H1 repository governance — reopened / BLOCKED", readiness, StringComparison.Ordinal);
		Assert.DoesNotContain("H1 and H2 are closed", current, StringComparison.Ordinal);
		Assert.DoesNotContain("H1 closed on 2026-09-17", roadmap, StringComparison.Ordinal);
		Assert.DoesNotContain("H1 repository governance — PASS with live active ruleset evidence", readiness, StringComparison.Ordinal);
		Assert.Contains("| H1 – Repository Governance & Required Gates | Implemented | `BLOCKED`", trackA, StringComparison.Ordinal);
		Assert.DoesNotContain("| H1 – Repository Governance & Required Gates | Implemented | `PASS`", trackA, StringComparison.Ordinal);
		Assert.Contains("- [ ] H1 live repository-governance required-check binding restored and revalidated", securityRoadmap, StringComparison.Ordinal);
		Assert.DoesNotContain("H1 live repository-governance activation/evidence is closed", securityRoadmap, StringComparison.Ordinal);
		Assert.DoesNotContain("## Required status checks\n## Required status checks", repositoryGovernance, StringComparison.Ordinal);

		Assert.Contains("## Subsequent productivity integration — Complete", uiStatus, StringComparison.Ordinal);
		Assert.Contains($"User Preferences schema **{UserPreferenceSchemaMigration.CurrentVersion}**", uiStatus, StringComparison.Ordinal);
		Assert.DoesNotContain("## Deliberately deferred", uiStatus, StringComparison.Ordinal);
		Assert.Contains("## Historical follow-up boundary", uiPackages, StringComparison.Ordinal);
		Assert.DoesNotContain("remain outside this rollout because they require persisted preference/state architecture", uiPackages, StringComparison.Ordinal);

		Assert.DoesNotContain("Not yet implemented as complete Finance packages", financeFoundation, StringComparison.Ordinal);
		Assert.DoesNotContain("The next Finance package is **F3", financeFoundation, StringComparison.Ordinal);
		Assert.DoesNotContain("Next is **F3", receivables, StringComparison.Ordinal);
		Assert.DoesNotContain("Next: **F3", generalLedger, StringComparison.Ordinal);
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
			"docs/UserFacingChanges.md",
			"docs/Roadmap.md",
			"docs/ReleaseCandidateReadiness.md",
			"docs/UiUxRolloutStatus.md"
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
