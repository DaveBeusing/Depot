// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceReportingMappingDesignerProjectionTests
{
	[Fact]
	public void ProjectionReportsMappedUnmappedInvalidAndInactiveCoverage()
	{
		var assetId = Guid.NewGuid();
		var expenseId = Guid.NewGuid();
		var inactiveId = Guid.NewGuid();
		var unmappedId = Guid.NewGuid();
		var accounts = new[]
		{
			new FinanceReportingAccountRecord(assetId, "1000", "Cash", FinanceAccountType.Asset, true),
			new FinanceReportingAccountRecord(expenseId, "5000", "Expense", FinanceAccountType.Expense, true),
			new FinanceReportingAccountRecord(inactiveId, "7000", "Inactive", FinanceAccountType.Expense, false),
			new FinanceReportingAccountRecord(unmappedId, "8000", "Unmapped", FinanceAccountType.Expense, true)
		};
		var mappings = new[]
		{
			new FinanceReportingAccountMapping { Id = 1, AccountId = assetId, StatementSection = FinanceStatementSection.CurrentAssets, IsCashAccount = true, SortOrder = 10 },
			new FinanceReportingAccountMapping { Id = 2, AccountId = expenseId, StatementSection = FinanceStatementSection.CurrentAssets, SortOrder = 20 },
			new FinanceReportingAccountMapping { Id = 3, AccountId = inactiveId, StatementSection = FinanceStatementSection.OperatingExpenses, IsActive = false, SortOrder = 30 }
		};

		var projection = FinanceReportingMappingProjector.Project(accounts, mappings);

		Assert.Equal(4, projection.Coverage.Total);
		Assert.Equal(1, projection.Coverage.Mapped);
		Assert.Equal(1, projection.Coverage.Unmapped);
		Assert.Equal(1, projection.Coverage.Invalid);
		Assert.Equal(1, projection.Coverage.Inactive);
		Assert.Equal(FinanceReportingMappingState.Mapped, projection.Rows.Single(value => value.AccountId == assetId).State);
		Assert.Equal(FinanceReportingMappingState.Invalid, projection.Rows.Single(value => value.AccountId == expenseId).State);
	}

	[Fact]
	public void ValidatorRejectsIncompatibleAccountTypeCashAndCogsMappings()
	{
		var asset = new FinanceReportingAccountRecord(Guid.NewGuid(), "1000", "Asset", FinanceAccountType.Asset, true);
		var invalid = new FinanceReportingAccountMapping
		{
			AccountId = asset.Id,
			StatementSection = FinanceStatementSection.Revenue,
			CashFlowCategory = FinanceCashFlowCategory.Operating,
			IsCashAccount = true,
			IsCostOfGoodsSold = true
		};

		var validation = FinanceReportingMappingValidator.Validate(invalid, asset);

		Assert.False(validation.IsValid);
		Assert.Contains(validation.Errors, value => value.Contains("Financial-statement section", StringComparison.Ordinal));
		Assert.Contains(validation.Errors, value => value.Contains("Cash accounts", StringComparison.Ordinal));
		Assert.Contains(validation.Errors, value => value.Contains("Cost of Goods Sold", StringComparison.Ordinal));
	}

	[Fact]
	public void DragDropTargetTransformationChangesOnlyExplicitMappingFields()
	{
		var accountId = Guid.NewGuid();
		var mapping = new FinanceReportingAccountMapping
		{
			Id = 42,
			Version = 7,
			AccountingBookId = Guid.NewGuid(),
			AccountId = accountId,
			StatementSection = FinanceStatementSection.OperatingExpenses,
			CashFlowCategory = FinanceCashFlowCategory.None,
			TaxCategory = FinanceTaxReportCategory.None,
			SortOrder = 40,
			IsActive = true
		};
		var cashTarget = FinanceReportingMappingProjector.Targets.Single(value => value.Kind == FinanceReportingMappingTargetKind.CashAccount);
		var cogsTarget = FinanceReportingMappingProjector.Targets.Single(value => value.Kind == FinanceReportingMappingTargetKind.CostOfGoodsSold);

		var cash = FinanceReportingMappingProjector.ApplyTarget(mapping, cashTarget);
		var cogs = FinanceReportingMappingProjector.ApplyTarget(cash, cogsTarget);

		Assert.Equal(mapping.Id, cogs.Id);
		Assert.Equal(mapping.Version, cogs.Version);
		Assert.Equal(mapping.AccountingBookId, cogs.AccountingBookId);
		Assert.Equal(mapping.AccountId, cogs.AccountId);
		Assert.Equal(mapping.SortOrder, cogs.SortOrder);
		Assert.True(cogs.IsCashAccount);
		Assert.Equal(FinanceCashFlowCategory.None, cogs.CashFlowCategory);
		Assert.True(cogs.IsCostOfGoodsSold);
		Assert.Equal(FinanceStatementSection.CostOfGoodsSold, cogs.StatementSection);
	}

	[Fact]
	public void ProjectionOrdersPersistedMappingsBySortOrderBeforeUnmappedAccounts()
	{
		var first = new FinanceReportingAccountRecord(Guid.NewGuid(), "2000", "Second number", FinanceAccountType.Asset, true);
		var second = new FinanceReportingAccountRecord(Guid.NewGuid(), "1000", "First number", FinanceAccountType.Asset, true);
		var unmapped = new FinanceReportingAccountRecord(Guid.NewGuid(), "0500", "Unmapped", FinanceAccountType.Asset, true);
		var projection = FinanceReportingMappingProjector.Project(
			[first, second, unmapped],
			[
				new FinanceReportingAccountMapping { Id = 1, AccountId = first.Id, StatementSection = FinanceStatementSection.CurrentAssets, SortOrder = 20 },
				new FinanceReportingAccountMapping { Id = 2, AccountId = second.Id, StatementSection = FinanceStatementSection.CurrentAssets, SortOrder = 10 }
			]);

		Assert.Equal([second.Id, first.Id, unmapped.Id], projection.Rows.Select(value => value.AccountId).ToArray());
	}

	[Fact]
	public void TargetsAreExplicitAndNeverDerivedFromAccountNamesOrNumbers()
	{
		var targets = FinanceReportingMappingProjector.Targets;

		Assert.Contains(targets, value => value.StatementSection == FinanceStatementSection.CurrentAssets);
		Assert.Contains(targets, value => value.CashFlowCategory == FinanceCashFlowCategory.Operating);
		Assert.Contains(targets, value => value.TaxCategory == FinanceTaxReportCategory.OutputTax);
		Assert.Contains(targets, value => value.Kind == FinanceReportingMappingTargetKind.CashAccount);
		Assert.Contains(targets, value => value.Kind == FinanceReportingMappingTargetKind.CostOfGoodsSold);
		Assert.DoesNotContain(targets, value => value.Title.Contains("1000", StringComparison.Ordinal));
	}
}
