// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class PurchaseOrderService
{
	private readonly PurchaseOrderRepository _orders;
	private readonly SupplierRepository _suppliers;
	private readonly ItemRepository _items;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly NotificationService _notifications;

	public PurchaseOrderService(PurchaseOrderRepository orders, SupplierRepository suppliers, ItemRepository items, AuditService audit, IAuthorizationService authorization, NotificationService notifications)
	{
		_orders = orders;
		_suppliers = suppliers;
		_items = items;
		_audit = audit;
		_authorization = authorization;
		_notifications = notifications;
	}

	public Task<PageResult<PurchaseOrder>> SearchAsync(string? searchText, PurchaseOrderStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersView);
		return _orders.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken);
	}

	public Task<PurchaseOrder?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersView);
		return _orders.GetByIdAsync(id, cancellationToken);
	}

	public bool CanCurrentUserApprove => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove);
	public bool CanCurrentUserCreate => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersCreate);
	public bool CanCurrentUserEdit => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersEdit);
	public bool CanCurrentUserSubmit => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersSubmit);
	public bool CanCurrentUserOrder => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersOrder);
	public bool CanCurrentUserClose => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersClose);

	public async Task<PurchaseOrder> SaveDraftAsync(PurchaseOrder draft, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(draft.Id == 0 ? ApplicationPermission.PurchaseOrdersCreate : ApplicationPermission.PurchaseOrdersEdit);
		if (draft.Id != 0 && draft.Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only draft purchase orders can be edited.");
		await ValidateOrderContentAsync(draft, cancellationToken);
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

	public async Task<PurchaseOrder> SubmitForApprovalAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersSubmit);
		var user = CurrentUser();
		var before = await GetForTransitionAsync(id, version, PurchaseOrderStatus.Draft, cancellationToken);
		await ValidateOrderContentAsync(before, cancellationToken);
		return await ChangeStatusAsync(before, version, PurchaseOrderStatus.Draft, PurchaseOrderStatus.PendingApproval,
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
		ApproveAsync(id, version, comment, Guid.NewGuid(), cancellationToken);

	public Task<PurchaseOrder> ApproveAsync(long id, long version, string? comment, Guid operationId, CancellationToken cancellationToken = default) =>
		DecideApprovalAsync(id, version, PurchaseOrderStatus.Approved, comment, operationId, cancellationToken);

	public Task<PurchaseOrder> RejectAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default) =>
		RejectAsync(id, version, comment, Guid.NewGuid(), cancellationToken);

	public Task<PurchaseOrder> RejectAsync(long id, long version, string? comment, Guid operationId, CancellationToken cancellationToken = default) =>
		DecideApprovalAsync(id, version, PurchaseOrderStatus.Rejected, comment, operationId, cancellationToken);

	public Task<PurchaseOrder> ReopenRejectedAsync(long id, long version, CancellationToken cancellationToken = default) =>
		RequireAndChangeStatusAsync(ApplicationPermission.PurchaseOrdersEdit, id, version, PurchaseOrderStatus.Rejected, PurchaseOrderStatus.Draft,
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

	public Task<PurchaseOrder> PlaceOrderAsync(long id, long version, CancellationToken cancellationToken = default) =>
		PlaceOrderAsync(id, version, Guid.NewGuid(), cancellationToken);

	public async Task<PurchaseOrder> PlaceOrderAsync(long id, long version, Guid operationId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersOrder);
		var operation = new WorkflowOperation(operationId, WorkflowOperationNames.PlacePurchaseOrder, id);
		var completed = await _orders.GetCompletedOperationAsync(operation, cancellationToken);
		if (completed is not null) return completed;
		var before = await GetForTransitionAsync(id, version, PurchaseOrderStatus.Approved, cancellationToken);
		await ValidateOrderContentAsync(before, cancellationToken);
		return await ChangeStatusAsync(before, version, PurchaseOrderStatus.Approved, PurchaseOrderStatus.Ordered, null, operation, cancellationToken);
	}

	public Task<PurchaseOrder> CloseOrderAsync(long id, long version, string reason, CancellationToken cancellationToken = default) =>
		CloseOrderAsync(id, version, reason, Guid.NewGuid(), cancellationToken);

	public async Task<PurchaseOrder> CloseOrderAsync(long id, long version, string reason, Guid operationId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersClose);
		var operation = new WorkflowOperation(operationId, WorkflowOperationNames.ClosePurchaseOrder, id);
		var completed = await _orders.GetCompletedOperationAsync(operation, cancellationToken);
		if (completed is not null) return completed;
		var normalizedReason = Normalize(reason) ?? throw new ArgumentException("A close reason is required.", nameof(reason));
		if (normalizedReason.Length > 2000) throw new ArgumentException("The close reason must not exceed 2000 characters.", nameof(reason));
		var user = CurrentUser();
		var order = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		if (order.Version != version) throw new ConcurrencyConflictException("purchase order");
		if (order.Status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))
			throw new InvalidOperationException("Only ordered or partially received purchase orders can be closed.");
		return await ChangeStatusAsync(order, version, order.Status, PurchaseOrderStatus.Closed,
			result =>
			{
				result.ClosedByUserId = user.Id;
				result.ClosedByUserDisplay = user.DisplayName;
				result.ClosedAtUtc = DateTime.UtcNow;
				result.CloseReason = normalizedReason;
			}, operation, cancellationToken);
	}

	public async Task<PurchaseOrder> CancelAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersEdit);
		var order = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		if (order.Version != version) throw new ConcurrencyConflictException("purchase order");
		if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered)) throw new InvalidOperationException("This purchase order can no longer be cancelled.");
		if (order.Lines.Any(line => line.ReceivedQuantity > 0)) throw new InvalidOperationException("A purchase order with posted goods receipts cannot be cancelled. Close it instead.");
		return await ChangeStatusAsync(order, version, order.Status, PurchaseOrderStatus.Cancelled, null, cancellationToken);
	}

	private async Task<PurchaseOrder> DecideApprovalAsync(long id, long version, PurchaseOrderStatus decision, string? comment, Guid operationId, CancellationToken cancellationToken)
	{
		if (!_authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove))
			throw new UnauthorizedAccessException("The current user is not permitted to approve purchase orders.");
		var user = CurrentUser();
		var operation = new WorkflowOperation(operationId, decision == PurchaseOrderStatus.Approved ? WorkflowOperationNames.ApprovePurchaseOrder : WorkflowOperationNames.RejectPurchaseOrder, id);
		var completed = await _orders.GetCompletedOperationAsync(operation, cancellationToken);
		if (completed is not null) return completed;
		comment = Normalize(comment);
		if (comment?.Length > 2000) throw new ArgumentException("The approval comment must not exceed 2000 characters.", nameof(comment));
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		var isSelfApproval = before.CreatedByUserId == user.Id;
		var isAdministrator = _authorization.IsInRole(SystemRoleCatalog.AdministratorCode);
		if (isSelfApproval && !isAdministrator)
			throw new InvalidOperationException("A purchase order cannot be approved or rejected by its creator.");
		return await ChangeStatusAsync(before, version, PurchaseOrderStatus.PendingApproval, decision,
			order =>
			{
				order.ApprovalDecisionByUserId = user.Id;
				order.ApprovalDecisionByUserDisplay = user.DisplayName;
				order.ApprovalDecisionAtUtc = DateTime.UtcNow;
				order.ApprovalComment = comment;
			}, operation, cancellationToken);
	}

	private async Task<PurchaseOrder> ChangeStatusAsync(long id, long version, PurchaseOrderStatus expected, PurchaseOrderStatus status, Action<PurchaseOrder>? applyMetadata, CancellationToken cancellationToken)
	{
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		return await ChangeStatusAsync(before, version, expected, status, applyMetadata, cancellationToken);
	}

	private Task<PurchaseOrder> RequireAndChangeStatusAsync(ApplicationPermission permission, long id, long version, PurchaseOrderStatus expected, PurchaseOrderStatus status, Action<PurchaseOrder>? applyMetadata, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(permission);
		return ChangeStatusAsync(id, version, expected, status, applyMetadata, cancellationToken);
	}

	private async Task<PurchaseOrder> ChangeStatusAsync(
		PurchaseOrder before,
		long version,
		PurchaseOrderStatus expected,
		PurchaseOrderStatus status,
		Action<PurchaseOrder>? applyMetadata,
		CancellationToken cancellationToken) =>
		await ChangeStatusAsync(before, version, expected, status, applyMetadata, null, cancellationToken);

	private async Task<PurchaseOrder> ChangeStatusAsync(
		PurchaseOrder before,
		long version,
		PurchaseOrderStatus expected,
		PurchaseOrderStatus status,
		Action<PurchaseOrder>? applyMetadata,
		WorkflowOperation? operation,
		CancellationToken cancellationToken)
	{
		if (before.Version != version) throw new ConcurrencyConflictException("purchase order");
		if (before.Status != expected)
			throw new InvalidOperationException($"The purchase order must be in {expected} status for this transition.");
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
			ClosedByUserId = before.ClosedByUserId,
			ClosedAtUtc = before.ClosedAtUtc,
			CloseReason = before.CloseReason,
			CreatedByUserDisplay = before.CreatedByUserDisplay,
			SubmittedByUserDisplay = before.SubmittedByUserDisplay,
			ApprovalDecisionByUserDisplay = before.ApprovalDecisionByUserDisplay,
			ClosedByUserDisplay = before.ClosedByUserDisplay,
			Version = version + 1,
			Lines = before.Lines
		};
		applyMetadata?.Invoke(after);
		var changed = await _orders.SetStatusAsync(
			before.Id,
			version,
			expected,
			status,
			after,
			_audit.CreateUpdatedEntry(before.Id, before, after),
			operation,
			(session, token) => CreateStatusNotificationAsync(new DatabaseTransactionContext(session), before, after, token),
			cancellationToken);
		if (status is PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Rejected)
			_notifications.RaiseChanged();
		return changed;
	}

	private async Task CreateStatusNotificationAsync(
		DatabaseTransactionContext transaction,
		PurchaseOrder before,
		PurchaseOrder after,
		CancellationToken cancellationToken)
	{
		NotificationRequest? request = after.Status switch
		{
			PurchaseOrderStatus.PendingApproval => new(
				NotificationType.Workflow, NotificationSeverity.Information,
				$"Purchase order {after.OrderNumber} requires approval",
				$"{after.SubmittedByUserDisplay ?? "A user"} submitted purchase order {after.OrderNumber} for approval.",
				NotificationSourceTypes.PurchaseOrderApproval, after.Id, after.OrderNumber, after.SubmittedByUserId),
			PurchaseOrderStatus.Approved => new(
				NotificationType.Workflow, NotificationSeverity.Success,
				$"Purchase order {after.OrderNumber} approved",
				$"Purchase order {after.OrderNumber} was approved.",
				NotificationSourceTypes.PurchaseOrder, after.Id, after.OrderNumber, after.ApprovalDecisionByUserId),
			PurchaseOrderStatus.Rejected => new(
				NotificationType.Workflow, NotificationSeverity.Warning,
				$"Purchase order {after.OrderNumber} rejected",
				$"Purchase order {after.OrderNumber} was rejected{(string.IsNullOrWhiteSpace(after.ApprovalComment) ? "." : $": {after.ApprovalComment}")}",
				NotificationSourceTypes.PurchaseOrder, after.Id, after.OrderNumber, after.ApprovalDecisionByUserId),
			_ => null
		};
		if (request is null) return;

		IReadOnlyCollection<long> recipients;
		if (after.Status == PurchaseOrderStatus.PendingApproval)
		{
			var holders = await _notifications.ResolvePermissionHoldersAsync(transaction, ApplicationPermission.PurchaseOrdersApprove, cancellationToken);
			var administrators = (await _notifications.ResolveAdministratorsAsync(transaction, cancellationToken)).ToHashSet();
			recipients = holders.Where(id => id != after.CreatedByUserId || administrators.Contains(id)).Distinct().ToArray();
		}
		else
		{
			recipients = new long?[] { before.CreatedByUserId, before.SubmittedByUserId }
				.Where(id => id is > 0).Select(id => id.GetValueOrDefault()).Distinct().ToArray();
		}
		await _notifications.CreateAsync(transaction, request, recipients, cancellationToken);
	}

	private async Task<PurchaseOrder> GetForTransitionAsync(
		long id,
		long version,
		PurchaseOrderStatus expected,
		CancellationToken cancellationToken)
	{
		var order = await _orders.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException("Purchase order was not found.");
		if (order.Version != version) throw new ConcurrencyConflictException("purchase order");
		if (order.Status != expected)
			throw new InvalidOperationException($"The purchase order must be in {expected} status for this transition.");
		return order;
	}

	private async Task ValidateOrderContentAsync(PurchaseOrder order, CancellationToken cancellationToken)
	{
		order.Notes = Normalize(order.Notes);
		var supplier = await _suppliers.GetByIdAsync(order.SupplierId, cancellationToken)
			?? throw new InvalidOperationException("The selected supplier was not found.");
		if (!supplier.IsActive) throw new InvalidOperationException("The selected supplier is inactive.");
		if (order.ExpectedDeliveryDate is not null && order.ExpectedDeliveryDate.Value.Date < order.OrderDate.Date)
			throw new ArgumentException("Expected delivery date cannot be earlier than the order date.");
		if (order.Notes?.Length > 4000) throw new ArgumentException("Notes must not exceed 4000 characters.");
		if (order.Lines.Count == 0) throw new InvalidOperationException("A purchase order requires at least one line.");
		if (order.Lines.Select(line => line.ItemId).Distinct().Count() != order.Lines.Count)
			throw new InvalidOperationException("An item can only occur once per purchase order.");

		var items = (await _items.GetByIdsAsync(
			order.Lines.Select(line => line.ItemId),
			cancellationToken)).ToDictionary(item => item.Id);
		foreach (var line in order.Lines)
		{
			if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(line.Quantity), "Quantity must be greater than zero.");
			if (line.UnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(line.UnitPrice), "Unit price cannot be negative.");
			if (!items.TryGetValue(line.ItemId, out var item)) throw new InvalidOperationException("An ordered item was not found.");
			if (!item.IsActive) throw new InvalidOperationException($"Item '{item.PartNumber}' is inactive.");
			line.ItemPartNumber = item.PartNumber;
			line.ItemDescription = item.Description;
		}
		order.SupplierName = supplier.Name;
	}

	private User CurrentUser() =>
		_authorization.CurrentUser is { IsActive: true } user
			? user
			: throw new UnauthorizedAccessException("An active signed-in user is required.");

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
