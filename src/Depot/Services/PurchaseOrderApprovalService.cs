// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class PurchaseOrderApprovalService
{
	private const int HistoryLimit = 100;
	private readonly PurchaseOrderRepository _orders;
	private readonly AuditRepository _auditRepository;
	private readonly PurchaseOrderService _purchaseOrders;
	private readonly IAuthorizationService _authorization;
	private readonly AuditJsonSanitizer _sanitizer;

	public PurchaseOrderApprovalService(
		PurchaseOrderRepository orders,
		AuditRepository auditRepository,
		PurchaseOrderService purchaseOrders,
		IAuthorizationService authorization,
		AuditJsonSanitizer sanitizer)
	{
		_orders = orders;
		_auditRepository = auditRepository;
		_purchaseOrders = purchaseOrders;
		_authorization = authorization;
		_sanitizer = sanitizer;
	}

	public bool CanDecide(long? createdByUserId) =>
		_authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove) &&
		(_authorization.CurrentUser?.Id != createdByUserId ||
		 _authorization.IsInRole(SystemRoleCatalog.AdministratorCode));

	public async Task<PurchaseOrderApprovalPage> SearchAsync(
		PurchaseOrderApprovalFilter filter,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		var pageTask = _orders.SearchPendingApprovalsAsync(filter, pageNumber, pageSize, cancellationToken);
		var summaryTask = _orders.GetPendingApprovalSummaryAsync(filter, cancellationToken);
		await Task.WhenAll(pageTask, summaryTask);
		return new PurchaseOrderApprovalPage(
			await pageTask,
			await summaryTask ?? new PurchaseOrderApprovalSummary(0, null, 0));
	}

	public async Task<PurchaseOrderApprovalDetails?> GetDetailsAsync(
		long purchaseOrderId,
		CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		var orderTask = _orders.GetByIdAsync(purchaseOrderId, cancellationToken);
		var historyTask = _auditRepository.GetEntityHistoryAsync(
			nameof(PurchaseOrder), purchaseOrderId, HistoryLimit, cancellationToken);
		await Task.WhenAll(orderTask, historyTask);
		var order = await orderTask;
		if (order is null) return null;
		var history = (await historyTask).Select(ToHistoryItem).ToArray();
		return new PurchaseOrderApprovalDetails(order, history);
	}

	public Task<PurchaseOrder?> GetCurrentAsync(long purchaseOrderId, CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		return _orders.GetByIdAsync(purchaseOrderId, cancellationToken);
	}

	public Task<PurchaseOrder> ApproveAsync(
		long purchaseOrderId,
		long version,
		string? comment,
		CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		return _purchaseOrders.ApproveAsync(purchaseOrderId, version, comment, cancellationToken);
	}

	public Task<PurchaseOrder> ApproveAsync(long purchaseOrderId, long version, string? comment, Guid operationId, CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		return _purchaseOrders.ApproveAsync(purchaseOrderId, version, comment, operationId, cancellationToken);
	}

	public Task<PurchaseOrder> RejectAsync(
		long purchaseOrderId,
		long version,
		string? comment,
		CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		return _purchaseOrders.RejectAsync(purchaseOrderId, version, comment, cancellationToken);
	}

	public Task<PurchaseOrder> RejectAsync(long purchaseOrderId, long version, string? comment, Guid operationId, CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		return _purchaseOrders.RejectAsync(purchaseOrderId, version, comment, operationId, cancellationToken);
	}

	public async Task<PurchaseOrderApprovalSummary> GetSummaryAsync(
		PurchaseOrderApprovalFilter filter,
		CancellationToken cancellationToken = default)
	{
		EnsureAuthorized();
		return await _orders.GetPendingApprovalSummaryAsync(filter, cancellationToken)
			?? new PurchaseOrderApprovalSummary(0, null, 0);
	}

	private PurchaseOrderApprovalHistoryItem ToHistoryItem(AuditLogDetails entry) =>
		new(
			entry.TimestampUtc,
			entry.UserEmail,
			entry.Action,
			DescribeStatusChange(entry.BeforeJson, entry.AfterJson),
			_sanitizer.Compare(entry.BeforeJson, entry.AfterJson));

	private static string DescribeStatusChange(string? beforeJson, string? afterJson)
	{
		var before = ReadStatus(beforeJson);
		var after = ReadStatus(afterJson);
		if (before is null && after is null) return "Details changed";
		if (before is null) return $"Created as {DisplayStatus(after)}";
		if (after is null || before == after) return $"{DisplayStatus(before)} updated";
		return $"{DisplayStatus(before)} → {DisplayStatus(after)}";
	}

	private static PurchaseOrderStatus? ReadStatus(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try
		{
			using var document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("status", out var value) ||
				!value.TryGetInt32(out var status) ||
				!Enum.IsDefined(typeof(PurchaseOrderStatus), status)) return null;
			return (PurchaseOrderStatus)status;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string DisplayStatus(PurchaseOrderStatus? status) => status switch
	{
		PurchaseOrderStatus.PendingApproval => "Pending Approval",
		PurchaseOrderStatus.PartiallyReceived => "Partially Received",
		null => "Unknown",
		_ => status.Value.ToString()
	};

	private void EnsureAuthorized()
	{
		if (!_authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove))
			throw new UnauthorizedAccessException("The current user is not permitted to approve purchase orders.");
	}
}
