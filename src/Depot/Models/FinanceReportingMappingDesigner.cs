// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceReportingMappingState
{
	Mapped,
	Unmapped,
	Invalid,
	Inactive
}

public enum FinanceReportingMappingTargetKind
{
	StatementSection,
	CashFlowCategory,
	TaxCategory,
	CashAccount,
	CostOfGoodsSold
}

public sealed record FinanceReportingMappingTarget(
	string Key,
	FinanceReportingMappingTargetKind Kind,
	string Group,
	string Title,
	string Description,
	FinanceStatementSection? StatementSection = null,
	FinanceCashFlowCategory? CashFlowCategory = null,
	FinanceTaxReportCategory? TaxCategory = null);

public sealed record FinanceReportingMappingValidationResult(
	bool IsValid,
	IReadOnlyList<string> Errors)
{
	public string Summary => IsValid ? "Mapping is valid." : string.Join(Environment.NewLine, Errors);
}

public sealed record FinanceReportingMappingProjectionRow(
	FinanceReportingAccountRecord Account,
	FinanceReportingAccountMapping? Mapping,
	FinanceReportingMappingState State,
	string ValidationMessage)
{
	public Guid AccountId => Account.Id;
	public string AccountNumber => Account.Number;
	public string AccountName => Account.Name;
	public FinanceAccountType AccountType => Account.AccountType;
	public bool AccountActive => Account.IsActive;
	public bool HasMapping => Mapping is not null;
	public long? MappingId => Mapping?.Id;
	public int SortOrder => Mapping?.SortOrder ?? int.MaxValue;
	public FinanceStatementSection StatementSection => Mapping?.StatementSection ?? FinanceStatementSection.Unclassified;
	public FinanceCashFlowCategory CashFlowCategory => Mapping?.CashFlowCategory ?? FinanceCashFlowCategory.None;
	public FinanceTaxReportCategory TaxCategory => Mapping?.TaxCategory ?? FinanceTaxReportCategory.None;
	public bool IsCashAccount => Mapping?.IsCashAccount ?? false;
	public bool IsCostOfGoodsSold => Mapping?.IsCostOfGoodsSold ?? false;
	public string StateText => State.ToString();
}

public sealed record FinanceReportingMappingCoverage(
	int Total,
	int Mapped,
	int Unmapped,
	int Invalid,
	int Inactive)
{
	public int Covered => Mapped + Inactive;
	public decimal CoveragePercent => Total == 0 ? 100m : decimal.Round(Covered * 100m / Total, 1, MidpointRounding.AwayFromZero);
	public string Summary => $"{Mapped} mapped · {Unmapped} unmapped · {Invalid} invalid · {Inactive} inactive · {CoveragePercent:N1}% coverage";
}

public sealed record FinanceReportingMappingProjection(
	IReadOnlyList<FinanceReportingMappingProjectionRow> Rows,
	FinanceReportingMappingCoverage Coverage);

public static class FinanceReportingMappingValidator
{
	public static FinanceReportingMappingValidationResult Validate(
		FinanceReportingAccountMapping mapping,
		FinanceReportingAccountRecord account)
	{
		ArgumentNullException.ThrowIfNull(mapping);
		ArgumentNullException.ThrowIfNull(account);
		var errors = new List<string>();
		if (mapping.AccountId != account.Id)
			errors.Add("Reporting mapping account identity does not match the selected account.");
		if (!account.IsActive && mapping.IsActive)
			errors.Add("An inactive account cannot have an active reporting mapping.");
		if (mapping.IsCashAccount && mapping.CashFlowCategory != FinanceCashFlowCategory.None)
			errors.Add("Cash accounts must not classify themselves as operating, investing or financing counterpart accounts.");
		if (mapping.IsCostOfGoodsSold && account.AccountType != FinanceAccountType.Expense)
			errors.Add("Cost of Goods Sold mapping requires an expense account.");
		var validSection = mapping.StatementSection == FinanceStatementSection.Unclassified || account.AccountType switch
		{
			FinanceAccountType.Asset => mapping.StatementSection is FinanceStatementSection.CurrentAssets or FinanceStatementSection.NonCurrentAssets,
			FinanceAccountType.Liability => mapping.StatementSection is FinanceStatementSection.CurrentLiabilities or FinanceStatementSection.NonCurrentLiabilities,
			FinanceAccountType.Equity => mapping.StatementSection == FinanceStatementSection.Equity,
			FinanceAccountType.Revenue => mapping.StatementSection is FinanceStatementSection.Revenue or FinanceStatementSection.OtherIncomeExpense,
			FinanceAccountType.Expense => mapping.StatementSection is FinanceStatementSection.CostOfGoodsSold or FinanceStatementSection.OperatingExpenses or FinanceStatementSection.OtherIncomeExpense,
			_ => false
		};
		if (!validSection)
			errors.Add("Financial-statement section is incompatible with the account type.");
		return new FinanceReportingMappingValidationResult(errors.Count == 0, errors);
	}

