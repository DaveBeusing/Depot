// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ProjectAccountingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ProjectAccountingRepository _projects;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public ProjectAccountingService(
		IDatabaseTransactionRunner transactions,
		ProjectAccountingRepository projects,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
		_projects = projects ?? throw new ArgumentNullException(nameof(projects));
		_auditEntries = auditEntries ?? throw new ArgumentNullException(nameof(auditEntries));
		_audit = audit ?? throw new ArgumentNullException(nameof(audit));
		_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.ProjectsView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.ProjectsManage);
	public bool CanManageAttributions => _authorization.HasPermission(ApplicationPermission.ProjectAttributionsManage);
	public bool CanViewFinancials => _authorization.HasPermission(ApplicationPermission.ProjectFinancialsView);
	public bool CanManageBudgetLinks => _authorization.HasPermission(ApplicationPermission.ProjectsManage) && _authorization.HasPermission(ApplicationPermission.FinanceBudgetingManage);

	public Task<PageResult<ProjectRecord>> SearchAsync(ProjectListFilter filter, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsView);
		return _projects.SearchAsync(filter, pageNumber, pageSize, cancellationToken);
	}

	public async Task<ProjectRecord> GetAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsView);
		RequirePositive(id, nameof(id));
		return await _projects.GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("Project was not found.");
	}

	public Task<IReadOnlyList<ProjectPhase>> ListPhasesAsync(long projectId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsView);
		RequirePositive(projectId, nameof(projectId));
		return _projects.ListPhasesAsync(projectId, cancellationToken);
	}

	public Task<IReadOnlyList<ProjectAttribution>> ListAttributionsAsync(long projectId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsView);
		RequirePositive(projectId, nameof(projectId));
		return _projects.ListAttributionsAsync(projectId, cancellationToken);
	}

	public Task<IReadOnlyList<ProjectBudgetLink>> ListBudgetLinksAsync(long projectId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectFinancialsView);
		RequirePositive(projectId, nameof(projectId));
		return _projects.ListBudgetLinksAsync(projectId, cancellationToken);
	}

	public async Task<ProjectRecord> SaveAsync(ProjectRecord value, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsManage);
		var user = RequireUser();
		var normalized = NormalizeProject(value);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _projects.LegalEntityExistsAsync(transaction, normalized.LegalEntityId, token))
				throw new InvalidOperationException("Project legal entity was not found or is inactive.");
			if (!await _projects.ActiveUserExistsAsync(transaction, normalized.OwnerUserId, token))
				throw new InvalidOperationException("Project owner was not found or is inactive.");
			if (normalized.CustomerId is { } customerId && !await _projects.CustomerExistsAsync(transaction, customerId, token))
				throw new InvalidOperationException("Project customer was not found or is inactive.");

			if (normalized.Id == 0)
			{
				var now = DateTime.UtcNow;
				var created = normalized with
				{
					Version = 1,
					Status = ProjectStatus.Draft,
					CreatedAtUtc = now,
					CreatedByUserId = user.Id,
					UpdatedAtUtc = now,
					UpdatedByUserId = user.Id,
					ClosedAtUtc = null,
					ClosedByUserId = null,
					CancelledAtUtc = null,
					CancelledByUserId = null
				};
				created = await _projects.CreateProjectAsync(transaction, created, token);
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
				return created;
			}

			var before = await RequireProjectAsync(transaction, normalized.Id, token);
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("project");
			if (before.LegalEntityId != normalized.LegalEntityId)
				throw new InvalidOperationException("Project legal entity cannot be changed after creation.");
			if (before.Status is ProjectStatus.Closed or ProjectStatus.Cancelled)
				throw new InvalidOperationException("Closed or cancelled projects cannot be edited.");
			var after = normalized with
			{
				Status = before.Status,
				CreatedAtUtc = before.CreatedAtUtc,
				CreatedByUserId = before.CreatedByUserId,
				UpdatedAtUtc = DateTime.UtcNow,
				UpdatedByUserId = user.Id,
				ClosedAtUtc = before.ClosedAtUtc,
				ClosedByUserId = before.ClosedByUserId,
				CancelledAtUtc = before.CancelledAtUtc,
				CancelledByUserId = before.CancelledByUserId
			};
			if (await _projects.UpdateProjectAsync(transaction, after, before.Version, token) != 1)
				throw new ConcurrencyConflictException("project");
			var persisted = after with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(persisted.Id, before, persisted), token);
			return persisted;
		}, cancellationToken);
	}

	public Task<ProjectRecord> ActivateAsync(long projectId, long expectedVersion, CancellationToken cancellationToken = default) =>
		TransitionAsync(projectId, expectedVersion, ProjectStatus.Active, cancellationToken);
	public Task<ProjectRecord> PutOnHoldAsync(long projectId, long expectedVersion, CancellationToken cancellationToken = default) =>
		TransitionAsync(projectId, expectedVersion, ProjectStatus.OnHold, cancellationToken);
	public Task<ProjectRecord> CloseAsync(long projectId, long expectedVersion, CancellationToken cancellationToken = default) =>
		TransitionAsync(projectId, expectedVersion, ProjectStatus.Closed, cancellationToken);
	public Task<ProjectRecord> CancelAsync(long projectId, long expectedVersion, CancellationToken cancellationToken = default) =>
		TransitionAsync(projectId, expectedVersion, ProjectStatus.Cancelled, cancellationToken);

	public async Task<ProjectPhase> SavePhaseAsync(ProjectPhase value, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsManage);
		RequirePositive(value.ProjectId, nameof(value.ProjectId));
		var normalized = NormalizePhase(value);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var project = await RequireProjectAsync(transaction, normalized.ProjectId, token);
			RequireOperationalProject(project);
			if (normalized.Id == 0)
			{
				var created = await _projects.CreatePhaseAsync(transaction, normalized with { Version = 1, Status = ProjectPhaseStatus.Planned }, token);
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
				return created;
			}
			var before = await _projects.GetPhaseAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Project phase was not found.");
			if (before.ProjectId != project.Id) throw new InvalidOperationException("Project phase belongs to another project.");
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("project phase");
			if (before.Status is ProjectPhaseStatus.Completed or ProjectPhaseStatus.Cancelled)
				throw new InvalidOperationException("Completed or cancelled project phases cannot be edited.");
			var updated = normalized with { Status = before.Status };
			if (await _projects.UpdatePhaseAsync(transaction, updated, before.Version, token) != 1)
				throw new ConcurrencyConflictException("project phase");
			var persisted = updated with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(persisted.Id, before, persisted), token);
			return persisted;
		}, cancellationToken);
	}

	public Task<ProjectPhase> ActivatePhaseAsync(long phaseId, long expectedVersion, CancellationToken cancellationToken = default) =>
		TransitionPhaseAsync(phaseId, expectedVersion, ProjectPhaseStatus.Active, cancellationToken);

	public Task<ProjectPhase> CompletePhaseAsync(long phaseId, long expectedVersion, CancellationToken cancellationToken = default) =>
		TransitionPhaseAsync(phaseId, expectedVersion, ProjectPhaseStatus.Completed, cancellationToken);

	public Task<ProjectPhase> CancelPhaseAsync(long phaseId, long expectedVersion, CancellationToken cancellationToken = default) =>
		TransitionPhaseAsync(phaseId, expectedVersion, ProjectPhaseStatus.Cancelled, cancellationToken);

	public async Task<ProjectAttribution> AttributeAsync(ProjectAttributionRequest request, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectAttributionsManage);
		RequireSourcePermission(request.EntityKind);
		var user = RequireUser();
		RequirePositive(request.ProjectId, nameof(request.ProjectId));
		RequirePositive(request.EntityId, nameof(request.EntityId));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var project = await RequireProjectAsync(transaction, request.ProjectId, token);
			RequireOperationalProject(project);
			await ValidatePhaseAsync(transaction, project.Id, request.ProjectPhaseId, token);
			var source = await _projects.ResolveSourceAsync(transaction, request.EntityKind, request.EntityId, token)
				?? throw new InvalidOperationException("The source document or journal entry was not found.");
			if (source.Kind == ProjectAttributionEntityKind.JournalEntry && source.JournalEntryKind != FinanceJournalEntryKind.Manual)
				throw new InvalidOperationException("Direct project journal attribution is limited to controlled manual journal entries.");
			if (source.LegalEntityId is { } sourceLegalEntityId && sourceLegalEntityId != project.LegalEntityId)
				throw new InvalidOperationException("The posted source belongs to another legal entity.");

			var existing = await _projects.GetAttributionAsync(transaction, request.EntityKind, request.EntityId, token);
			if (existing is not null)
			{
				if (existing.ProjectId == request.ProjectId && existing.ProjectPhaseId == request.ProjectPhaseId) return existing;
				if (existing.IsImmutable || source.IsImmutable)
					throw new InvalidOperationException("The source attribution is immutable because authoritative operational or accounting evidence already exists.");
				var changed = existing with
				{
					ProjectId = request.ProjectId,
					ProjectPhaseId = request.ProjectPhaseId,
					SourceType = source.SourceType,
					SourceId = source.SourceId,
					JournalEntryId = source.JournalEntryId,
					IsImmutable = source.IsImmutable
				};
				if (await _projects.UpdateAttributionAsync(transaction, changed, existing.Version, token) != 1)
					throw new ConcurrencyConflictException("project attribution");
				var persisted = changed with { Version = existing.Version + 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(existing.Id, "Reattributed", existing, persisted), token);
				return persisted;
			}

			var created = new ProjectAttribution
			{
				ProjectId = request.ProjectId,
				ProjectPhaseId = request.ProjectPhaseId,
				EntityKind = request.EntityKind,
				EntityId = request.EntityId,
				SourceType = source.SourceType,
				SourceId = source.SourceId,
				JournalEntryId = source.JournalEntryId,
				IsImmutable = source.IsImmutable,
				CreatedAtUtc = DateTime.UtcNow,
				CreatedByUserId = user.Id
			};
			created = await _projects.CreateAttributionAsync(transaction, created, token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(created.Id, "Attributed", null, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task RemoveAttributionAsync(ProjectAttributionEntityKind kind, long entityId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectAttributionsManage);
		RequireSourcePermission(kind);
		RequirePositive(entityId, nameof(entityId));
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _projects.GetAttributionAsync(transaction, kind, entityId, token) ?? throw new InvalidOperationException("Project attribution was not found.");
			var project = await RequireProjectAsync(transaction, existing.ProjectId, token);
			RequireOperationalProject(project);
			var source = await _projects.ResolveSourceAsync(transaction, kind, entityId, token);
			if (existing.IsImmutable || source?.IsImmutable == true)
				throw new InvalidOperationException("Immutable project attribution cannot be removed.");
			if (await _projects.DeleteAttributionAsync(transaction, existing.Id, existing.Version, token) != 1)
				throw new ConcurrencyConflictException("project attribution");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(existing.Id, "AttributionRemoved", existing, null), token);
			return 0;
		}, cancellationToken);
	}

	public Task<IReadOnlyList<ProjectBudgetLineOption>> ListBudgetLineOptionsAsync(long projectId, int limit = 200, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectFinancialsView);
		return ListBudgetLineOptionsCoreAsync(projectId, limit, cancellationToken);
	}

	private async Task<IReadOnlyList<ProjectBudgetLineOption>> ListBudgetLineOptionsCoreAsync(long projectId, int limit, CancellationToken cancellationToken)
	{
		var project = await GetAsync(projectId, cancellationToken);
		return await _projects.ListBudgetLineOptionsAsync(project.LegalEntityId, limit, cancellationToken);
	}

	public async Task<ProjectBudgetLink> LinkBudgetLineAsync(long projectId, long? projectPhaseId, long financeBudgetLineId, string? categoryCode, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsManage);
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		var user = RequireUser();
		RequirePositive(projectId, nameof(projectId));
		RequirePositive(financeBudgetLineId, nameof(financeBudgetLineId));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var project = await RequireProjectAsync(transaction, projectId, token);
			RequireOperationalProject(project);
			await ValidatePhaseAsync(transaction, project.Id, projectPhaseId, token);
			var line = await _projects.GetBudgetLineContextAsync(transaction, financeBudgetLineId, token)
				?? throw new InvalidOperationException("Finance budget line was not found.");
			if (line.LegalEntityId != project.LegalEntityId)
				throw new InvalidOperationException("Finance budget line belongs to another legal entity.");
			var existing = await _projects.GetBudgetLinkAsync(transaction, financeBudgetLineId, token);
			if (existing is not null)
			{
				if (existing.ProjectId == projectId && existing.ProjectPhaseId == projectPhaseId) return existing;
				throw new InvalidOperationException("Finance budget line is already linked to another project or phase.");
			}
			var created = new ProjectBudgetLink
			{
				ProjectId = projectId,
				ProjectPhaseId = projectPhaseId,
				FinanceBudgetLineId = financeBudgetLineId,
				CategoryCode = Optional(categoryCode, 100)?.ToUpperInvariant(),
				CreatedAtUtc = DateTime.UtcNow,
				CreatedByUserId = user.Id
			};
			created = await _projects.CreateBudgetLinkAsync(transaction, created, token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(created.Id, "BudgetLineLinked", null, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task UnlinkBudgetLineAsync(long financeBudgetLineId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsManage);
		_authorization.RequirePermission(ApplicationPermission.FinanceBudgetingManage);
		RequirePositive(financeBudgetLineId, nameof(financeBudgetLineId));
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _projects.GetBudgetLinkAsync(transaction, financeBudgetLineId, token) ?? throw new InvalidOperationException("Project budget link was not found.");
			var project = await RequireProjectAsync(transaction, existing.ProjectId, token);
			RequireOperationalProject(project);
			if (await _projects.DeleteBudgetLinkAsync(transaction, existing.Id, token) != 1)
				throw new ConcurrencyConflictException("project budget link");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(existing.Id, "BudgetLineUnlinked", existing, null), token);
			return 0;
		}, cancellationToken);
	}

	public Task<IReadOnlyList<ProjectActualRow>> ListActualsAsync(long projectId, long? projectPhaseId = null, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectFinancialsView);
		ValidateRange(fromDate, toDate);
		return _projects.ListActualsAsync(projectId, projectPhaseId, fromDate, toDate, cancellationToken);
	}

	public Task<IReadOnlyList<ProjectCommitmentRow>> ListCommitmentsAsync(long projectId, long? projectPhaseId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectFinancialsView);
		return _projects.ListCommitmentsAsync(projectId, projectPhaseId, cancellationToken);
	}

	public async Task<IReadOnlyList<ProjectBudgetVarianceRow>> GetBudgetVarianceAsync(long projectId, long? projectPhaseId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectFinancialsView);
		var actuals = await _projects.ListActualsAsync(projectId, projectPhaseId, null, null, cancellationToken);
		var budgets = await _projects.ListBudgetAggregatesAsync(projectId, projectPhaseId, cancellationToken);
		var actualByKey = actuals
			.GroupBy(value => (value.AccountingBookId, value.AccountingPeriodId, value.AccountId, value.ProjectPhaseId))
			.ToDictionary(group => group.Key, group => group.Sum(value => value.Amount));
		var budgetByKey = budgets
			.GroupBy(value => (value.AccountingBookId, value.AccountingPeriodId, value.AccountId, value.ProjectPhaseId))
			.ToDictionary(group => group.Key, group => (
				Amount: group.Sum(value => value.Budget),
				PeriodCode: group.First().PeriodCode,
				AccountNumber: group.First().AccountNumber,
				AccountName: group.First().AccountName,
				Category: group.Select(value => value.CategoryCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? group.First().CategoryCode : null));
		var keys = actualByKey.Keys.Union(budgetByKey.Keys)
			.OrderBy(value => value.AccountingBookId)
			.ThenBy(value => value.AccountingPeriodId)
			.ThenBy(value => value.AccountId)
			.ThenBy(value => value.ProjectPhaseId)
			.ToArray();
		var rows = new List<ProjectBudgetVarianceRow>(keys.Length);
		foreach (var key in keys)
		{
			var budget = budgetByKey.GetValueOrDefault(key);
			var actualMeta = actuals.FirstOrDefault(value => value.AccountingBookId == key.AccountingBookId && value.AccountingPeriodId == key.AccountingPeriodId && value.AccountId == key.AccountId && value.ProjectPhaseId == key.ProjectPhaseId);
			rows.Add(new ProjectBudgetVarianceRow(
				key.AccountingBookId,
				key.AccountingPeriodId,
				budget.PeriodCode ?? key.AccountingPeriodId.ToString("D"),
				key.AccountId,
				budget.AccountNumber ?? actualMeta?.AccountNumber ?? string.Empty,
				budget.AccountName ?? actualMeta?.AccountName ?? string.Empty,
				key.ProjectPhaseId,
				budget.Category,
				actualByKey.GetValueOrDefault(key),
				budget.Amount));
		}
		return rows;
	}

	public async Task<ProjectFinancialSummary> GetFinancialSummaryAsync(long projectId, long? projectPhaseId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectFinancialsView);
		var actuals = await _projects.ListActualsAsync(projectId, projectPhaseId, null, null, cancellationToken);
		var commitments = await _projects.ListCommitmentsAsync(projectId, projectPhaseId, cancellationToken);
		var totals = actuals
			.GroupBy(value => value.ReportingCurrency)
			.Select(group => new ProjectCurrencyTotals(
				group.Key,
				group.Where(value => value.AccountType == FinanceAccountType.Revenue).Sum(value => value.Amount),
				group.Where(value => value.AccountType == FinanceAccountType.Expense).Sum(value => value.Amount)))
			.OrderBy(value => value.Currency.Value, StringComparer.Ordinal)
			.ToArray();
		return new ProjectFinancialSummary(projectId, totals, commitments.Sum(value => value.RemainingAmount), actuals.Select(value => value.JournalEntryId).Distinct().Count(), commitments.Count);
	}

	public async Task<IReadOnlyList<ProjectRecord>> GetOwnedWorkAsync(long ownerUserId, int limit, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsView);
		var page = await _projects.SearchAsync(new ProjectListFilter(OwnerUserId: ownerUserId), 1, Math.Clamp(limit, 1, 100), cancellationToken);
		return page.Items.Where(value => value.Status is ProjectStatus.Draft or ProjectStatus.Active or ProjectStatus.OnHold).ToArray();
	}

	private async Task<ProjectPhase> TransitionPhaseAsync(long phaseId, long expectedVersion, ProjectPhaseStatus target, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsManage);
		RequirePositive(phaseId, nameof(phaseId));
		RequirePositive(expectedVersion, nameof(expectedVersion));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _projects.GetPhaseAsync(transaction, phaseId, token) ?? throw new InvalidOperationException("Project phase was not found.");
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("project phase");
			var project = await RequireProjectAsync(transaction, before.ProjectId, token);
			RequireOperationalProject(project);
			if (!CanTransitionPhase(before.Status, target))
				throw new InvalidOperationException($"Project phase status '{before.Status}' cannot transition to '{target}'.");
			var after = before with { Status = target };
			if (await _projects.UpdatePhaseAsync(transaction, after, before.Version, token) != 1)
				throw new ConcurrencyConflictException("project phase");
			var persisted = after with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(phaseId, target.ToString(), before, persisted), token);
			return persisted;
		}, cancellationToken);
	}

	private async Task<ProjectRecord> TransitionAsync(long projectId, long expectedVersion, ProjectStatus target, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.ProjectsManage);
		var user = RequireUser();
		RequirePositive(projectId, nameof(projectId));
		RequirePositive(expectedVersion, nameof(expectedVersion));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await RequireProjectAsync(transaction, projectId, token);
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("project");
			if (!CanTransition(before.Status, target))
				throw new InvalidOperationException($"Project status '{before.Status}' cannot transition to '{target}'.");
			var now = DateTime.UtcNow;
			var after = before with
			{
				Status = target,
				UpdatedAtUtc = now,
				UpdatedByUserId = user.Id,
				ClosedAtUtc = target == ProjectStatus.Closed ? now : before.ClosedAtUtc,
				ClosedByUserId = target == ProjectStatus.Closed ? user.Id : before.ClosedByUserId,
				CancelledAtUtc = target == ProjectStatus.Cancelled ? now : before.CancelledAtUtc,
				CancelledByUserId = target == ProjectStatus.Cancelled ? user.Id : before.CancelledByUserId
			};
			if (await _projects.UpdateProjectAsync(transaction, after, before.Version, token) != 1)
				throw new ConcurrencyConflictException("project");
			var persisted = after with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(projectId, target.ToString(), before, persisted), token);
			return persisted;
		}, cancellationToken);
	}

	private async Task<ProjectRecord> RequireProjectAsync(DatabaseTransactionContext transaction, long projectId, CancellationToken cancellationToken) =>
		await _projects.GetAsync(transaction, projectId, cancellationToken) ?? throw new InvalidOperationException("Project was not found.");

	private async Task ValidatePhaseAsync(DatabaseTransactionContext transaction, long projectId, long? phaseId, CancellationToken cancellationToken)
	{
		if (!phaseId.HasValue) return;
		var phase = await _projects.GetPhaseAsync(transaction, phaseId.Value, cancellationToken) ?? throw new InvalidOperationException("Project phase was not found.");
		if (phase.ProjectId != projectId) throw new InvalidOperationException("Project phase belongs to another project.");
		if (phase.Status is ProjectPhaseStatus.Completed or ProjectPhaseStatus.Cancelled)
			throw new InvalidOperationException("Completed or cancelled project phases cannot receive new activity.");
	}

	private void RequireSourcePermission(ProjectAttributionEntityKind kind)
	{
		var permission = kind switch
		{
			ProjectAttributionEntityKind.PurchaseOrder => ApplicationPermission.PurchaseOrdersEdit,
			ProjectAttributionEntityKind.SupplierDocument => ApplicationPermission.FinanceSupplierInvoicesCreate,
			ProjectAttributionEntityKind.SalesOrder => ApplicationPermission.SalesOrdersEdit,
			ProjectAttributionEntityKind.SalesInvoice => ApplicationPermission.SalesInvoicesCreate,
			ProjectAttributionEntityKind.JournalEntry => ApplicationPermission.FinanceManualJournalsPost,
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};
		_authorization.RequirePermission(permission);
	}

	private User RequireUser() =>
		_authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active authenticated user is required.");

	private static ProjectRecord NormalizeProject(ProjectRecord value)
	{
		ArgumentNullException.ThrowIfNull(value);
		var code = Required(value.Code, 50).ToUpperInvariant();
		var name = Required(value.Name, 200);
		var description = Optional(value.Description, 2000);
		if (value.LegalEntityId == Guid.Empty) throw new ArgumentException("Project legal entity is required.", nameof(value));
		RequirePositive(value.OwnerUserId, nameof(value.OwnerUserId));
		if (value.CustomerId is <= 0) throw new ArgumentOutOfRangeException(nameof(value.CustomerId));
		ValidateRange(value.PlannedStartDate, value.PlannedEndDate);
		return value with { Code = code, Name = name, Description = description };
	}

	private static ProjectPhase NormalizePhase(ProjectPhase value)
	{
		ArgumentNullException.ThrowIfNull(value);
		var code = Required(value.Code, 50).ToUpperInvariant();
		var name = Required(value.Name, 200);
		ValidateRange(value.PlannedStartDate, value.PlannedEndDate);
		return value with { Code = code, Name = name, Description = Optional(value.Description, 2000) };
	}

	private static void RequireOperationalProject(ProjectRecord project)
	{
		if (!project.AcceptsOperationalActivity)
			throw new InvalidOperationException("Closed or cancelled projects cannot receive new mutable operational activity.");
	}

	private static bool CanTransition(ProjectStatus current, ProjectStatus target) => (current, target) switch
	{
		(ProjectStatus.Draft, ProjectStatus.Active or ProjectStatus.Cancelled) => true,
		(ProjectStatus.Active, ProjectStatus.OnHold or ProjectStatus.Closed or ProjectStatus.Cancelled) => true,
		(ProjectStatus.OnHold, ProjectStatus.Active or ProjectStatus.Closed or ProjectStatus.Cancelled) => true,
		_ => false
	};

	private static bool CanTransitionPhase(ProjectPhaseStatus current, ProjectPhaseStatus target) => (current, target) switch
	{
		(ProjectPhaseStatus.Planned, ProjectPhaseStatus.Active or ProjectPhaseStatus.Cancelled) => true,
		(ProjectPhaseStatus.Active, ProjectPhaseStatus.Completed or ProjectPhaseStatus.Cancelled) => true,
		_ => false
	};

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
		if (string.IsNullOrWhiteSpace(normalized)) return null;
		if (normalized.Length > maximumLength) throw new ArgumentException($"Value cannot exceed {maximumLength} characters.");
		return normalized;
	}

	private static void RequirePositive(long value, string name)
	{
		if (value <= 0) throw new ArgumentOutOfRangeException(name);
	}

	private static void ValidateRange(DateOnly? from, DateOnly? to)
	{
		if (from.HasValue && to.HasValue && to.Value < from.Value) throw new ArgumentException("End date must be on or after start date.");
	}
}
