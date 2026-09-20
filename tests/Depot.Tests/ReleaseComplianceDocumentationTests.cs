// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class ReleaseComplianceDocumentationTests
{
	[Fact]
	public void CanonicalReleaseDocumentsExposeSupportedAndUnsupportedBoundaries()
	{
		var root = FindRepositoryRoot();
		var boundary = Read(root, "docs", "ReleaseComplianceBoundary.md");
		var limitations = Read(root, "docs", "KnownLimitations.md");
		var support = Read(root, "docs", "compliance", "SupportPolicy.md");
		var readme = Read(root, "README.md");
		var release = Read(root, "docs", "Release1.0.md");

		Assert.Contains("External qualified reviews remain external evidence", boundary, StringComparison.Ordinal);
		Assert.Contains("No qualified legal, accounting or tax opinion", boundary, StringComparison.Ordinal);
		Assert.Contains("Preview builds are not production-supported", limitations, StringComparison.Ordinal);
		Assert.Contains("No Stable 1.0 production support window", support, StringComparison.Ordinal);
		Assert.Contains("release-specific support statement", support, StringComparison.Ordinal);
		Assert.Contains("[Release Compliance Boundary](docs/ReleaseComplianceBoundary.md)", readme, StringComparison.Ordinal);
		Assert.Contains("[Known Limitations](docs/KnownLimitations.md)", readme, StringComparison.Ordinal);
		Assert.Contains("canonical release compliance claim boundary", release, StringComparison.Ordinal);
		Assert.Contains("canonical Known Limitations document", release, StringComparison.Ordinal);
		Assert.DoesNotContain("certifies database providers", readme, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("Current technical certification baselines", readme, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ElectronicInvoiceReleaseMatrixMatchesProductionContract()
	{
		var root = FindRepositoryRoot();
		var model = Read(root, "src", "Depot", "Models", "ElectronicInvoice.cs");
		var boundary = Read(root, "docs", "ReleaseComplianceBoundary.md");
		var limitations = Read(root, "docs", "KnownLimitations.md");

		Assert.Contains("XRechnungVersion = \"3.0\"", model, StringComparison.Ordinal);
		Assert.Contains("StandardRated = \"S\"", model, StringComparison.Ordinal);
		Assert.Contains("ZeroRated = \"Z\"", model, StringComparison.Ordinal);
		Assert.Contains("Exempt = \"E\"", model, StringComparison.Ordinal);
		Assert.Contains("ReverseCharge = \"AE\"", model, StringComparison.Ordinal);
		Assert.Contains("CreditNote = 381", model, StringComparison.Ordinal);

		foreach (var marker in new[]
		{
			"XRechnung 3.0",
			"Standard rated (`S`)",
			"Zero rated (`Z`)",
			"Exempt (`E`)",
			"Reverse charge (`AE`)",
			"Credit Note (`381`)",
			"ZUGFeRD 2.5.2",
			"Factur-X 1.09.2",
			"`XRECHNUNG`"
		})
		{
			Assert.Contains(marker, boundary, StringComparison.OrdinalIgnoreCase);
			Assert.Contains(marker, limitations, StringComparison.OrdinalIgnoreCase);
		}

		Assert.Contains("does not currently perform external electronic-invoice transport", boundary, StringComparison.Ordinal);
		Assert.Contains("other VAT category/document combinations are not implicitly supported", limitations, StringComparison.Ordinal);
	}

	[Fact]
	public void LocalizationAndDatabaseClaimsRemainBounded()
	{
		var root = FindRepositoryRoot();
		var localizationModel = Read(root, "src", "Depot", "Models", "FinanceLocalization.cs");
		var localizationDoc = Read(root, "docs", "FinanceLocalization.md");
		var boundary = Read(root, "docs", "ReleaseComplianceBoundary.md");
		var limitations = Read(root, "docs", "KnownLimitations.md");
		var databaseMatrix = Read(root, "docs", "DatabaseProviderSupportMatrix.md");
		Assert.Contains("## Depot 1.0 accepted baselines", databaseMatrix, StringComparison.Ordinal);
		Assert.DoesNotContain("## Depot 1.0 certification baselines", databaseMatrix, StringComparison.Ordinal);
		Assert.DoesNotContain("implicitly certified", databaseMatrix, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("certification environment", databaseMatrix, StringComparison.OrdinalIgnoreCase);

		foreach (var supportLevel in new[]
		{
			"SoftwareCapability",
			"ConfigurationRequired",
			"ExternalProcedureRequired",
			"ReferenceOnly"
		})
		{
			Assert.Contains(supportLevel, localizationModel, StringComparison.Ordinal);
			Assert.Contains(supportLevel, localizationDoc, StringComparison.Ordinal);
			Assert.Contains(supportLevel, boundary, StringComparison.Ordinal);
		}

		Assert.Contains("not be described as a certified or legally sufficient German/EU country pack", localizationDoc, StringComparison.Ordinal);
		Assert.Contains("not a certified German tax/accounting pack", boundary, StringComparison.Ordinal);

		foreach (var baseline in new[]
		{
			"SQL Server 2022",
			"MariaDB 11.8.9 LTS",
			"MySQL 8.4.11 LTS"
		})
		{
			Assert.Contains(baseline, databaseMatrix, StringComparison.Ordinal);
			Assert.Contains(baseline, boundary, StringComparison.Ordinal);
			Assert.Contains(baseline, limitations, StringComparison.Ordinal);
		}

		Assert.Contains("Versions outside these baselines are not implicitly production-supported", boundary, StringComparison.Ordinal);
		Assert.Contains("SQLite uses dynamic `NUMERIC` affinity", limitations, StringComparison.Ordinal);
	}

	[Fact]
	public void CraPrivacyBankingAndOperationsClaimsStayExternalWhereRequired()
	{
		var root = FindRepositoryRoot();
		var boundary = Read(root, "docs", "ReleaseComplianceBoundary.md");
		var limitations = Read(root, "docs", "KnownLimitations.md");
		var dataProtection = Read(root, "docs", "compliance", "DataProtection.md");
		var banking = Read(root, "docs", "FinanceBanking.md");
		var operations = Read(root, "docs", "ProductionOperationsDisasterRecovery.md");

		Assert.Contains("Article 14 reporting obligations apply from 11 September 2026", boundary, StringComparison.Ordinal);
		Assert.Contains("wider Regulation applies from 11 December 2027", boundary, StringComparison.Ordinal);
		Assert.Contains("final product classification", boundary, StringComparison.Ordinal);
		Assert.Contains("Deployment-specific GDPR/DSGVO obligations remain", dataProtection, StringComparison.Ordinal);

		foreach (var excluded in new[] { "direct bank connectivity", "EBICS", "PSD2/open-banking APIs", "payment initiation" })
		{
			Assert.Contains(excluded, banking, StringComparison.OrdinalIgnoreCase);
			Assert.Contains(excluded, boundary, StringComparison.OrdinalIgnoreCase);
			Assert.Contains(excluded, limitations, StringComparison.OrdinalIgnoreCase);
		}

		Assert.Contains("production deployment must still own scheduling, retention, encryption, off-host copies, monitoring, RPO/RTO", operations, StringComparison.Ordinal);
		Assert.Contains("Repository recovery CI proves a generic technical restore boundary only", limitations, StringComparison.Ordinal);
	}

	private static string Read(string root, params string[] path) =>
		File.ReadAllText(Path.Combine([root, .. path]));

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