	public static void ThrowIfInvalid(FinanceReportingAccountMapping mapping, FinanceReportingAccountRecord account)
	{
		var validation = Validate(mapping, account);
		if (!validation.IsValid) throw new InvalidOperationException(validation.Errors[0]);
	}
}

public static class FinanceReportingMappingProjector
{
	public static FinanceReportingMappingProjection Project(
		IEnumerable<FinanceReportingAccountRecord> accounts,
		IEnumerable<FinanceReportingAccountMapping> mappings)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(mappings);
		var mappingByAccount = mappings
			.GroupBy(value => value.AccountId)
			.ToDictionary(group => group.Key, group => group.OrderByDescending(value => value.Id).First());
		var rows = accounts
			.Select(account =>
			{
				mappingByAccount.TryGetValue(account.Id, out var mapping);
				if (mapping is null)
					return new FinanceReportingMappingProjectionRow(account, null, account.IsActive ? FinanceReportingMappingState.Unmapped : FinanceReportingMappingState.Inactive, account.IsActive ? "No explicit reporting mapping." : "Account is inactive and has no active mapping.");
				var validation = FinanceReportingMappingValidator.Validate(mapping, account);
				var state = !validation.IsValid
					? FinanceReportingMappingState.Invalid
					: !account.IsActive || !mapping.IsActive
						? FinanceReportingMappingState.Inactive
						: FinanceReportingMappingState.Mapped;
				return new FinanceReportingMappingProjectionRow(account, mapping, state, validation.Summary);
			})
			.OrderBy(value => value.Mapping?.SortOrder ?? int.MaxValue)
			.ThenBy(value => value.Account.Number, StringComparer.OrdinalIgnoreCase)
			.ThenBy(value => value.Account.Id)
			.ToArray();
		var coverage = new FinanceReportingMappingCoverage(
			rows.Length,
			rows.Count(value => value.State == FinanceReportingMappingState.Mapped),
			rows.Count(value => value.State == FinanceReportingMappingState.Unmapped),
			rows.Count(value => value.State == FinanceReportingMappingState.Invalid),
			rows.Count(value => value.State == FinanceReportingMappingState.Inactive));
		return new FinanceReportingMappingProjection(rows, coverage);
	}

	public static IReadOnlyList<FinanceReportingMappingTarget> Targets { get; } = BuildTargets();

	private static IReadOnlyList<FinanceReportingMappingTarget> BuildTargets()
	{
		var targets = new List<FinanceReportingMappingTarget>();
		targets.AddRange(Enum.GetValues<FinanceStatementSection>()
			.Where(value => value != FinanceStatementSection.Unclassified)
			.Select(value => new FinanceReportingMappingTarget($"statement:{value}", FinanceReportingMappingTargetKind.StatementSection, "Statement sections", value.ToString(), "Balance Sheet / Profit & Loss classification", StatementSection: value)));
		targets.AddRange(Enum.GetValues<FinanceCashFlowCategory>()
			.Where(value => value != FinanceCashFlowCategory.None)
			.Select(value => new FinanceReportingMappingTarget($"cashflow:{value}", FinanceReportingMappingTargetKind.CashFlowCategory, "Cash flow", value.ToString(), "Cash-flow counterpart category", CashFlowCategory: value)));
		targets.AddRange(Enum.GetValues<FinanceTaxReportCategory>()
			.Where(value => value != FinanceTaxReportCategory.None)
			.Select(value => new FinanceReportingMappingTarget($"tax:{value}", FinanceReportingMappingTargetKind.TaxCategory, "Tax", value.ToString(), "Tax reporting category", TaxCategory: value)));
		targets.Add(new FinanceReportingMappingTarget("flag:cash", FinanceReportingMappingTargetKind.CashAccount, "Flags", "Cash account", "Marks the account as a cash account."));
		targets.Add(new FinanceReportingMappingTarget("flag:cogs", FinanceReportingMappingTargetKind.CostOfGoodsSold, "Flags", "Cost of Goods Sold", "Marks the account as COGS."));
		return targets;
	}
}
