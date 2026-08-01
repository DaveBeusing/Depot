// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class PurchaseOrderService
{
	private readonly PurchaseOrderRepository _orders;
	private readonly SupplierRepository _suppliers;
	private readonly ItemRepository _items;
	private readonly AuditService _audit;
	private readonly AuthorizationService _authorization;

	public PurchaseOrderService(PurchaseOrderRepository orders, SupplierRepository suppliers, ItemRepository items, AuditService audit, AuthorizationService authorization)
	{
		_orders = orders;
		_suppliers = suppliers;
		_items = items;
		_audit = audit;
		_authorization = authorization;
	}

	public Task<PageResult<PurchaseOrder>> SearchAsync(string? searchText, PurchaseOrderStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default) =>
		_orders.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken);

	public Task<PurchaseOrder?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => _orders.GetByIdAsync(id, cancellationToken);

	public bool CanCurrentUserApprove => _authorization.CanApprovePurchaseOrders();

	public async Task<PurchaseOrder> SaveDraftAsync(PurchaseOrder draft, CancellationToken cancellationToken = default)
	{
		draft.Notes = Normalize(draft.Notes);
		if (draft.Id != 0 && draft.Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only draft purchase orders can be edited.");
		var supplier = await _suppliers.GetByIdAsync(draft.SupplierId, cancellationToken) ?? throw new InvalidOperationException("The selected supplier was not found.");
		if (!supplier.IsActive) throw new InvalidOperationException("The selected supplier is inactive.");
		if (draft.ExpectedDeliveryDate is not null && draft.ExpectedDeliveryDate.Value.Date < draft.OrderDate.Date) throw new ArgumentException("Expected delivery date cannot be earlier than the order date.");
		if (draft.Notes?.Length > 4000) throw new ArgumentException("Notes must not exceed 4000 characters.");
		if (draft.Lines.Count == 0) throw new InvalidOperationException("A purchase order requires at least one line.");
		if (draft.Lines.Select(line => line.ItemId).Distinct().Count() != draft.Lines.Count) throw new InvalidOperationException("An item can only occur once per purchase order.");
		var items = (await _items.GetByIdsAsync(
			draft.Lines.Select(line => line.ItemId),
			cancellationToken)).ToDictionary(item => item.Id);
		foreach (var line in draft.Lines)
		{
			if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(line.Quantity), "Quantity must be greater than zero.");
			if (line.UnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(line.UnitPrice), "Unit price cannot be negative.");
			if (!items.TryGetValue(line.ItemId, out var item)) throw new InvalidOperationException("An ordered item was not found.");
			if (!item.IsActive) throw new InvalidOperationException($"Item '{item.PartNumber}' is inactive.");
			line.ItemPartNumber = item.PartNumber;
			line.ItemDescription = item.Description;
		}
		draft.SupplierName = supplier.Name;
		var isNew = draft.Id == 0;
		if (isNew)
		{
			var creator = CurrentUser();
			draft.CreatedByUserId = creator.Id;
			draft.CreatedByUserDisplay = creator.DisplayName;
		}
		var before = isNew ? null : await _orders.GetByIdAsync(draft.Id, cancellationToken);
		return await _orders.SaveDraftAsync(
			draft,
			after => isNew
				? _audit.CreateCreatedEntry(after.Id, after)
				: _audit.CreateUpdatedEntry(after.Id, before ?? throw new InvalidOperationException("Purchase order was not found before saving."), after),
			cancellationToken);
	}

	public Task<PurchaseOrder> SubmitForApprovalAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		var user = CurrentUser();
		return ChangeStatusAsync(id, version, PurchaseOrderStatus.Draft, PurchaseOrderStatus.PendingApproval,
			order =>
			{
				order.CreatedByUserId ??= user.Id;
				order.CreatedByUserDisplay ??= user.DisplayName;
				order.SubmittedByUserId = user.Id;
				order.SubmittedByUserDisplay = user.DisplayName;
				order.SubmittedAtUtc = DateTime.UtcNow;
				order.ApprovalDecisionByUserId = null;
				order.ApprovalDecisionByUserDisplay = null;
				order.ApprovalDecisionAtUtc = null;
				order.ApprovalComment = null;
			}, cancellationToken);
	}

	public Task<PurchaseOrder> ApproveAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default) =>
		DecideApprovalAsync(id, version, PurchaseOrderStatus.Approved, comment, cancellationToken);

	public Task<PurchaseOrder> RejectAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default) =>
		DecideApprovalAsync(id, version, PurchaseOrderStatus.Rejected, comment, cancellationToken);

	public Task<PurchaseOrder> ReopenRejectedAsync(long id, long version, CancellationToken cancellationToken = default) =>
		ChangeStatusAsync(id, version, PurchaseOrderStatus.Rejected, PurchaseOrderStatus.Draft,
			order =>
			{
				order.SubmittedByUserId = null;
				order.SubmittedByUserDisplay = null;
				order.SubmittedAtUtc = null;
				order.ApprovalDecisionByUserId = null;
				order.ApprovalDecisionByUserDisplay = null;
				order.ApprovalDecisionAtUtc = null;
				order.ApprovalComment = null;
			}, cancellationToken);

	public Task<PurchaseOrder> MarkOrderedAsync(long id, long version, CancellationToken cancellationToken = default) =>
		ChangeStatusAsync(id, version, PurchaseOrderStatus.Approved, PurchaseOrderStatus.Ordered, null, cancellationToken);

	public Task<PurchaseOrder> CloseAsync(long id, long version, CancellationToken cancellationToken = default) =>
		ChangeStatusAsync(id, version, PurchaseOrderStatus.Received, PurchaseOrderStatus.Closed, null, cancellationToken);

	public async Task<PurchaseOrder> CancelAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		var order = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered)) throw new InvalidOperationException("This purchase order can no longer be cancelled.");
		return await ChangeStatusAsync(order, version, order.Status, PurchaseOrderStatus.Cancelled, null, cancellationToken);
	}

	private async Task<PurchaseOrder> DecideApprovalAsync(long id, long version, PurchaseOrderStatus decision, string? comment, CancellationToken cancellationToken)
	{
		if (!_authorization.CanApprovePurchaseOrders())
			throw new UnauthorizedAccessException("The current user is not permitted to approve purchase orders.");
		var user = CurrentUser();
		comment = Normalize(comment);
		if (comment?.Length > 2000) throw new ArgumentException("The approval comment must not exceed 2000 characters.", nameof(comment));
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		if (before.CreatedByUserId == user.Id)
			throw new InvalidOperationException("A purchase order cannot be approved or rejected by its creator.");
		return await ChangeStatusAsync(before, version, PurchaseOrderStatus.PendingApproval, decision,
			order =>
			{
				order.ApprovalDecisionByUserId = user.Id;
				order.ApprovalDecisionByUserDisplay = user.DisplayName;
				order.ApprovalDecisionAtUtc = DateTime.UtcNow;
				order.ApprovalComment = comment;
			}, cancellationToken);
	}

	private async Task<PurchaseOrder> ChangeStatusAsync(long id, long version, PurchaseOrderStatus expected, PurchaseOrderStatus status, Action<PurchaseOrder>? applyMetadata, CancellationToken cancellationToken)
	{
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		return await ChangeStatusAsync(before, version, expected, status, applyMetadata, cancellationToken);
	}

	private async Task<PurchaseOrder> ChangeStatusAsync(
		PurchaseOrder before,
		long version,
		PurchaseOrderStatus expected,
		PurchaseOrderStatus status,
		Action<PurchaseOrder>? applyMetadata,
		CancellationToken cancellationToken)
	{
		var after = new PurchaseOrder
		{
			Id = before.Id,
			OrderNumber = before.OrderNumber,
			SupplierId = before.SupplierId,
			SupplierName = before.SupplierName,
			OrderDate = before.OrderDate,
			ExpectedDeliveryDate = before.ExpectedDeliveryDate,
			Notes = before.Notes,
			Status = status,
			CreatedByUserId = before.CreatedByUserId,
			SubmittedByUserId = before.SubmittedByUserId,
			SubmittedAtUtc = before.SubmittedAtUtc,
			ApprovalDecisionByUserId = before.ApprovalDecisionByUserId,
			ApprovalDecisionAtUtc = before.ApprovalDecisionAtUtc,
			ApprovalComment = before.ApprovalComment,
			CreatedByUserDisplay = before.CreatedByUserDisplay,
			SubmittedByUserDisplay = before.SubmittedByUserDisplay,
			ApprovalDecisionByUserDisplay = before.ApprovalDecisionByUserDisplay,
			Version = version + 1,
			Lines = before.Lines
		};
		applyMetadata?.Invoke(after);
		return await _orders.SetStatusAsync(
			before.Id,
			version,
			expected,
			status,
			after,
			_audit.CreateUpdatedEntry(before.Id, before, after),
			cancellationToken);
	}

	private User CurrentUser() =>
		_authorization.CurrentUser is { IsActive: true } user
			? user
			: throw new UnauthorizedAccessException("An active signed-in user is required.");

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
