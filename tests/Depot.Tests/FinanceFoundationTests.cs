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
	public void JournalEntriesAreClassifiedAsRetainedAccountingRecords()
	{
		var classification = BusinessRecordCatalog.Require(nameof(FinanceJournalEntry));
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, classification.RetentionCategory);
		Assert.Contains("reversal", classification.CorrectionMechanism, StringComparison.OrdinalIgnoreCase);
	}
}
