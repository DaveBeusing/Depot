// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesTimelineService
{
	private readonly SalesTimelineRepository _timeline;
	private readonly PurchaseOrderHistoryService? _purchaseHistory;
	private readonly IAuthorizationService _authorization;

	public SalesTimelineService(SalesTimelineRepository timeline, IAuthorizationService authorization)
		: this(timeline, null, authorization)
	{
	}

	public SalesTimelineService(
		SalesTimelineRepository timeline,
		PurchaseOrderHistoryService? purchaseHistory,
		IAuthorizationService authorization)
	{
		_timeline = timeline;
		_purchaseHistory = purchaseHistory;
		_authorization = authorization;
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListAsync(SalesOrder order, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesOrdersView);
		return Prepare(await _timeline.ListSalesAsync(order.Id, cancellationToken));
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListAsync(Shipment shipment, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ShipmentsView);
		return Prepare(await _timeline.ListSalesAsync(shipment.SalesOrderId, cancellationToken));
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListAsync(SalesInvoice invoice, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesInvoicesView);
		return invoice.SalesOrderId is long orderId ? Prepare(await _timeline.ListSalesAsync(orderId, cancellationToken)) : [];
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListAsync(PurchaseOrder order, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersView);
		var items = (await _timeline.ListPurchaseAsync(order.Id, cancellationToken)).ToList();
		await AppendPurchaseHistoryAsync(items, order.Id, order.OrderNumber, cancellationToken);
		return Prepare(items);
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListAsync(GoodsReceipt receipt, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.GoodsReceiptsView);
		var items = (await _timeline.ListPurchaseAsync(receipt.PurchaseOrderId, cancellationToken)).ToList();
		var order = items.FirstOrDefault(value => value.Kind == WorkflowTimelineKind.PurchaseOrder);
		if (order is not null) await AppendPurchaseHistoryAsync(items, receipt.PurchaseOrderId, order.DisplayNumber, cancellationToken);
		return Prepare(items);
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListAsync(FinanceSupplierDocument document, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		var purchaseOrderIds = await _timeline.FindPurchaseOrderIdsForSupplierDocumentAsync(document.Id, cancellationToken);
		var items = new List<WorkflowTimelineItem>();
		if (purchaseOrderIds.Count == 0)
		{
			items.AddRange(await _timeline.ListSupplierDocumentAsync(document.Id, cancellationToken));
		}
		else
		{
			foreach (var purchaseOrderId in purchaseOrderIds)
			{
				var purchaseItems = await _timeline.ListPurchaseAsync(purchaseOrderId, cancellationToken);
				items.AddRange(purchaseItems);
				var order = purchaseItems.FirstOrDefault(value => value.Kind == WorkflowTimelineKind.PurchaseOrder);
				if (order is not null) await AppendPurchaseHistoryAsync(items, purchaseOrderId, order.DisplayNumber, cancellationToken);
			}
		}
		return Prepare(items);
	}

	private async Task AppendPurchaseHistoryAsync(
		List<WorkflowTimelineItem> items,
		long purchaseOrderId,
		string displayNumber,
		CancellationToken cancellationToken)
	{
		if (_purchaseHistory is null || !_authorization.HasPermission(ApplicationPermission.PurchaseOrdersView)) return;
		var history = await _purchaseHistory.GetHistoryAsync(purchaseOrderId, cancellationToken);
		foreach (var entry in history)
		{
			var status = entry.NewStatus;
			switch (status)
			{
				case "Pending Approval":
					items.Add(PurchaseHistoryItem(WorkflowTimelineKind.PurchaseApproval, purchaseOrderId, displayNumber, "Submitted for approval", status, entry.TimestampUtc, WorkflowTimelineSeverity.Information));
					break;
				case "Approved":
					items.Add(PurchaseHistoryItem(WorkflowTimelineKind.PurchaseApproval, purchaseOrderId, displayNumber, "Purchase order approved", status, entry.TimestampUtc, WorkflowTimelineSeverity.Success));
					break;
				case "Rejected":
					items.Add(PurchaseHistoryItem(WorkflowTimelineKind.PurchaseApproval, purchaseOrderId, displayNumber, "Purchase order rejected", status, entry.TimestampUtc, WorkflowTimelineSeverity.Warning, isCorrection: true));
					break;
				case "Ordered":
					items.Add(PurchaseHistoryItem(WorkflowTimelineKind.PurchaseOrdered, purchaseOrderId, displayNumber, "Purchase order placed", status, entry.TimestampUtc, WorkflowTimelineSeverity.Success));
					break;
				case "Closed":
					items.Add(PurchaseHistoryItem(WorkflowTimelineKind.PurchaseOrder, purchaseOrderId, displayNumber, "Purchase order closed", status, entry.TimestampUtc, WorkflowTimelineSeverity.Success));
					break;
				case "Cancelled":
					items.Add(PurchaseHistoryItem(WorkflowTimelineKind.PurchaseOrder, purchaseOrderId, displayNumber, "Purchase order cancelled", status, entry.TimestampUtc, WorkflowTimelineSeverity.Warning, isCorrection: true));
					break;
			}
		}
	}

	private IReadOnlyList<WorkflowTimelineItem> Prepare(IEnumerable<WorkflowTimelineItem> source)
	{
		var values = source
			.Where(CanView)
			.Select(value => value with { RouteId = RouteFor(value.Kind), IsCurrent = false })
			.GroupBy(value => new { value.Kind, value.EntityId, value.DisplayNumber, value.Title, value.Status, value.OccurredAt, value.IsCorrection, value.IsReversal })
			.Select(group => group.First())
			.OrderBy(value => Sequence(value.Kind))
			.ThenBy(value => value.OccurredAt)
			.ThenBy(value => value.DisplayNumber, StringComparer.Ordinal)
			.ThenBy(value => value.EntityId)
			.ToArray();
		if (values.Length == 0) return values;
		values[^1] = values[^1] with { IsCurrent = true };
		return values;
	}

	private bool CanView(WorkflowTimelineItem item) => item.Kind switch
	{
		WorkflowTimelineKind.SalesQuote => _authorization.HasPermission(ApplicationPermission.SalesQuotesView),
		WorkflowTimelineKind.SalesOrder or WorkflowTimelineKind.SalesApproval or WorkflowTimelineKind.ReservationRelease => _authorization.HasPermission(ApplicationPermission.SalesOrdersView),
		WorkflowTimelineKind.Shipment or WorkflowTimelineKind.ShipmentReversal => _authorization.HasPermission(ApplicationPermission.ShipmentsView),
		WorkflowTimelineKind.CustomerReturn => _authorization.HasPermission(ApplicationPermission.CustomerReturnsView) && _authorization.HasPermission(ApplicationPermission.ShipmentsView),
		WorkflowTimelineKind.SalesInvoice => _authorization.HasPermission(ApplicationPermission.SalesInvoicesView),
		WorkflowTimelineKind.SalesCreditNote => _authorization.HasPermission(ApplicationPermission.CreditNotesView) && _authorization.HasPermission(ApplicationPermission.SalesInvoicesView),
		WorkflowTimelineKind.Receivable or WorkflowTimelineKind.ReceivablePayment or WorkflowTimelineKind.ReceivablePaymentReversal => _authorization.HasPermission(ApplicationPermission.FinanceReceivablesView),
		WorkflowTimelineKind.PurchaseOrder or WorkflowTimelineKind.PurchaseApproval or WorkflowTimelineKind.PurchaseOrdered => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersView),
		WorkflowTimelineKind.GoodsReceipt or WorkflowTimelineKind.GoodsReceiptReversal => _authorization.HasPermission(ApplicationPermission.GoodsReceiptsView),
		WorkflowTimelineKind.SupplierInvoice or WorkflowTimelineKind.SupplierMatch or WorkflowTimelineKind.SupplierApproval or WorkflowTimelineKind.SupplierDocumentReversal or WorkflowTimelineKind.Payable or WorkflowTimelineKind.SupplierPayment or WorkflowTimelineKind.SupplierPaymentReversal => _authorization.HasPermission(ApplicationPermission.FinancePayablesView),
		_ => false
	};

	private static string? RouteFor(WorkflowTimelineKind kind) => kind switch
	{
		WorkflowTimelineKind.SalesQuote => "sales.quotes",
		WorkflowTimelineKind.SalesOrder or WorkflowTimelineKind.SalesApproval or WorkflowTimelineKind.ReservationRelease => "sales.orders",
		WorkflowTimelineKind.Shipment or WorkflowTimelineKind.ShipmentReversal or WorkflowTimelineKind.CustomerReturn => "sales.shipping",
		WorkflowTimelineKind.SalesInvoice or WorkflowTimelineKind.SalesCreditNote => "sales.invoices",
		WorkflowTimelineKind.Receivable or WorkflowTimelineKind.ReceivablePayment or WorkflowTimelineKind.ReceivablePaymentReversal => "finance.receivables",
		WorkflowTimelineKind.PurchaseOrder or WorkflowTimelineKind.PurchaseApproval or WorkflowTimelineKind.PurchaseOrdered => "purchasing.purchase-orders",
		WorkflowTimelineKind.GoodsReceipt or WorkflowTimelineKind.GoodsReceiptReversal => "purchasing.goods-receipts",
		WorkflowTimelineKind.SupplierInvoice or WorkflowTimelineKind.SupplierMatch or WorkflowTimelineKind.SupplierApproval or WorkflowTimelineKind.SupplierDocumentReversal or WorkflowTimelineKind.Payable or WorkflowTimelineKind.SupplierPayment or WorkflowTimelineKind.SupplierPaymentReversal => "finance.payables",
		_ => null
	};

	private static int Sequence(WorkflowTimelineKind kind) => kind switch
	{
		WorkflowTimelineKind.SalesQuote => 10,
		WorkflowTimelineKind.SalesOrder => 20,
		WorkflowTimelineKind.SalesApproval => 30,
		WorkflowTimelineKind.ReservationRelease => 40,
		WorkflowTimelineKind.Shipment => 50,
		WorkflowTimelineKind.ShipmentReversal => 51,
		WorkflowTimelineKind.SalesInvoice => 60,
		WorkflowTimelineKind.SalesCreditNote => 61,
		WorkflowTimelineKind.CustomerReturn => 62,
		WorkflowTimelineKind.Receivable => 70,
		WorkflowTimelineKind.ReceivablePayment => 80,
		WorkflowTimelineKind.ReceivablePaymentReversal => 81,
		WorkflowTimelineKind.PurchaseOrder => 110,
		WorkflowTimelineKind.PurchaseApproval => 120,
		WorkflowTimelineKind.PurchaseOrdered => 130,
		WorkflowTimelineKind.GoodsReceipt => 140,
		WorkflowTimelineKind.GoodsReceiptReversal => 141,
		WorkflowTimelineKind.SupplierInvoice => 150,
		WorkflowTimelineKind.SupplierMatch => 160,
		WorkflowTimelineKind.SupplierApproval => 170,
		WorkflowTimelineKind.SupplierDocumentReversal => 171,
		WorkflowTimelineKind.Payable => 180,
		WorkflowTimelineKind.SupplierPayment => 190,
		WorkflowTimelineKind.SupplierPaymentReversal => 191,
		_ => int.MaxValue
	};

	private static WorkflowTimelineItem PurchaseHistoryItem(
		WorkflowTimelineKind kind,
		long entityId,
		string displayNumber,
		string title,
		string status,
		DateTime occurredAt,
		WorkflowTimelineSeverity severity,
		bool isCorrection = false) => new()
	{
		Kind = kind,
		EntityId = entityId,
		DisplayNumber = displayNumber,
		Title = title,
		Status = status,
		OccurredAt = occurredAt,
		IsCorrection = isCorrection,
		Severity = severity
	};
}
