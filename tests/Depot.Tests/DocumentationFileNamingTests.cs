// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.RegularExpressions;

using Xunit;

namespace Depot.Tests;

public sealed class DocumentationFileNamingTests
{
	private static readonly Regex PascalCaseDocumentName = new("^[A-Z][a-z0-9]*(?:[A-Z][a-z0-9]*)*(?:\\.[0-9]+)*\\.md$", RegexOptions.CultureInvariant);

	private static readonly string[] LegacyDocumentNames =
	[
		"COMPLIANCE_OVERVIEW.md", "CURRENT_STATUS.md", "DATA_ACCESS_AUDIT.md", "DOCUMENTATION_STATUS.md",
		"FINANCE_ARCHITECTURE.md", "FINANCE_BANKING.md", "FINANCE_COMPLIANCE.md", "FINANCE_LOCALIZATION.md",
		"FINANCE_REPORTING.md", "HELP_CENTER.md", "ITEM_COSTING_AND_BULK_PRICING.md", "ITEM_MASTER_DATA.md",
		"ITEM_TRACEABILITY.md", "NOTIFICATION_CENTER.md", "REFERENCE_DATA_DEFAULTS.md", "RELEASE_1_0.md",
		"SALES_PRICING.md", "SECURITY_ROADMAP.md", "USER_FACING_CHANGES.md", "VERSIONING.md",
		"ACCESSIBILITY.md", "ASVS_MAPPING.md", "AUDIT_AND_RECOVERY.md", "AUTHENTICATION_SECURITY.md",
		"BUSINESS_RECORD_INTEGRITY.md", "COMPANY_MASTER_DATA.md", "COMPANY_MASTER_DATA_LEGAL_MAPPING.md",
		"COMPLIANCE_MATRIX.md", "CRA_CLASSIFICATION.md", "CRA_INCIDENT_REPORTING.md", "CRA_RISK_ASSESSMENT.md",
		"CRA_TECHNICAL_DOCUMENTATION.md", "CRYPTOGRAPHY.md", "DATA_INVENTORY.md", "DATA_PROTECTION.md",
		"DEPENDENCY_POLICY.md", "ELECTRONIC_INVOICING.md", "GOVERNANCE.md", "INVOICE_FINALIZATION.md",
		"ISSUER_SNAPSHOTS.md", "LICENSE_REVIEW.md", "PHASE4_STATUS.md", "PHASE5_STATUS.md", "PHASE6_STATUS.md",
		"PHASE7_STATUS.md", "PRIVACY_BY_DESIGN.md", "PROCEDURAL_DOCUMENTATION.md", "RELEASE_INTEGRITY.md",
		"RETENTION_POLICY.md", "SECURE_CONFIGURATION.md", "SECURE_DEFAULTS_REVIEW.md", "SECURITY_LOGGING.md",
		"SECURITY_REVIEW.md", "SECURITY_UPDATE_LIFECYCLE.md", "SOFTWARE_QUALITY.md", "SUPPORT_POLICY.md",
		"TELEMETRY_POLICY.md", "THREAT_MODEL.md", "VULNERABILITY_MANAGEMENT.md"
	];

	private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".md", ".cs", ".csproj", ".props", ".targets", ".slnx", ".ps1", ".sh", ".json", ".xml", ".yml", ".yaml"
	};

	[Fact]
	public void ProjectDocumentationUsesPascalCaseFileNames()
	{
		var repositoryRoot = FindRepositoryRoot();
		var docsRoot = Path.Combine(repositoryRoot, "docs");
		var documents = Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories);

		Assert.Equal(63, documents.Length);
		foreach (var document in documents)
		{
			var fileName = Path.GetFileName(document);
			Assert.Matches(PascalCaseDocumentName, fileName);
		}
	}

	[Fact]
	public void RepositoryDoesNotReferenceLegacyDocumentationNames()
	{
		var repositoryRoot = FindRepositoryRoot();
		var files = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
			.Where(path => TextExtensions.Contains(Path.GetExtension(path)))
			.Where(path => !ContainsDirectory(path, ".git") && !ContainsDirectory(path, "bin") && !ContainsDirectory(path, "obj"))
			.Where(path => !path.EndsWith(nameof(DocumentationFileNamingTests) + ".cs", StringComparison.Ordinal));

		foreach (var file in files)
		{
			var content = File.ReadAllText(file);
			foreach (var legacyName in LegacyDocumentNames)
				Assert.DoesNotContain(legacyName, content, StringComparison.Ordinal);

			Assert.DoesNotContain("docs/compliance/SECURITY.md", content.Replace('\\', '/'), StringComparison.Ordinal);
		}
	}

	private static bool ContainsDirectory(string path, string directoryName)
	{
		var separatorWrappedPath = $"{Path.DirectorySeparatorChar}{path}{Path.DirectorySeparatorChar}";
		return separatorWrappedPath.Contains($"{Path.DirectorySeparatorChar}{directoryName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Depot.slnx")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
