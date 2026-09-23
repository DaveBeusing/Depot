// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceBudgetingService
{
	private const int MaxBatchLines = 10000;
	private const int MaxCsvCharacters = 5_000_000;

	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceBudgetingRepository _budgets;
	private readonly FinanceFinancialReportingService _reporting;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly ApprovalPolicyService _approvalPolicies;

	public FinanceBudgetingService(
		IDatabaseTransactionRunner transactions,
		FinanceBudgetingRepository budgets,
		FinanceFinancialReportingService reporting,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization,
		ApprovalPolicyService approvalPolicies)
	{
		_transactions = transactions;
		_budgets = budgets;
		_reporting = reporting;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_approvalPolicies = approvalPolicies;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.FinanceBudgetingView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.FinanceBudgetingManage);
	public bool CanApprove => _authorization.HasPermission(ApplicationPermission.FinanceBudgetingApprove);
	public bool CanLock => _authorization.HasPermission(ApplicationPermission.FinanceBudgetingLock);

	public Task<PageResult<FinanceBudgetVersion>> SearchVersionsAsync(
		FinanceBudgetListFilter? filter = null,
		int pageNumber = 1,
		int pageSize = 100,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		return _budgets.SearchVersionsAsync(filter ?? new FinanceBudgetListFilter(), pageNumber, pageSize, cancellationToken);
	}

	public Task<FinanceBudgetVersion?> GetVersionAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequirePositive(id, nameof(id));
		return _budgets.GetVersionAsync(id, cancellationToken);
	}

	public Task<PageResult<FinanceBudgetLine>> GetLinesAsync(
		long budgetVersionId,
		int pageNumber = 1,
		int pageSize = 200,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		return _budgets.GetLinesAsync(budgetVersionId, pageNumber, pageSize, cancellationToken);
	}

	public Task<FinanceBudgetSummary> GetSummaryAsync(long budgetVersionId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		return _budgets.GetSummaryAsync(budgetVersionId, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBudgetOption>> GetLegalEntitiesAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		return _budgets.GetLegalEntitiesAsync(cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBudgetOption>> GetAccountingBooksAsync(Guid? legalEntityId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		return _budgets.GetAccountingBooksAsync(legalEntityId, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBudgetOption>> GetFiscalCalendarsAsync(Guid legalEntityId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequireGuid(legalEntityId, nameof(legalEntityId));
		return _budgets.GetFiscalCalendarsAsync(legalEntityId, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBudgetPeriodOption>> GetPeriodsAsync(Guid fiscalCalendarId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequireGuid(fiscalCalendarId, nameof(fiscalCalendarId));
		return _budgets.GetPeriodsAsync(fiscalCalendarId, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBudgetAccountOption>> GetAccountsAsync(Guid accountingBookId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequireGuid(accountingBookId, nameof(accountingBookId));
		return _budgets.GetAccountsAsync(accountingBookId, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBudgetDimensionOption>> GetDimensionsAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		return _budgets.GetDimensionsAsync(cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBudgetDimensionValueOption>> GetDimensionValuesAsync(Guid dimensionId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequireGuid(dimensionId, nameof(dimensionId));
		return _budgets.GetDimensionValuesAsync(dimensionId, cancellationToken);
	}

	public async Task<FinanceBudgetVersion> CreateDraftAsync(
		Guid legalEntityId,
		Guid accountingBookId,
		Guid fiscalCalendarId,
		int fiscalYear,
		string name,
		long? ownerUserId = null,
		string? description = null,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		ValidateFiscalYear(fiscalYear);
		RequireGuid(legalEntityId, nameof(legalEntityId));
		RequireGuid(accountingBookId, nameof(accountingBookId));
		RequireGuid(fiscalCalendarId, nameof(fiscalCalendarId));
		var normalizedName = Required(name, 200);
		var normalizedDescription = Optional(description, 1000);
		var owner = ownerUserId ?? user.Id;
		if (owner <= 0) throw new ArgumentOutOfRangeException(nameof(ownerUserId));

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var book = await RequireBookAsync(transaction, accountingBookId, legalEntityId, token);
			if (!await _budgets.FiscalCalendarMatchesEntityAsync(transaction, fiscalCalendarId, legalEntityId, token))
				throw new InvalidOperationException("The fiscal calendar is not active for the selected legal entity.");
			var versionNumber = await _budgets.GetNextVersionNumberAsync(transaction, legalEntityId, book.Id, fiscalYear, normalizedName, token);
			var now = DateTime.UtcNow;
			var draft = new FinanceBudgetVersion
			{
				LegalEntityId = legalEntityId,
				AccountingBookId = book.Id,
				FiscalCalendarId = fiscalCalendarId,
				FiscalYear = fiscalYear,
				Name = normalizedName,
				BudgetVersionNumber = versionNumber,
				Currency = book.ReportingCurrency,
				Status = FinanceBudgetStatus.Draft,
				OwnerUserId = owner,
				Description = normalizedDescription,
				SourceKind = FinanceBudgetSourceKind.Manual,
				CreatedAtUtc = now,
				CreatedByUserId = user.Id,
				UpdatedAtUtc = now,
				UpdatedByUserId = user.Id
			};
			var id = await _budgets.CreateVersionAsync(transaction, draft, token);
			var created = draft with { Id = id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceBudgetVersion> UpdateDraftMetadataAsync(
		long budgetVersionId,
		long expectedVersion,
		long ownerUserId,
		string? description,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		RequirePositive(expectedVersion, nameof(expectedVersion));
		RequirePositive(ownerUserId, nameof(ownerUserId));
		var normalizedDescription = Optional(description, 1000);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireDraft(before);
			RequireVersion(before, expectedVersion);
			var after = before with
			{
				OwnerUserId = ownerUserId,
				Description = normalizedDescription,
				UpdatedAtUtc = DateTime.UtcNow,
				UpdatedByUserId = user.Id
			};
			await UpdateBudgetAsync(transaction, before, after, token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after with { Version = before.Version + 1 }), token);
			return after with { Version = before.Version + 1 };
		}, cancellationToken);
	}

	public async Task<FinanceBudgetLine> SaveLineAsync(
		long budgetVersionId,
		FinanceBudgetLine value,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(value);
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(budgetVersionId, nameof(budgetVersionId));

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var budget = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireDraft(budget);
			var normalized = NormalizeLine(value with { BudgetVersionId = budgetVersionId });
			await ValidateLineAsync(transaction, budget, normalized, token);
			if (await _budgets.LineKeyExistsAsync(transaction, normalized, normalized.Id == 0 ? null : normalized.Id, token))
				throw new InvalidOperationException("A budget line already exists for the selected account, period and dimension key.");

			FinanceBudgetLine result;
			if (normalized.Id == 0)
			{
				var id = await _budgets.CreateLineAsync(transaction, normalized, token);
				result = normalized with { Id = id, Version = 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, result), token);
			}
			else
			{
				var before = await _budgets.GetLineAsync(transaction, budgetVersionId, normalized.Id, token)
					?? throw new InvalidOperationException("Budget line was not found.");
				if (before.Version != normalized.Version) throw new ConcurrencyConflictException("finance budget line");
				if (await _budgets.UpdateLineAsync(transaction, normalized, before.Version, token) != 1)
					throw new ConcurrencyConflictException("finance budget line");
				result = normalized with { Version = before.Version + 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(result.Id, before, result), token);
			}
			await TouchBudgetAsync(transaction, budget, user.Id, token);
			return result;
		}, cancellationToken);
	}

	public async Task DeleteLineAsync(
		long budgetVersionId,
		long lineId,
		long expectedLineVersion,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		RequirePositive(lineId, nameof(lineId));
		RequirePositive(expectedLineVersion, nameof(expectedLineVersion));

		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var budget = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireDraft(budget);
			var before = await _budgets.GetLineAsync(transaction, budgetVersionId, lineId, token)
				?? throw new InvalidOperationException("Budget line was not found.");
			if (before.Version != expectedLineVersion) throw new ConcurrencyConflictException("finance budget line");
			if (await _budgets.DeleteLineAsync(transaction, budgetVersionId, lineId, expectedLineVersion, token) != 1)
				throw new ConcurrencyConflictException("finance budget line");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(lineId, "Deleted", before, (FinanceBudgetLine?)null), token);
			await TouchBudgetAsync(transaction, budget, user.Id, token);
			return true;
		}, cancellationToken);
	}

	public Task<FinanceBudgetVersion> CopyVersionAsync(
		long sourceBudgetVersionId,
		string? newName = null,
		CancellationToken cancellationToken = default) =>
		CopyCoreAsync(sourceBudgetVersionId, null, newName, FinanceBudgetSourceKind.VersionCopy, false, cancellationToken);

	public Task<FinanceBudgetVersion> CreateAmendmentAsync(
		long sourceBudgetVersionId,
		CancellationToken cancellationToken = default) =>
		CopyCoreAsync(sourceBudgetVersionId, null, null, FinanceBudgetSourceKind.VersionCopy, true, cancellationToken);

	public Task<FinanceBudgetVersion> CreateFromPriorYearAsync(
		long sourceBudgetVersionId,
		int targetFiscalYear,
		string? newName = null,
		CancellationToken cancellationToken = default) =>
		CopyCoreAsync(sourceBudgetVersionId, targetFiscalYear, newName, FinanceBudgetSourceKind.PriorYear, true, cancellationToken);

	public async Task<IReadOnlyList<FinanceBudgetLine>> SpreadAnnualAmountAsync(
		long budgetVersionId,
		Guid accountId,
		decimal annualAmount,
		Guid? dimensionId = null,
		Guid? dimensionValueId = null,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		RequireGuid(accountId, nameof(accountId));
		ValidateDimensionPair(dimensionId, dimensionValueId);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var budget = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireDraft(budget);
			var periods = (await _budgets.GetPeriodsAsync(transaction, budget.FiscalCalendarId, token))
				.Where(value => value.StartDate.Year == budget.FiscalYear)
				.OrderBy(value => value.StartDate)
				.ThenBy(value => value.Code, StringComparer.Ordinal)
				.ToArray();
			if (periods.Length == 0)
				throw new InvalidOperationException("The fiscal calendar has no periods whose start date belongs to the budget fiscal year.");
			if (!await _budgets.AccountBelongsToBookAsync(transaction, budget.AccountingBookId, accountId, token))
				throw new InvalidOperationException("The account does not belong to the selected accounting book.");
			if (dimensionId.HasValue && !await _budgets.DimensionValueMatchesAsync(transaction, dimensionId.Value, dimensionValueId!.Value, token))
				throw new InvalidOperationException("The accounting dimension value is invalid.");

			var baseAmount = Math.Round(annualAmount / periods.Length, 9, MidpointRounding.ToZero);
			var assigned = 0m;
			var created = new List<FinanceBudgetLine>(periods.Length);
			for (var index = 0; index < periods.Length; index++)
			{
				var amount = index == periods.Length - 1 ? annualAmount - assigned : baseAmount;
				assigned += amount;
				var line = new FinanceBudgetLine
				{
					BudgetVersionId = budget.Id,
					AccountId = accountId,
					AccountingPeriodId = periods[index].Id,
					DimensionId = dimensionId,
					DimensionValueId = dimensionValueId,
					Amount = amount,
					SourceEvidence = $"Equal-period spread of {annualAmount.ToString("G29", CultureInfo.InvariantCulture)} {budget.Currency.Value}; remainder assigned to final period."
				};
				if (await _budgets.LineKeyExistsAsync(transaction, line, null, token))
					throw new InvalidOperationException($"A budget line already exists for period '{periods[index].Code}' and the selected account/dimension key.");
				var id = await _budgets.CreateLineAsync(transaction, line, token);
				var persisted = line with { Id = id };
				created.Add(persisted);
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, persisted), token);
			}
			await TouchBudgetAsync(transaction, budget, user.Id, token);
			return (IReadOnlyList<FinanceBudgetLine>)created;
		}, cancellationToken);
	}

	public async Task<FinanceBudgetImportPreview> PreviewImportAsync(
		long budgetVersionId,
		string csv,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		var parsed = ParseCsv(csv);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var budget = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireDraft(budget);
			return await ValidateImportRowsAsync(transaction, budget, parsed, token);
		}, cancellationToken);
	}

	public async Task<int> ApplyImportAsync(
		long budgetVersionId,
		string csv,
		bool replaceExisting = false,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		var parsed = ParseCsv(csv);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var budget = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireDraft(budget);
			var preview = await ValidateImportRowsAsync(transaction, budget, parsed, token);
			if (!preview.CanApply)
				throw new InvalidOperationException($"Budget import contains {preview.InvalidRowCount} invalid rows and {preview.DuplicateRowCount} duplicate keys.");
			var existing = await _budgets.CountLinesAsync(transaction, budgetVersionId, token);
			if (!replaceExisting && existing != 0)
				throw new InvalidOperationException("The budget already contains lines. Use explicit replace mode or import into an empty draft.");
			if (replaceExisting) await _budgets.DeleteAllLinesAsync(transaction, budgetVersionId, token);

			foreach (var row in preview.Rows)
			{
				token.ThrowIfCancellationRequested();
				var line = new FinanceBudgetLine
				{
					BudgetVersionId = budgetVersionId,
					AccountId = row.AccountId,
					AccountingPeriodId = row.AccountingPeriodId,
					DimensionId = row.DimensionId,
					DimensionValueId = row.DimensionValueId,
					Amount = row.Amount,
					SourceEvidence = Optional(row.SourceEvidence, 500) ?? $"CSV import row {row.RowNumber.ToString(CultureInfo.InvariantCulture)}"
				};
				await _budgets.CreateLineAsync(transaction, line, token);
			}
			var imported = budget with
			{
				SourceKind = FinanceBudgetSourceKind.CsvImport,
				UpdatedAtUtc = DateTime.UtcNow,
				UpdatedByUserId = user.Id
			};
			await UpdateBudgetAsync(transaction, budget, imported, token);
			await _auditEntries.CreateAsync(
				transaction,
				_audit.CreateActionEntry(
					budget.Id,
					replaceExisting ? "BudgetLinesReplacedFromCsv" : "BudgetLinesImportedFromCsv",
					budget,
					imported with { Version = budget.Version + 1 }),
				token);
			return preview.ValidRowCount;
		}, cancellationToken);
	}

	public async Task<string> ExportCsvAsync(long budgetVersionId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		var version = await _budgets.GetVersionAsync(budgetVersionId, cancellationToken)
			?? throw new InvalidOperationException("Budget version was not found.");
		var builder = new StringBuilder();
		builder.AppendLine("AccountId,AccountingPeriodId,DimensionId,DimensionValueId,Amount,SourceEvidence");
		var pageNumber = 1;
		var exported = 0;
		while (true)
		{
			var page = await _budgets.GetLinesAsync(budgetVersionId, pageNumber, 500, cancellationToken);
			foreach (var line in page.Items)
			{
				builder.AppendLine(string.Join(',',
					Csv(line.AccountId.ToString("D")),
					Csv(line.AccountingPeriodId.ToString("D")),
					Csv(line.DimensionId?.ToString("D")),
					Csv(line.DimensionValueId?.ToString("D")),
					Csv(line.Amount.ToString("G29", CultureInfo.InvariantCulture)),
					Csv(line.SourceEvidence)));
			}
			exported += page.Items.Count;
			if (exported > MaxBatchLines)
				throw new InvalidOperationException($"Budget export exceeds the bounded limit of {MaxBatchLines} lines.");
			if (!page.HasNextPage) break;
			pageNumber++;
		}
		_ = version;
		return builder.ToString();
	}

	public async Task<FinanceBudgetVersion> SubmitAsync(
		long budgetVersionId,
		long expectedVersion,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		RequirePositive(expectedVersion, nameof(expectedVersion));
		var current = await _budgets.GetVersionAsync(budgetVersionId, cancellationToken)
			?? throw new InvalidOperationException("Budget version was not found.");
		RequireDraft(current);
		RequireVersion(current, expectedVersion);
		var approvalAmount = await _budgets.GetApprovalAmountAsync(budgetVersionId, cancellationToken);
		var snapshot = await _approvalPolicies.ResolveSnapshotAsync(
			new ApprovalSubjectAttributes(
				ApprovalSubjectKind.FinanceBudget,
				budgetVersionId.ToString(CultureInfo.InvariantCulture),
				approvalAmount,
				current.Currency.Value,
				current.LegalEntityId,
				current.AccountingBookId),
			cancellationToken);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireDraft(before);
			RequireVersion(before, expectedVersion);
			if (await _budgets.CountLinesAsync(transaction, budgetVersionId, token) == 0)
				throw new InvalidOperationException("A budget must contain at least one line before submission.");
			var after = before with
			{
				Status = FinanceBudgetStatus.PendingApproval,
				ApprovalInstanceId = snapshot.InstanceId,
				UpdatedAtUtc = DateTime.UtcNow,
				UpdatedByUserId = user.Id
			};
			await UpdateBudgetAsync(transaction, before, after, token);
			await _approvalPolicies.StartResolvedAsync(transaction, snapshot, token);
			var persisted = after with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(after.Id, "Submitted", before, persisted), token);
			return persisted;
		}, cancellationToken);
	}

	public Task<FinanceBudgetVersion> ApproveAsync(
		long budgetVersionId,
		long expectedVersion,
		string? comment = null,
		CancellationToken cancellationToken = default) =>
		DecideAsync(budgetVersionId, expectedVersion, ApprovalDecisionKind.Approved, comment, cancellationToken);

	public Task<FinanceBudgetVersion> RejectAsync(
		long budgetVersionId,
		long expectedVersion,
		string? comment = null,
		CancellationToken cancellationToken = default) =>
		DecideAsync(budgetVersionId, expectedVersion, ApprovalDecisionKind.Rejected, comment, cancellationToken);

	public async Task<FinanceBudgetVersion> LockAsync(
		long budgetVersionId,
		long expectedVersion,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingLock);
		var user = RequireUser();
		return await TransitionAsync(
			budgetVersionId,
			expectedVersion,
			value => value.Status == FinanceBudgetStatus.Approved,
			FinanceBudgetStatus.Locked,
			"Locked",
			user.Id,
			cancellationToken);
	}

	public async Task<FinanceBudgetVersion> SupersedeAsync(
		long budgetVersionId,
		long expectedVersion,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingLock);
		var user = RequireUser();
		return await TransitionAsync(
			budgetVersionId,
			expectedVersion,
			value => value.Status is FinanceBudgetStatus.Approved or FinanceBudgetStatus.Locked,
			FinanceBudgetStatus.Superseded,
			"Superseded",
			user.Id,
			cancellationToken);
	}

	public async Task<FinanceBudgetVersion> ArchiveAsync(
		long budgetVersionId,
		long expectedVersion,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		return await TransitionAsync(
			budgetVersionId,
			expectedVersion,
			value => value.Status is FinanceBudgetStatus.Draft or FinanceBudgetStatus.Superseded,
			FinanceBudgetStatus.Archived,
			"Archived",
			user.Id,
			cancellationToken);
	}

	public async Task<IReadOnlyList<FinanceBudgetVarianceRow>> GetProfitLossVarianceAsync(
		long budgetVersionId,
		Guid? dimensionId = null,
		Guid? dimensionValueId = null,
		CancellationToken cancellationToken = default) =>
		await GetVarianceAsync(budgetVersionId, true, dimensionId, dimensionValueId, cancellationToken);

	public async Task<IReadOnlyList<FinanceBudgetVarianceRow>> GetGeneralVarianceAsync(
		long budgetVersionId,
		Guid? dimensionId = null,
		Guid? dimensionValueId = null,
		CancellationToken cancellationToken = default) =>
		await GetVarianceAsync(budgetVersionId, false, dimensionId, dimensionValueId, cancellationToken);

	private async Task<FinanceBudgetVersion> CopyCoreAsync(
		long sourceBudgetVersionId,
		int? targetFiscalYear,
		string? newName,
		FinanceBudgetSourceKind sourceKind,
		bool requireImmutableSource,
		CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(sourceBudgetVersionId, nameof(sourceBudgetVersionId));
		if (targetFiscalYear.HasValue) ValidateFiscalYear(targetFiscalYear.Value);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var source = await RequireBudgetAsync(transaction, sourceBudgetVersionId, token);
			if (requireImmutableSource && !source.IsImmutable)
				throw new InvalidOperationException("The source budget must be approved, locked, superseded or archived.");
			if (sourceKind == FinanceBudgetSourceKind.PriorYear && targetFiscalYear != source.FiscalYear + 1)
				throw new InvalidOperationException("Create-from-prior-year requires the target fiscal year to immediately follow the source fiscal year.");
			var lineCount = await _budgets.CountLinesAsync(transaction, source.Id, token);
			if (lineCount > MaxBatchLines)
				throw new InvalidOperationException($"Budget copy exceeds the bounded limit of {MaxBatchLines} lines.");
			var sourceLines = await _budgets.ListLinesAsync(transaction, source.Id, token);
			var targetYear = targetFiscalYear ?? source.FiscalYear;
			var targetName = string.IsNullOrWhiteSpace(newName) ? source.Name : Required(newName, 200);
			var nextVersion = await _budgets.GetNextVersionNumberAsync(transaction, source.LegalEntityId, source.AccountingBookId, targetYear, targetName, token);
			var now = DateTime.UtcNow;
			var draft = source with
			{
				Id = 0,
				Version = 1,
				FiscalYear = targetYear,
				Name = targetName,
				BudgetVersionNumber = nextVersion,
				Status = FinanceBudgetStatus.Draft,
				OwnerUserId = user.Id,
				SourceKind = sourceKind,
				SourceBudgetVersionId = source.Id,
				ApprovalInstanceId = null,
				CreatedAtUtc = now,
				CreatedByUserId = user.Id,
				UpdatedAtUtc = now,
				UpdatedByUserId = user.Id
			};
			var newId = await _budgets.CreateVersionAsync(transaction, draft, token);
			var created = draft with { Id = newId };

			Dictionary<Guid, Guid>? periodMap = null;
			if (sourceKind == FinanceBudgetSourceKind.PriorYear)
			{
				var periods = await _budgets.GetPeriodsAsync(transaction, source.FiscalCalendarId, token);
				var sourcePeriods = periods.Where(value => value.StartDate.Year == source.FiscalYear).OrderBy(value => value.StartDate).ThenBy(value => value.Code, StringComparer.Ordinal).ToArray();
				var targetPeriods = periods.Where(value => value.StartDate.Year == targetYear).OrderBy(value => value.StartDate).ThenBy(value => value.Code, StringComparer.Ordinal).ToArray();
				if (sourcePeriods.Length == 0 || sourcePeriods.Length != targetPeriods.Length)
					throw new InvalidOperationException("Prior-year copy requires the source and target fiscal years to expose the same number of calendar periods.");
				periodMap = sourcePeriods.Select((period, index) => (period.Id, Target: targetPeriods[index].Id)).ToDictionary(value => value.Id, value => value.Target);
			}

			foreach (var sourceLine in sourceLines)
			{
				token.ThrowIfCancellationRequested();
				var targetPeriod = sourceLine.AccountingPeriodId;
				if (periodMap is not null && !periodMap.TryGetValue(sourceLine.AccountingPeriodId, out targetPeriod))
					throw new InvalidOperationException("A source budget line references a period outside the source fiscal year.");
				var copy = sourceLine with
				{
					Id = 0,
					Version = 1,
					BudgetVersionId = newId,
					AccountingPeriodId = targetPeriod,
					SourceEvidence = sourceKind == FinanceBudgetSourceKind.PriorYear
						? $"Prior-year source budget {source.Id}, line {sourceLine.Id}."
						: $"Copied from budget {source.Id}, line {sourceLine.Id}."
				};
				await _budgets.CreateLineAsync(transaction, copy, token);
			}
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(newId, created), token);
			return created;
		}, cancellationToken);
	}

	private async Task<FinanceBudgetVersion> DecideAsync(
		long budgetVersionId,
		long expectedVersion,
		ApprovalDecisionKind decision,
		string? comment,
		CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingApprove);
		var user = RequireUser();
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		RequirePositive(expectedVersion, nameof(expectedVersion));
		var prepared = await _approvalPolicies.PrepareDecisionAsync(
			ApprovalSubjectKind.FinanceBudget,
			budgetVersionId.ToString(CultureInfo.InvariantCulture),
			decision,
			comment,
			cancellationToken);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await RequireBudgetAsync(transaction, budgetVersionId, token);
			if (before.Status != FinanceBudgetStatus.PendingApproval)
				throw new InvalidOperationException("Only a pending budget can receive an approval decision.");
			RequireVersion(before, expectedVersion);
			if (before.ApprovalInstanceId != prepared.Instance.Id)
				throw new ConcurrencyConflictException("finance budget approval");
			await _approvalPolicies.ApplyPreparedDecisionAsync(transaction, prepared, token);
			var targetStatus = decision == ApprovalDecisionKind.Rejected
				? FinanceBudgetStatus.Draft
				: prepared.IsFinalApproval
					? FinanceBudgetStatus.Approved
					: FinanceBudgetStatus.PendingApproval;
			var after = before with
			{
				Status = targetStatus,
				UpdatedAtUtc = DateTime.UtcNow,
				UpdatedByUserId = user.Id
			};
			await UpdateBudgetAsync(transaction, before, after, token);
			var persisted = after with { Version = before.Version + 1 };
			var action = decision == ApprovalDecisionKind.Rejected
				? "RejectedToDraft"
				: prepared.IsFinalApproval ? "Approved" : "ApprovalStageCompleted";
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(before.Id, action, before, persisted), token);
			return persisted;
		}, cancellationToken);
	}

	private async Task<FinanceBudgetVersion> TransitionAsync(
		long budgetVersionId,
		long expectedVersion,
		Func<FinanceBudgetVersion, bool> allowed,
		FinanceBudgetStatus status,
		string action,
		long userId,
		CancellationToken cancellationToken)
	{
		RequirePositive(budgetVersionId, nameof(budgetVersionId));
		RequirePositive(expectedVersion, nameof(expectedVersion));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await RequireBudgetAsync(transaction, budgetVersionId, token);
			RequireVersion(before, expectedVersion);
			if (!allowed(before)) throw new InvalidOperationException($"Budget status '{before.Status}' cannot transition to '{status}'.");
			var after = before with { Status = status, UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = userId };
			await UpdateBudgetAsync(transaction, before, after, token);
			var persisted = after with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(before.Id, action, before, persisted), token);
			return persisted;
		}, cancellationToken);
	}

	private async Task<IReadOnlyList<FinanceBudgetVarianceRow>> GetVarianceAsync(
		long budgetVersionId,
		bool profitLossOnly,
		Guid? dimensionId,
		Guid? dimensionValueId,
		CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingView);
		ValidateDimensionPair(dimensionId, dimensionValueId);
		var budget = await _budgets.GetVersionAsync(budgetVersionId, cancellationToken)
			?? throw new InvalidOperationException("Budget version was not found.");
		var aggregates = await _transactions.ExecuteAsync(
			(transaction, token) => _budgets.GetAggregatesAsync(transaction, budgetVersionId, dimensionId, dimensionValueId, token),
			cancellationToken);
		var budgetRows = (profitLossOnly
				? aggregates.Where(value => value.AccountType is FinanceAccountType.Revenue or FinanceAccountType.Expense)
				: aggregates)
			.ToArray();
		var periods = (await _budgets.GetPeriodsAsync(budget.FiscalCalendarId, cancellationToken))
			.Where(value => value.StartDate.Year == budget.FiscalYear)
			.OrderBy(value => value.StartDate)
			.ThenBy(value => value.Code, StringComparer.Ordinal)
			.ToArray();
		var actuals = new Dictionary<(Guid AccountId, Guid PeriodId), (decimal Amount, string Number, string Name)>();
		foreach (var period in periods)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var report = await _reporting.GenerateAsync(
				new FinanceReportParameters
				{
					Kind = profitLossOnly ? FinanceReportKind.ProfitLoss : FinanceReportKind.TrialBalance,
					AccountingBookId = budget.AccountingBookId,
					FromDate = period.StartDate,
					ToDate = period.EndDate,
					DimensionId = dimensionId,
					DimensionValueId = dimensionValueId,
					IncludeZeroBalances = false
				},
				cancellationToken);
			foreach (var row in report.Rows)
			{
				if (!Guid.TryParse(row.Key, out var accountId)) continue;
				var amount = profitLossOnly ? row.Amount : row.Debit - row.Credit;
				actuals[(accountId, period.Id)] = (amount, row.AccountNumber ?? string.Empty, row.AccountName ?? row.Label);
			}
		}

		var budgetsByKey = budgetRows.ToDictionary(
			value => (value.AccountId, value.AccountingPeriodId),
			value => profitLossOnly && value.AccountType == FinanceAccountType.Revenue ? -value.RawBudget : value.RawBudget);
		var metadata = budgetRows.ToDictionary(
			value => (value.AccountId, value.AccountingPeriodId),
			value => (value.AccountNumber, value.AccountName, value.PeriodCode, value.PeriodStart));
		var keys = budgetsByKey.Keys.Union(actuals.Keys)
			.OrderBy(key => metadata.TryGetValue(key, out var meta) ? meta.PeriodStart : periods.First(value => value.Id == key.PeriodId).StartDate)
			.ThenBy(key => metadata.TryGetValue(key, out var meta) ? meta.AccountNumber : actuals[key].Number, StringComparer.Ordinal)
			.ToArray();
		var results = new List<FinanceBudgetVarianceRow>(keys.Length);
		foreach (var key in keys)
		{
			var actual = actuals.GetValueOrDefault(key);
			if (!metadata.TryGetValue(key, out var meta))
			{
				var period = periods.First(value => value.Id == key.PeriodId);
				meta = (actual.Number, actual.Name, period.Code, period.StartDate);
			}
			results.Add(new FinanceBudgetVarianceRow(
				key.AccountId,
				meta.AccountNumber,
				meta.AccountName,
				key.PeriodId,
				meta.PeriodCode,
				dimensionId,
				dimensionValueId,
				actual.Amount,
				budgetsByKey.GetValueOrDefault(key)));
		}
		return results;
	}

	private async Task<FinanceBudgetImportPreview> ValidateImportRowsAsync(
		DatabaseTransactionContext transaction,
		FinanceBudgetVersion budget,
		IReadOnlyList<FinanceBudgetImportRow> parsed,
		CancellationToken cancellationToken)
	{
		var result = new List<FinanceBudgetImportRow>(parsed.Count);
		var seen = new HashSet<(Guid AccountId, Guid PeriodId, Guid? DimensionId, Guid? DimensionValueId)>();
		var duplicateCount = 0;
		var accountCache = new Dictionary<Guid, bool>();
		var periodCache = new Dictionary<Guid, bool>();
		var dimensionCache = new Dictionary<(Guid, Guid), bool>();
		foreach (var row in parsed)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var errors = row.Errors.ToList();
			if (errors.Count == 0)
			{
				if (!accountCache.TryGetValue(row.AccountId, out var accountValid))
				{
					accountValid = await _budgets.AccountBelongsToBookAsync(transaction, budget.AccountingBookId, row.AccountId, cancellationToken);
					accountCache[row.AccountId] = accountValid;
				}
				if (!accountValid) errors.Add("Account does not belong to the budget accounting book.");

				if (!periodCache.TryGetValue(row.AccountingPeriodId, out var periodValid))
				{
					periodValid = await _budgets.PeriodBelongsToCalendarAsync(transaction, budget.FiscalCalendarId, row.AccountingPeriodId, cancellationToken);
					periodCache[row.AccountingPeriodId] = periodValid;
				}
				if (!periodValid) errors.Add("Accounting period does not belong to the budget fiscal calendar.");

				if (row.DimensionId.HasValue)
				{
					var pair = (row.DimensionId.Value, row.DimensionValueId!.Value);
					if (!dimensionCache.TryGetValue(pair, out var dimensionValid))
					{
						dimensionValid = await _budgets.DimensionValueMatchesAsync(transaction, pair.Item1, pair.Item2, cancellationToken);
						dimensionCache[pair] = dimensionValid;
					}
					if (!dimensionValid) errors.Add("Accounting dimension value is invalid.");
				}
				var key = (row.AccountId, row.AccountingPeriodId, row.DimensionId, row.DimensionValueId);
				if (!seen.Add(key))
				{
					errors.Add("Duplicate account/period/dimension key in import.");
					duplicateCount++;
				}
			}
			result.Add(row with { Errors = errors });
		}
		var invalid = result.Count(value => !value.IsValid);
		return new FinanceBudgetImportPreview(result, result.Count - invalid, invalid, duplicateCount);
	}

	private static IReadOnlyList<FinanceBudgetImportRow> ParseCsv(string csv)
	{
		if (csv is null) throw new ArgumentNullException(nameof(csv));
		if (csv.Length > MaxCsvCharacters) throw new InvalidOperationException($"Budget CSV exceeds the maximum size of {MaxCsvCharacters} characters.");
		var records = ReadCsvRecords(csv);
		if (records.Count == 0) throw new InvalidOperationException("Budget CSV is empty.");
		var header = records[0];
		var columns = header
			.Select((name, index) => (Name: name.Trim(), Index: index))
			.ToDictionary(value => value.Name, value => value.Index, StringComparer.OrdinalIgnoreCase);
		foreach (var required in new[] { "AccountId", "AccountingPeriodId", "Amount" })
			if (!columns.ContainsKey(required)) throw new InvalidOperationException($"Budget CSV requires column '{required}'.");
		var result = new List<FinanceBudgetImportRow>();
		for (var index = 1; index < records.Count; index++)
		{
			if (records[index].All(string.IsNullOrWhiteSpace)) continue;
			if (result.Count >= MaxBatchLines) throw new InvalidOperationException($"Budget CSV exceeds the bounded limit of {MaxBatchLines} data rows.");
			var values = records[index];
			var errors = new List<string>();
			var accountId = ParseRequiredGuid(Value(values, columns, "AccountId"), "AccountId", errors);
			var periodId = ParseRequiredGuid(Value(values, columns, "AccountingPeriodId"), "AccountingPeriodId", errors);
			var dimensionId = ParseOptionalGuid(Value(values, columns, "DimensionId"), "DimensionId", errors);
			var dimensionValueId = ParseOptionalGuid(Value(values, columns, "DimensionValueId"), "DimensionValueId", errors);
			if (dimensionId.HasValue != dimensionValueId.HasValue)
				errors.Add("DimensionId and DimensionValueId must be supplied together.");
			var amountText = Value(values, columns, "Amount");
			var amount = decimal.TryParse(amountText, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedAmount)
				? parsedAmount
				: 0m;
			if (!decimal.TryParse(amountText, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
				errors.Add("Amount must be an invariant decimal value.");
			var evidence = Optional(Value(values, columns, "SourceEvidence"), 500);
			result.Add(new FinanceBudgetImportRow(index + 1, accountId, periodId, dimensionId, dimensionValueId, amount, evidence, errors));
		}
		return result;
	}

	private static List<string[]> ReadCsvRecords(string csv)
	{
		var rows = new List<string[]>();
		var row = new List<string>();
		var field = new StringBuilder();
		var quoted = false;
		for (var index = 0; index < csv.Length; index++)
		{
			var character = csv[index];
			if (quoted)
			{
				if (character == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
				{
					field.Append('"');
					index++;
				}
				else if (character == '"') quoted = false;
				else field.Append(character);
				continue;
			}
			if (character == '"') quoted = true;
			else if (character == ',')
			{
				row.Add(field.ToString());
				field.Clear();
			}
			else if (character == '\r' || character == '\n')
			{
				if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
				row.Add(field.ToString());
				field.Clear();
				rows.Add(row.ToArray());
				row.Clear();
			}
			else field.Append(character);
		}
		if (quoted) throw new InvalidOperationException("Budget CSV contains an unterminated quoted field.");
		if (field.Length > 0 || row.Count > 0)
		{
			row.Add(field.ToString());
			rows.Add(row.ToArray());
		}
		return rows;
	}

	private async Task ValidateLineAsync(DatabaseTransactionContext transaction, FinanceBudgetVersion budget, FinanceBudgetLine line, CancellationToken cancellationToken)
	{
		if (!await _budgets.AccountBelongsToBookAsync(transaction, budget.AccountingBookId, line.AccountId, cancellationToken))
			throw new InvalidOperationException("The account does not belong to the selected accounting book.");
		if (!await _budgets.PeriodBelongsToCalendarAsync(transaction, budget.FiscalCalendarId, line.AccountingPeriodId, cancellationToken))
			throw new InvalidOperationException("The accounting period does not belong to the selected fiscal calendar.");
		if (line.DimensionId.HasValue && !await _budgets.DimensionValueMatchesAsync(transaction, line.DimensionId.Value, line.DimensionValueId!.Value, cancellationToken))
			throw new InvalidOperationException("The accounting dimension value is invalid.");
	}

	private async Task<FinanceBudgetBookContext> RequireBookAsync(
		DatabaseTransactionContext transaction,
		Guid accountingBookId,
		Guid legalEntityId,
		CancellationToken cancellationToken)
	{
		var book = await _budgets.GetBookContextAsync(transaction, accountingBookId, cancellationToken)
			?? throw new InvalidOperationException("Accounting book was not found.");
		if (!book.IsActive || book.LegalEntityId != legalEntityId)
			throw new InvalidOperationException("Accounting book is inactive or belongs to another legal entity.");
		return book;
	}

	private async Task<FinanceBudgetVersion> RequireBudgetAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		await _budgets.GetVersionAsync(transaction, id, cancellationToken)
			?? throw new InvalidOperationException("Budget version was not found.");

	private async Task TouchBudgetAsync(DatabaseTransactionContext transaction, FinanceBudgetVersion budget, long userId, CancellationToken cancellationToken)
	{
		var touched = budget with { UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = userId };
		if (await _budgets.UpdateVersionAsync(transaction, touched, budget.Version, cancellationToken) != 1)
			throw new ConcurrencyConflictException("finance budget");
	}

	private async Task UpdateBudgetAsync(DatabaseTransactionContext transaction, FinanceBudgetVersion before, FinanceBudgetVersion after, CancellationToken cancellationToken)
	{
		if (await _budgets.UpdateVersionAsync(transaction, after, before.Version, cancellationToken) != 1)
			throw new ConcurrencyConflictException("finance budget");
	}

	private static FinanceBudgetLine NormalizeLine(FinanceBudgetLine value)
	{
		RequireGuid(value.AccountId, nameof(value.AccountId));
		RequireGuid(value.AccountingPeriodId, nameof(value.AccountingPeriodId));
		ValidateDimensionPair(value.DimensionId, value.DimensionValueId);
		return value with { SourceEvidence = Optional(value.SourceEvidence, 500) };
	}

	private static void RequireDraft(FinanceBudgetVersion value)
	{
		if (value.Status != FinanceBudgetStatus.Draft)
			throw new InvalidOperationException("Only draft budget versions can be edited.");
	}

	private static void RequireVersion(FinanceBudgetVersion value, long expectedVersion)
	{
		if (value.Version != expectedVersion) throw new ConcurrencyConflictException("finance budget");
	}

	private User RequireUser() =>
		_authorization.CurrentUser is { IsActive: true } user
			? user
			: throw new UnauthorizedAccessException("An active authenticated user is required.");

	private static void ValidateFiscalYear(int fiscalYear)
	{
		if (fiscalYear is < 1900 or > 9999) throw new ArgumentOutOfRangeException(nameof(fiscalYear));
	}

	private static void ValidateDimensionPair(Guid? dimensionId, Guid? dimensionValueId)
	{
		if (dimensionId.HasValue != dimensionValueId.HasValue)
			throw new ArgumentException("Dimension and dimension value must be supplied together.");
		if (dimensionId == Guid.Empty || dimensionValueId == Guid.Empty)
			throw new ArgumentException("Dimension identifiers cannot be empty.");
	}

	private static Guid ParseRequiredGuid(string? value, string name, ICollection<string> errors)
	{
		if (Guid.TryParse(value, out var result) && result != Guid.Empty) return result;
		errors.Add($"{name} must be a non-empty GUID.");
		return Guid.Empty;
	}

	private static Guid? ParseOptionalGuid(string? value, string name, ICollection<string> errors)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		if (Guid.TryParse(value, out var result) && result != Guid.Empty) return result;
		errors.Add($"{name} must be a GUID when supplied.");
		return null;
	}

	private static string? Value(string[] row, IReadOnlyDictionary<string, int> columns, string name) =>
		columns.TryGetValue(name, out var index) && index < row.Length ? row[index] : null;

	private static string Required(string? value, int maximumLength)
	{
		var normalized = value?.Trim();
		if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("A value is required.");
		if (normalized.Length > maximumLength) throw new ArgumentException($"Value cannot exceed {maximumLength} characters.");
		return normalized;
	}

	private static string? Optional(string? value, int maximumLength)
	{
		var normalized = value?.Trim();
		if (string.IsNullOrEmpty(normalized)) return null;
		if (normalized.Length > maximumLength) throw new ArgumentException($"Value cannot exceed {maximumLength} characters.");
		return normalized;
	}

	private static void RequirePositive(long value, string name)
	{
		if (value <= 0) throw new ArgumentOutOfRangeException(name);
	}

	private static void RequireGuid(Guid value, string name)
	{
		if (value == Guid.Empty) throw new ArgumentException("A non-empty identifier is required.", name);
	}

	private static string Csv(string? value)
	{
		var text = value ?? string.Empty;
		return text.IndexOfAny([',', '"', '\r', '\n']) < 0
			? text
			: $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
	}
}
