// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class MaterialReturnService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly MaterialReturnRepository _returns;
	private readonly MaterialIssueRepository _issues;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _movements;
	private readonly ReasonCodeRepository _reasonCodes;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly StockMovementReversalService _reversals;

	public MaterialReturnService(IDatabaseTransactionRunner transactions, MaterialReturnRepository returns, MaterialIssueRepository issues, InventoryRepository inventories, StockMovementRepository movements, ReasonCodeRepository reasonCodes, AuditRepository auditEntries, AuditService audit, StockMovementReversalService reversals)
	{
		_transactions = transactions; _returns = returns; _issues = issues; _inventories = inventories; _movements = movements; _reasonCodes = reasonCodes; _auditEntries = auditEntries; _audit = audit; _reversals = reversals;
	}

	public Task<PageResult<MaterialReturnOverviewItem>> SearchAsync(string? searchText, MaterialReturnStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => _returns.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken);
	public Task<MaterialReturn?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => _returns.GetByIdAsync(id, cancellationToken);
	public Task<MaterialReturnOverviewItem?> GetOverviewByIdAsync(long id, CancellationToken cancellationToken = default) => _returns.GetOverviewByIdAsync(id, cancellationToken);
	public Task<PageResult<InventoryOverviewItem>> SearchInventoryOptionsAsync(string? searchText, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default) => _inventories.SearchOverviewPageAsync(searchText, pageNumber, pageSize, cancellationToken);
	public Task<PageResult<MaterialIssueOverviewItem>> SearchOriginalIssuesAsync(string? searchText, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default) => _issues.SearchAsync(searchText, MaterialIssueStatus.Posted, pageNumber, pageSize, cancellationToken);

	public async Task<IReadOnlyList<MovementOverviewItem>> GetMovementsAsync(long id, CancellationToken cancellationToken = default)
	{
		var value = await _returns.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("The material return was not found.");
		return await _movements.ListByReferenceAsync(DocumentReference(value.ReturnNumber), cancellationToken);
	}

	public Task<MaterialReturn> SaveDraftAsync(MaterialReturn value, CancellationToken cancellationToken = default)
	{
		NormalizeAndValidate(value); var userId = RequireUser("save");
		return _transactions.ExecuteAsync((transaction, token) => SaveDraftAsync(transaction, value, userId, token), cancellationToken);
	}

	public Task<MaterialReturn> PostAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id)); var userId = RequireUser("post");
		return _transactions.ExecuteAsync((transaction, token) => PostAsync(transaction, id, version, userId, token), cancellationToken);
	}

	public Task<MaterialReturn> CancelAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id)); RequireUser("cancel");
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _returns.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("The material return was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("material return");
			if (before.Status != MaterialReturnStatus.Draft) throw new InvalidOperationException("Only a draft material return can be cancelled.");
			if (!await _returns.CancelDraftAsync(transaction, id, version, token)) throw new ConcurrencyConflictException("material return");
			var after = Copy(before); after.Status = MaterialReturnStatus.Cancelled; after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token); return after;
		}, cancellationToken);
	}

	public Task<IReadOnlyList<StockMovement>> CorrectAsync(long id, long version, long reasonCodeId, string correctionReason, CancellationToken cancellationToken = default)
	{
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id)); var userId = _reversals.RequireUser();
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var value = await _returns.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("The material return was not found.");
			if (value.Version != version) throw new ConcurrencyConflictException("material return");
			if (value.Status != MaterialReturnStatus.Posted) throw new InvalidOperationException("Only a posted material return can be corrected by counter-booking.");
			var originals = await _movements.ListOriginalsByReferenceAsync(transaction, DocumentReference(value.ReturnNumber), token);
			if (originals.Count != value.Lines.Count || originals.Any(movement => movement.MovementType != StockMovementType.MaterialReturn)) throw new InvalidOperationException("The material-return movements are incomplete or inconsistent.");
			return await _reversals.CreateReversalsAsync(transaction, originals, reasonCodeId, correctionReason, userId, token);
		}, cancellationToken);
	}

	private async Task<MaterialReturn> SaveDraftAsync(DatabaseTransactionContext transaction, MaterialReturn value, long userId, CancellationToken cancellationToken)
	{
		MaterialReturn? before = null;
		if (value.Id == 0) { value.CreatedByUserId = userId; value.ReturnNumber = $"PENDING-{Guid.NewGuid():N}"; value.Id = await _returns.CreateAsync(transaction, value, cancellationToken); value.ReturnNumber = $"MR-{value.Id:000000}"; if (await _returns.UpdateReturnNumberAsync(transaction, value.Id, value.ReturnNumber, cancellationToken) != 1) throw new ConcurrencyConflictException("material return number"); }
		else { before = await _returns.GetByIdAsync(transaction, value.Id, cancellationToken) ?? throw new InvalidOperationException("The material return was not found."); if (before.Version != value.Version) throw new ConcurrencyConflictException("material return"); if (before.Status != MaterialReturnStatus.Draft) throw new InvalidOperationException("Only draft material returns can be edited."); value.ReturnNumber = before.ReturnNumber; value.CreatedByUserId = before.CreatedByUserId; if (!await _returns.UpdateDraftAsync(transaction, value, cancellationToken)) throw new ConcurrencyConflictException("material return"); value.Version++; }
		await ValidateReferencesAsync(transaction, value, cancellationToken);
		var existingIds = before?.Lines.Select(line => line.Id).ToHashSet() ?? []; var suppliedIds = value.Lines.Where(line => line.Id > 0).Select(line => line.Id).ToArray();
		if (suppliedIds.Distinct().Count() != suppliedIds.Length || suppliedIds.Any(id => !existingIds.Contains(id))) throw new InvalidOperationException("A material-return line does not belong to this return.");
		await _returns.DeleteLinesAsync(transaction, value.Id, existingIds.Except(suppliedIds).OrderBy(id => id).ToArray(), cancellationToken);
		var lineNumber = 1; foreach (var line in value.Lines) { line.MaterialReturnId = value.Id; line.LineNumber = lineNumber++; if (line.Id == 0) line.Id = await _returns.CreateLineAsync(transaction, line, cancellationToken); else { if (!await _returns.UpdateLineAsync(transaction, line, cancellationToken)) throw new ConcurrencyConflictException("material return line"); line.Version++; } }
		await _auditEntries.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(value.Id, value) : _audit.CreateUpdatedEntry(value.Id, before, value), cancellationToken); return value;
	}

	private async Task<MaterialReturn> PostAsync(DatabaseTransactionContext transaction, long id, long version, long userId, CancellationToken cancellationToken)
	{
		var before = await _returns.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("The material return was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("material return"); if (before.Status != MaterialReturnStatus.Draft) throw new InvalidOperationException("Only a draft material return can be posted.");
		NormalizeAndValidate(before); await ValidateReferencesAsync(transaction, before, cancellationToken);
		var postedAtUtc = DateTime.UtcNow;
		foreach (var line in before.Lines.OrderBy(line => line.LineNumber)) { var movement = new StockMovement { InventoryId = line.InventoryId, ReasonCodeId = line.ReasonCodeId, MovementType = StockMovementType.MaterialReturn, TimestampUtc = postedAtUtc, Quantity = line.Quantity, Reference = DocumentReference(before.ReturnNumber), Notes = line.Notes }; movement.Id = await _movements.CreateAsync(transaction, movement, cancellationToken); }
		if (!await _returns.SetPostedAsync(transaction, id, version, userId, postedAtUtc, cancellationToken)) throw new ConcurrencyConflictException("material return");
		var after = Copy(before); after.Status = MaterialReturnStatus.Posted; after.PostedByUserId = userId; after.PostedAtUtc = postedAtUtc; after.Version++;
		await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), cancellationToken); return after;
	}

	private async Task ValidateReferencesAsync(DatabaseTransactionContext transaction, MaterialReturn value, CancellationToken cancellationToken)
	{
		if (value.OriginalMaterialIssueId is not null) { var issue = await _issues.GetByIdAsync(transaction, value.OriginalMaterialIssueId.Value, cancellationToken) ?? throw new InvalidOperationException("The referenced material issue was not found."); if (issue.Status != MaterialIssueStatus.Posted) throw new InvalidOperationException("Only a posted, unreversed material issue can be referenced."); value.OriginalMaterialIssueNumber = issue.IssueNumber; }
		var inventories = await _inventories.GetByIdsForUpdateAsync(transaction, value.Lines.Select(line => line.InventoryId), cancellationToken); if (inventories.Count != value.Lines.Select(line => line.InventoryId).Distinct().Count() || inventories.Any(inventory => !inventory.IsActive)) throw new InvalidOperationException("Every material-return inventory must exist and be active.");
		var reasons = await _reasonCodes.GetByIdsAsync(transaction, value.Lines.Select(line => line.ReasonCodeId), cancellationToken); if (reasons.Count != value.Lines.Select(line => line.ReasonCodeId).Distinct().Count() || reasons.Any(reason => !reason.IsActive)) throw new InvalidOperationException("Every material-return reason code must exist and be active.");
	}

	private static void NormalizeAndValidate(MaterialReturn value)
	{
		if (value.Status != MaterialReturnStatus.Draft) throw new InvalidOperationException("Only draft material returns can be saved or posted."); value.RecipientOrSource = value.RecipientOrSource?.Trim() ?? string.Empty; value.Reference = Normalize(value.Reference); value.Notes = Normalize(value.Notes);
		if (value.RecipientOrSource.Length == 0) throw new ArgumentException("A recipient or source is required."); if (value.OriginalMaterialIssueId is null && value.Reference is null && value.Notes is null) throw new ArgumentException("A return without an original material issue requires a business reference or explanation.");
		if (value.RecipientOrSource.Length > 250 || value.Reference?.Length > 250 || value.Notes?.Length > 4000) throw new ArgumentException("Material-return text exceeds its maximum length."); if (value.Lines.Count == 0) throw new InvalidOperationException("A material return requires at least one line.");
		if (value.Lines.Any(line => line.InventoryId <= 0 || line.ReasonCodeId <= 0)) throw new InvalidOperationException("Every line requires an inventory and reason code."); if (value.Lines.Any(line => line.Quantity <= 0)) throw new ArgumentOutOfRangeException(nameof(value), "Every return quantity must be greater than zero."); if (value.Lines.Select(line => line.InventoryId).Distinct().Count() != value.Lines.Count) throw new InvalidOperationException("An inventory can only occur once per material return.");
		foreach (var line in value.Lines) { line.Notes = Normalize(line.Notes); if (line.Notes?.Length > 2000) throw new ArgumentException("Line notes must not exceed 2000 characters."); }
	}

	private long RequireUser(string operation) => _audit.CurrentUserId ?? throw new InvalidOperationException($"A signed-in user is required to {operation} a material return.");
	private static string DocumentReference(string number) => $"Material Return {number}";
	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	private static MaterialReturn Copy(MaterialReturn value) => new() { Id = value.Id, ReturnNumber = value.ReturnNumber, ReturnDate = value.ReturnDate, Status = value.Status, RecipientOrSource = value.RecipientOrSource, OriginalMaterialIssueId = value.OriginalMaterialIssueId, OriginalMaterialIssueNumber = value.OriginalMaterialIssueNumber, Reference = value.Reference, Notes = value.Notes, CreatedByUserId = value.CreatedByUserId, PostedByUserId = value.PostedByUserId, PostedAtUtc = value.PostedAtUtc, Version = value.Version, Lines = value.Lines };
}
