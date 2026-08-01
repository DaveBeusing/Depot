// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class MaterialIssueService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly MaterialIssueRepository _issues;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _movements;
	private readonly ReasonCodeRepository _reasonCodes;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly StockMovementReversalService _reversals;
	private readonly AuthorizationService _authorization;

	public MaterialIssueService(IDatabaseTransactionRunner transactions, MaterialIssueRepository issues, InventoryRepository inventories, StockMovementRepository movements, ReasonCodeRepository reasonCodes, AuditRepository auditEntries, AuditService audit, StockMovementReversalService reversals, AuthorizationService authorization)
	{
		_transactions = transactions;
		_issues = issues;
		_inventories = inventories;
		_movements = movements;
		_reasonCodes = reasonCodes;
		_auditEntries = auditEntries;
		_audit = audit;
		_reversals = reversals;
		_authorization = authorization;
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.MaterialIssuesCreate);
	public bool CanPost => _authorization.HasPermission(ApplicationPermission.MaterialIssuesPost);
	public bool CanReverse => _authorization.HasPermission(ApplicationPermission.MaterialIssuesReverse);

	public Task<PageResult<MaterialIssueOverviewItem>> SearchAsync(string? searchText, MaterialIssueStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => _issues.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken);
	public Task<MaterialIssue?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => _issues.GetByIdAsync(id, cancellationToken);
	public Task<MaterialIssueOverviewItem?> GetOverviewByIdAsync(long id, CancellationToken cancellationToken = default) => _issues.GetOverviewByIdAsync(id, cancellationToken);
	public Task<PageResult<InventoryOverviewItem>> SearchInventoryOptionsAsync(string? searchText, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default) => _inventories.SearchOverviewPageAsync(searchText, pageNumber, pageSize, cancellationToken);

	public async Task<IReadOnlyList<MovementOverviewItem>> GetMovementsAsync(long id, CancellationToken cancellationToken = default)
	{
		var issue = await _issues.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("The material issue was not found.");
		return await _movements.ListByReferenceAsync(Reference(issue.IssueNumber), cancellationToken);
	}

	public Task<MaterialIssue> SaveDraftAsync(MaterialIssue issue, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.MaterialIssuesCreate);
		NormalizeAndValidate(issue);
		var userId = RequireUser("save");
		return _transactions.ExecuteAsync((transaction, token) => SaveDraftAsync(transaction, issue, userId, token), cancellationToken);
	}

	public Task<MaterialIssue> PostAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.MaterialIssuesPost);
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		var userId = RequireUser("post");
		return _transactions.ExecuteAsync((transaction, token) => PostAsync(transaction, id, version, userId, token), cancellationToken);
	}

	public Task<MaterialIssue> CancelAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.MaterialIssuesCreate);
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		RequireUser("cancel");
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _issues.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("The material issue was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("material issue");
			if (before.Status != MaterialIssueStatus.Draft) throw new InvalidOperationException("Only a draft material issue can be cancelled.");
			if (!await _issues.CancelDraftAsync(transaction, id, version, token)) throw new ConcurrencyConflictException("material issue");
			var after = Copy(before); after.Status = MaterialIssueStatus.Cancelled; after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public Task<MaterialIssue> ReverseAsync(long id, long version, long reasonCodeId, string reversalReason, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.MaterialIssuesReverse);
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		var userId = _reversals.RequireUser();
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _issues.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("The material issue was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("material issue");
			if (before.Status != MaterialIssueStatus.Posted) throw new InvalidOperationException("Only a posted material issue can be reversed.");
			var originals = await _movements.ListOriginalsByReferenceAsync(transaction, Reference(before.IssueNumber), token);
			if (originals.Count != before.Lines.Count || originals.Any(movement => movement.MovementType != StockMovementType.Withdrawal)) throw new InvalidOperationException("The material-issue movements are incomplete or inconsistent.");
			await _reversals.CreateReversalsAsync(transaction, originals, reasonCodeId, reversalReason, userId, token);
			var reversedAtUtc = DateTime.UtcNow;
			var normalizedReason = reversalReason.Trim();
			if (!await _issues.MarkReversedAsync(transaction, id, version, userId, reversedAtUtc, normalizedReason, token)) throw new ConcurrencyConflictException("material issue");
			var after = Copy(before); after.Status = MaterialIssueStatus.Reversed; after.ReversedByUserId = userId; after.ReversedAtUtc = reversedAtUtc; after.ReversalReason = normalizedReason; after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
	}

	private async Task<MaterialIssue> SaveDraftAsync(DatabaseTransactionContext transaction, MaterialIssue issue, long userId, CancellationToken cancellationToken)
	{
		MaterialIssue? before = null;
		if (issue.Id == 0)
		{
			issue.CreatedByUserId = userId;
			issue.IssueNumber = $"PENDING-{Guid.NewGuid():N}";
			issue.Id = await _issues.CreateAsync(transaction, issue, cancellationToken);
			issue.IssueNumber = $"MI-{issue.Id:000000}";
			if (await _issues.UpdateIssueNumberAsync(transaction, issue.Id, issue.IssueNumber, cancellationToken) != 1) throw new ConcurrencyConflictException("material issue number");
		}
		else
		{
			before = await _issues.GetByIdAsync(transaction, issue.Id, cancellationToken) ?? throw new InvalidOperationException("The material issue was not found.");
			if (before.Version != issue.Version) throw new ConcurrencyConflictException("material issue");
			if (before.Status != MaterialIssueStatus.Draft) throw new InvalidOperationException("Only draft material issues can be edited.");
			issue.IssueNumber = before.IssueNumber; issue.CreatedByUserId = before.CreatedByUserId;
			if (!await _issues.UpdateDraftAsync(transaction, issue, cancellationToken)) throw new ConcurrencyConflictException("material issue");
			issue.Version++;
		}

		await ValidateReferencesAsync(transaction, issue, cancellationToken);
		var existingIds = before?.Lines.Select(line => line.Id).ToHashSet() ?? [];
		var suppliedIds = issue.Lines.Where(line => line.Id > 0).Select(line => line.Id).ToArray();
		if (suppliedIds.Distinct().Count() != suppliedIds.Length || suppliedIds.Any(id => !existingIds.Contains(id))) throw new InvalidOperationException("A material-issue line does not belong to this issue.");
		await _issues.DeleteLinesAsync(transaction, issue.Id, existingIds.Except(suppliedIds).OrderBy(id => id).ToArray(), cancellationToken);
		var lineNumber = 1;
		foreach (var line in issue.Lines)
		{
			line.MaterialIssueId = issue.Id; line.LineNumber = lineNumber++;
			if (line.Id == 0) line.Id = await _issues.CreateLineAsync(transaction, line, cancellationToken);
			else { if (!await _issues.UpdateLineAsync(transaction, line, cancellationToken)) throw new ConcurrencyConflictException("material issue line"); line.Version++; }
		}
		await _auditEntries.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(issue.Id, issue) : _audit.CreateUpdatedEntry(issue.Id, before, issue), cancellationToken);
		return issue;
	}

	private async Task<MaterialIssue> PostAsync(DatabaseTransactionContext transaction, long id, long version, long userId, CancellationToken cancellationToken)
	{
		var before = await _issues.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("The material issue was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("material issue");
		if (before.Status != MaterialIssueStatus.Draft) throw new InvalidOperationException("Only a draft material issue can be posted.");
		NormalizeAndValidate(before);
		await ValidateReferencesAsync(transaction, before, cancellationToken);
		var inventoryIds = before.Lines.Select(line => line.InventoryId).Distinct().OrderBy(id => id).ToArray();
		var current = (await _movements.GetCurrentQuantitiesAsync(transaction, inventoryIds, cancellationToken)).ToDictionary(value => value.InventoryId, value => value.Quantity);
		if (before.Lines.Any(line => current.GetValueOrDefault(line.InventoryId) < line.Quantity)) throw new InsufficientStockException();
		var postedAtUtc = DateTime.UtcNow;
		foreach (var line in before.Lines.OrderBy(line => line.LineNumber))
		{
			var movement = new StockMovement { InventoryId = line.InventoryId, ReasonCodeId = line.ReasonCodeId, MovementType = StockMovementType.Withdrawal, TimestampUtc = postedAtUtc, Quantity = -line.Quantity, Reference = Reference(before.IssueNumber), Notes = line.Notes };
			movement.Id = await _movements.CreateAsync(transaction, movement, cancellationToken);
		}
		if (!await _issues.SetPostedAsync(transaction, id, version, userId, postedAtUtc, cancellationToken)) throw new ConcurrencyConflictException("material issue");
		var after = Copy(before); after.Status = MaterialIssueStatus.Posted; after.PostedByUserId = userId; after.PostedAtUtc = postedAtUtc; after.Version++;
		await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), cancellationToken);
		return after;
	}

	private async Task ValidateReferencesAsync(DatabaseTransactionContext transaction, MaterialIssue issue, CancellationToken cancellationToken)
	{
		var inventories = await _inventories.GetByIdsForUpdateAsync(transaction, issue.Lines.Select(line => line.InventoryId), cancellationToken);
		if (inventories.Count != issue.Lines.Select(line => line.InventoryId).Distinct().Count() || inventories.Any(inventory => !inventory.IsActive)) throw new InvalidOperationException("Every material-issue inventory must exist and be active.");
		var reasons = await _reasonCodes.GetByIdsAsync(transaction, issue.Lines.Select(line => line.ReasonCodeId), cancellationToken);
		if (reasons.Count != issue.Lines.Select(line => line.ReasonCodeId).Distinct().Count() || reasons.Any(reason => !reason.IsActive)) throw new InvalidOperationException("Every material-issue reason code must exist and be active.");
	}

	private static void NormalizeAndValidate(MaterialIssue issue)
	{
		if (issue.Status != MaterialIssueStatus.Draft) throw new InvalidOperationException("Only draft material issues can be saved or posted.");
		issue.Recipient = issue.Recipient?.Trim() ?? string.Empty; issue.Reference = Normalize(issue.Reference); issue.Notes = Normalize(issue.Notes);
		if (issue.Recipient.Length == 0) throw new ArgumentException("A recipient is required.");
		if (issue.Recipient.Length > 250 || issue.Reference?.Length > 250 || issue.Notes?.Length > 4000) throw new ArgumentException("Material-issue text exceeds its maximum length.");
		if (issue.Lines.Count == 0) throw new InvalidOperationException("A material issue requires at least one line.");
		if (issue.Lines.Any(line => line.InventoryId <= 0 || line.ReasonCodeId <= 0)) throw new InvalidOperationException("Every line requires an inventory and reason code.");
		if (issue.Lines.Any(line => line.Quantity <= 0)) throw new ArgumentOutOfRangeException(nameof(issue), "Every issue quantity must be greater than zero.");
		if (issue.Lines.Select(line => line.InventoryId).Distinct().Count() != issue.Lines.Count) throw new InvalidOperationException("An inventory can only occur once per material issue.");
		foreach (var line in issue.Lines) { line.Notes = Normalize(line.Notes); if (line.Notes?.Length > 2000) throw new ArgumentException("Line notes must not exceed 2000 characters."); }
	}

	private long RequireUser(string operation) => _audit.CurrentUserId ?? throw new InvalidOperationException($"A signed-in user is required to {operation} a material issue.");
	private static string Reference(string issueNumber) => $"Material Issue {issueNumber}";
	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	private static MaterialIssue Copy(MaterialIssue source) => new() { Id = source.Id, IssueNumber = source.IssueNumber, IssueDate = source.IssueDate, Status = source.Status, Recipient = source.Recipient, Reference = source.Reference, Notes = source.Notes, CreatedByUserId = source.CreatedByUserId, PostedByUserId = source.PostedByUserId, PostedAtUtc = source.PostedAtUtc, ReversedByUserId = source.ReversedByUserId, ReversedAtUtc = source.ReversedAtUtc, ReversalReason = source.ReversalReason, Version = source.Version, Lines = source.Lines };
}
