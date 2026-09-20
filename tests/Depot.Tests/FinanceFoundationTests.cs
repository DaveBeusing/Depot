// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceFoundationTests
{
	[Fact]
	public void CurrencyCodeNormalizesIsoSyntax()
	{
		var code = new CurrencyCode(" usd ");

		Assert.Equal("USD", code.Value);
	}

	[Theory]
	[InlineData("")]
	[InlineData("EU")]
	[InlineData("EURO")]
	[InlineData("€UR")]
	public void CurrencyCodeRejectsInvalidSyntax(string value)
	{
		Assert.Throws<ArgumentException>(() => new CurrencyCode(value));
	}

	[Fact]
	public void ExchangeRateRequiresPositiveRate()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeRate(
			Guid.NewGuid(),
			new CurrencyCode("USD"),
			new CurrencyCode("CAD"),
			0m,
			DateTimeOffset.UtcNow,
			"TEST"));
	}

	[Fact]
	public void SameCurrencyRateMustEqualOne()
	{
		Assert.Throws<ArgumentException>(() => new ExchangeRate(
			Guid.NewGuid(),
			new CurrencyCode("USD"),
			new CurrencyCode("USD"),
			1.01m,
			DateTimeOffset.UtcNow,
			"TEST"));
	}

	[Fact]
	public void FinancePermissionsAreCataloguedWithManualJournalSegregation()
	{
		Assert.Equal("Finance.View", PermissionCatalog.Code(ApplicationPermission.FinanceView));
		Assert.Equal("FinanceGeneralLedger.Post", PermissionCatalog.Code(ApplicationPermission.FinanceGeneralLedgerPost));
		Assert.Equal("FinanceManualJournals.Post", PermissionCatalog.Code(ApplicationPermission.FinanceManualJournalsPost));
		var financeRole = Assert.Single(SystemRoleCatalog.Definitions, role => role.Code == SystemRoleCatalog.FinanceCode);
		Assert.Contains(ApplicationPermission.FinanceView, financeRole.Permissions);
		Assert.Contains(ApplicationPermission.FinanceAccountingBooksManage, financeRole.Permissions);
		Assert.Contains(ApplicationPermission.FinanceTaxConfigurationManage, financeRole.Permissions);
		Assert.Contains(ApplicationPermission.FinanceGeneralLedgerPost, financeRole.Permissions);
		Assert.Contains(ApplicationPermission.FinanceGeneralLedgerReverse, financeRole.Permissions);
		Assert.Contains(ApplicationPermission.FinancePostingProfilesManage, financeRole.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceManualJournalsPost, financeRole.Permissions);
	}

	[Fact]
	public void FinanceFeatureSchemaIncludesGeneralLedgerVersionTwo()
	{
		Assert.Equal(2, FinanceSchemaMigration.CurrentVersion);
	}

	[Fact]
	public void PeriodControlUsesAuthorizedGeneralLedgerBoundaryAndAccessibleWorkspace()
	{
		var root = FindRepositoryRoot();
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "FinanceGeneralLedgerService.cs"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinancePeriodControlViewModel.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinancePeriodControlView.xaml"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));
		var templates = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "ViewTemplates.xaml"));

		Assert.Contains("ApplicationPermission.FinancePeriodsManage", service, StringComparison.Ordinal);
		Assert.Contains("SetPeriodStatusAsync", service, StringComparison.Ordinal);
		Assert.Contains("_ledger.SetPeriodStatusAsync", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("UPDATE FinanceAccountingPeriods", viewModel, StringComparison.Ordinal);
		Assert.Contains("ClosePeriodCommand", view, StringComparison.Ordinal);
		Assert.Contains("ReopenPeriodCommand", view, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"Accounting periods\"", view, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", view, StringComparison.Ordinal);
		Assert.Contains("ApplicationPermission.FinancePeriodsView, \"Period Control\"", main, StringComparison.Ordinal);
		Assert.Contains("\"finance.foundation\"", main, StringComparison.Ordinal);
		Assert.Contains("FinancePeriodControlViewModel", templates, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}

	[Fact]
	public void JournalEntriesAreClassifiedAsRetainedAccountingRecords()
	{
		var classification = BusinessRecordCatalog.Require(nameof(FinanceJournalEntry));
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, classification.RetentionCategory);
		Assert.Contains("reversal", classification.CorrectionMechanism, StringComparison.OrdinalIgnoreCase);
	}
}
