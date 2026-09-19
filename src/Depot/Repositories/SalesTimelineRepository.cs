// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesTimelineRepository : DatabaseRepository
{
	public SalesTimelineRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListSalesAsync(long salesOrderId, CancellationToken cancellationToken)
	{
		var order = await Database.QuerySingleOrDefaultAsync(
			"SELECT Id,OrderNumber,OrderDate,Status,SubmittedAtUtc,ApprovalDecisionAtUtc,ReleasedAtUtc,CancelledAtUtc FROM SalesOrders WHERE Id=$OrderId;",
			reader => new SalesOrderSnapshot(
				reader.GetInt64(0),
				reader.GetString(1),
				ReadDateUtc(reader, 2),
				(SalesOrderStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
				ReadNullableUtc(reader, 4),
				ReadNullableUtc(reader, 5),
				ReadNullableUtc(reader, 6),
				ReadNullableUtc(reader, 7)),
			cancellationToken,
			Parameter("$OrderId", salesOrderId));
		if (order is null) return [];

		var result = new List<WorkflowTimelineItem>();

		var quotes = await TryQueryAsync(
			"SELECT Id,QuoteNumber,CreatedAtUtc,Status FROM SalesQuotes WHERE ConvertedSalesOrderId=$OrderId ORDER BY CreatedAtUtc,Id;",
			reader => new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SalesQuote,
				EntityId = reader.GetInt64(0),
				DisplayNumber = reader.GetString(1),
				Title = "Quote",
				Status = ((SalesQuoteStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)).ToString(),
				OccurredAt = ReadUtc(reader, 2),
				Severity = WorkflowTimelineSeverity.Information
			},
			cancellationToken,
			Parameter("$OrderId", salesOrderId));
		result.AddRange(quotes);

		result.Add(new WorkflowTimelineItem
		{
			Kind = WorkflowTimelineKind.SalesOrder,
			EntityId = order.Id,
			DisplayNumber = order.OrderNumber,
			Title = "Sales order created",
			Status = order.Status.ToString(),
			OccurredAt = order.OrderDateUtc,
			Severity = WorkflowTimelineSeverity.Information
		});

		if (order.SubmittedAtUtc is { } submitted)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SalesApproval,
				EntityId = order.Id,
				DisplayNumber = order.OrderNumber,
				Title = "Submitted for approval",
				Status = "Pending Approval",
				OccurredAt = submitted,
				Severity = WorkflowTimelineSeverity.Information
			});

		if (order.ApprovalDecisionAtUtc is { } decision)
		{
			var rejected = order.Status == SalesOrderStatus.Rejected;
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SalesApproval,
				EntityId = order.Id,
				DisplayNumber = order.OrderNumber,
				Title = rejected ? "Sales order rejected" : "Sales order approved",
				Status = rejected ? "Rejected" : "Approved",
				OccurredAt = decision,
				IsCorrection = rejected,
				Severity = rejected ? WorkflowTimelineSeverity.Warning : WorkflowTimelineSeverity.Success
			});
		}

		var reservationDates = await TryQueryAsync(
			"SELECT ir.CreatedAtUtc FROM InventoryReservations ir INNER JOIN SalesOrderLines sol ON sol.Id=ir.SalesOrderLineId WHERE sol.SalesOrderId=$OrderId ORDER BY ir.CreatedAtUtc,ir.Id;",
			reader => ReadUtc(reader, 0),
			cancellationToken,
			Parameter("$OrderId", salesOrderId));
		if (reservationDates.FirstOrDefault() is { } reservationAt && reservationAt != default)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.ReservationRelease,
				EntityId = order.Id,
				DisplayNumber = order.OrderNumber,
				Title = "Inventory reserved",
				Status = "Reserved",
				OccurredAt = reservationAt,
				Severity = WorkflowTimelineSeverity.Information
			});

		if (order.ReleasedAtUtc is { } released)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.ReservationRelease,
				EntityId = order.Id,
				DisplayNumber = order.OrderNumber,
				Title = "Sales order released",
				Status = "Released",
				OccurredAt = released,
				Severity = WorkflowTimelineSeverity.Success
			});

		var shipments = await TryQueryAsync(
			"SELECT Id,ShipmentNumber,ShipmentDate,Status,PostedAtUtc,ReversedAtUtc FROM Shipments WHERE SalesOrderId=$OrderId ORDER BY ShipmentDate,Id;",
			reader => new ShipmentSnapshot(
				reader.GetInt64(0),
				reader.GetString(1),
				ReadDateUtc(reader, 2),
				(ShipmentStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
				ReadNullableUtc(reader, 4),
				ReadNullableUtc(reader, 5)),
			cancellationToken,
			Parameter("$OrderId", salesOrderId));
		foreach (var shipment in shipments)
		{
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.Shipment,
				EntityId = shipment.Id,
				DisplayNumber = shipment.Number,
				Title = shipment.Status == ShipmentStatus.Posted ? "Shipment posted" : "Shipment",
				Status = shipment.Status.ToString(),
				OccurredAt = shipment.PostedAtUtc ?? shipment.DateUtc,
				Severity = shipment.Status == ShipmentStatus.Posted ? WorkflowTimelineSeverity.Success : WorkflowTimelineSeverity.Information
			});
			if (shipment.ReversedAtUtc is { } reversed)
				result.Add(new WorkflowTimelineItem
				{
					Kind = WorkflowTimelineKind.ShipmentReversal,
					EntityId = shipment.Id,
					DisplayNumber = shipment.Number,
					Title = "Shipment reversed",
					Status = "Reversed",
					OccurredAt = reversed,
					IsReversal = true,
					Severity = WorkflowTimelineSeverity.Error
				});
		}

		var invoices = await TryQueryAsync(
			"SELECT Id,InvoiceNumber,InvoiceDate,Status,PostedAtUtc FROM SalesInvoices WHERE SalesOrderId=$OrderId ORDER BY InvoiceDate,Id;",
			reader => new InvoiceSnapshot(
				reader.GetInt64(0),
				reader.GetString(1),
				ReadDateUtc(reader, 2),
				(SalesInvoiceStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
				ReadNullableUtc(reader, 4)),
			cancellationToken,
			Parameter("$OrderId", salesOrderId));
		foreach (var invoice in invoices)
		{
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SalesInvoice,
				EntityId = invoice.Id,
				DisplayNumber = invoice.Number,
				Title = invoice.Status == SalesInvoiceStatus.Posted ? "Sales invoice posted" : "Sales invoice",
				Status = invoice.Status.ToString(),
				OccurredAt = invoice.PostedAtUtc ?? invoice.DateUtc,
				IsCorrection = invoice.Status == SalesInvoiceStatus.Cancelled,
				Severity = invoice.Status == SalesInvoiceStatus.Cancelled ? WorkflowTimelineSeverity.Warning : invoice.Status == SalesInvoiceStatus.Posted ? WorkflowTimelineSeverity.Success : WorkflowTimelineSeverity.Information
			});
			await AppendReceivableAsync(result, invoice, cancellationToken);
		}

		var returns = await TryQueryAsync(
			"SELECT Id,ReturnNumber,ReturnDate,Status,PostedAtUtc FROM CustomerReturns WHERE SalesOrderId=$OrderId ORDER BY ReturnDate,Id;",
			reader => new CorrectionSnapshot(
				reader.GetInt64(0),
				reader.GetString(1),
				ReadDateUtc(reader, 2),
				((CustomerReturnStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)).ToString(),
				ReadNullableUtc(reader, 4)),
			cancellationToken,
			Parameter("$OrderId", salesOrderId));
		foreach (var value in returns)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.CustomerReturn,
				EntityId = value.Id,
				DisplayNumber = value.Number,
				Title = "Customer return",
				Status = value.Status,
				OccurredAt = value.PostedAtUtc ?? value.DateUtc,
				IsCorrection = true,
				Severity = WorkflowTimelineSeverity.Warning
			});

		var credits = await TryQueryAsync(
			"SELECT cn.Id,cn.CreditNoteNumber,cn.CreditDate,cn.Status,cn.PostedAtUtc FROM SalesCreditNotes cn INNER JOIN SalesInvoices si ON si.Id=cn.SalesInvoiceId WHERE si.SalesOrderId=$OrderId ORDER BY cn.CreditDate,cn.Id;",
			reader => new CorrectionSnapshot(
				reader.GetInt64(0),
				reader.GetString(1),
				ReadDateUtc(reader, 2),
				((SalesCreditNoteStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)).ToString(),
				ReadNullableUtc(reader, 4)),
			cancellationToken,
			Parameter("$OrderId", salesOrderId));
		foreach (var value in credits)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SalesCreditNote,
				EntityId = value.Id,
				DisplayNumber = value.Number,
				Title = "Sales credit note",
				Status = value.Status,
				OccurredAt = value.PostedAtUtc ?? value.DateUtc,
				IsCorrection = true,
				Severity = WorkflowTimelineSeverity.Warning
			});

		if (order.CancelledAtUtc is { } cancelled)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SalesOrder,
				EntityId = order.Id,
				DisplayNumber = order.OrderNumber,
				Title = "Sales order cancelled",
				Status = "Cancelled",
				OccurredAt = cancelled,
				IsCorrection = true,
				Severity = WorkflowTimelineSeverity.Warning
			});

		return result;
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListPurchaseAsync(long purchaseOrderId, CancellationToken cancellationToken)
	{
		var order = await Database.QuerySingleOrDefaultAsync(
			"SELECT Id,OrderNumber,OrderDate,Status FROM PurchaseOrders WHERE Id=$OrderId;",
			reader => new PurchaseOrderSnapshot(
				reader.GetInt64(0),
				reader.GetString(1),
				ReadDateUtc(reader, 2),
				(PurchaseOrderStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)),
			cancellationToken,
			Parameter("$OrderId", purchaseOrderId));
		if (order is null) return [];

		var result = new List<WorkflowTimelineItem>
		{
			new()
			{
				Kind = WorkflowTimelineKind.PurchaseOrder,
				EntityId = order.Id,
				DisplayNumber = order.Number,
				Title = "Purchase order created",
				Status = order.Status.ToString(),
				OccurredAt = order.DateUtc,
				Severity = WorkflowTimelineSeverity.Information
			}
		};

		var receipts = await TryQueryAsync(
			"SELECT Id,ReceiptNumber,ReceiptDate,ReversedAtUtc FROM GoodsReceipts WHERE PurchaseOrderId=$OrderId ORDER BY ReceiptDate,Id;",
			reader => new GoodsReceiptSnapshot(reader.GetInt64(0), reader.GetString(1), ReadDateUtc(reader, 2), ReadNullableUtc(reader, 3)),
			cancellationToken,
			Parameter("$OrderId", purchaseOrderId));
		foreach (var receipt in receipts)
		{
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.GoodsReceipt,
				EntityId = receipt.Id,
				DisplayNumber = receipt.Number,
				Title = "Goods receipt posted",
				Status = receipt.ReversedAtUtc is null ? "Posted" : "Reversed",
				OccurredAt = receipt.DateUtc,
				Severity = receipt.ReversedAtUtc is null ? WorkflowTimelineSeverity.Success : WorkflowTimelineSeverity.Warning
			});
			if (receipt.ReversedAtUtc is { } reversed)
				result.Add(new WorkflowTimelineItem
				{
					Kind = WorkflowTimelineKind.GoodsReceiptReversal,
					EntityId = receipt.Id,
					DisplayNumber = receipt.Number,
					Title = "Goods receipt reversed",
					Status = "Reversed",
					OccurredAt = reversed,
					IsReversal = true,
					Severity = WorkflowTimelineSeverity.Error
				});
		}

		var documents = await TryQueryAsync(
			"SELECT DISTINCT d.Id,d.Kind,d.SupplierDocumentNumber,d.DocumentDate,d.Status,d.CreatedAtUtc,d.SubmittedAtUtc,d.ApprovalDecisionAtUtc,d.PostedAtUtc,d.ReversedAtUtc FROM FinanceSupplierDocuments d INNER JOIN FinanceSupplierDocumentLines l ON l.DocumentId=d.Id LEFT JOIN PurchaseOrderLines pol ON pol.Id=l.PurchaseOrderLineId LEFT JOIN GoodsReceiptLines grl ON grl.Id=l.GoodsReceiptLineId LEFT JOIN PurchaseOrderLines polr ON polr.Id=grl.PurchaseOrderLineId WHERE pol.PurchaseOrderId=$OrderId OR polr.PurchaseOrderId=$OrderId ORDER BY d.DocumentDate,d.Id;",
			ReadSupplierDocument,
			cancellationToken,
			Parameter("$OrderId", purchaseOrderId));
		foreach (var document in documents) await AppendSupplierDocumentAsync(result, document, cancellationToken);

		return result;
	}

	public async Task<IReadOnlyList<long>> FindPurchaseOrderIdsForSupplierDocumentAsync(long documentId, CancellationToken cancellationToken)
	{
		try
		{
			return await Database.QueryAsync(
				"SELECT DISTINCT pol.PurchaseOrderId FROM FinanceSupplierDocumentLines l INNER JOIN PurchaseOrderLines pol ON pol.Id=l.PurchaseOrderLineId WHERE l.DocumentId=$DocumentId UNION SELECT DISTINCT pol.PurchaseOrderId FROM FinanceSupplierDocumentLines l INNER JOIN GoodsReceiptLines grl ON grl.Id=l.GoodsReceiptLineId INNER JOIN PurchaseOrderLines pol ON pol.Id=grl.PurchaseOrderLineId WHERE l.DocumentId=$DocumentId ORDER BY 1;",
				reader => reader.GetInt64(0),
				cancellationToken,
				Parameter("$DocumentId", documentId));
		}
		catch (DbException)
		{
			return [];
		}
	}

	public async Task<IReadOnlyList<WorkflowTimelineItem>> ListSupplierDocumentAsync(long documentId, CancellationToken cancellationToken)
	{
		var document = await Database.QuerySingleOrDefaultAsync(
			"SELECT Id,Kind,SupplierDocumentNumber,DocumentDate,Status,CreatedAtUtc,SubmittedAtUtc,ApprovalDecisionAtUtc,PostedAtUtc,ReversedAtUtc FROM FinanceSupplierDocuments WHERE Id=$DocumentId;",
			ReadSupplierDocument,
			cancellationToken,
			Parameter("$DocumentId", documentId));
		if (document is null) return [];
		var result = new List<WorkflowTimelineItem>();
		await AppendSupplierDocumentAsync(result, document, cancellationToken);
		return result;
	}

	private async Task AppendReceivableAsync(List<WorkflowTimelineItem> result, InvoiceSnapshot invoice, CancellationToken cancellationToken)
	{
		try
		{
			var openItems = await Database.QueryAsync(
				"SELECT Id,CreatedAtUtc,RemainingAmount,IsVoided FROM FinanceReceivableOpenItems WHERE SourceType=$SourceType AND SourceId=$SourceId ORDER BY Id;",
				reader => new OpenItemSnapshot(reader.GetInt64(0), ReadUtc(reader, 1), ReadDecimal(reader, 2), ReadBool(reader, 3)),
				cancellationToken,
				Parameter("$SourceType", FinanceReceivableSourceTypes.SalesInvoice),
				Parameter("$SourceId", invoice.Id.ToString(CultureInfo.InvariantCulture)));
			foreach (var openItem in openItems)
			{
				result.Add(new WorkflowTimelineItem
				{
					Kind = WorkflowTimelineKind.Receivable,
					EntityId = openItem.Id,
					DisplayNumber = invoice.Number,
					Title = "Receivable",
					Status = openItem.IsVoided ? "Voided" : openItem.RemainingAmount == 0m ? "Settled" : "Open",
					OccurredAt = openItem.CreatedAtUtc,
					Severity = openItem.RemainingAmount == 0m ? WorkflowTimelineSeverity.Success : WorkflowTimelineSeverity.Information
				});
				var payments = await Database.QueryAsync(
					"SELECT DISTINCT p.Id,p.CreatedAtUtc,p.Reference,p.IsReversed,p.ReversedAtUtc FROM FinanceReceivableAllocations a INNER JOIN FinanceReceivablePayments p ON p.OpenItemId=a.CreditOpenItemId WHERE a.DebitOpenItemId=$OpenItemId ORDER BY p.CreatedAtUtc,p.Id;",
					reader => new PaymentSnapshot(
						reader.GetInt64(0),
						ReadUtc(reader, 1),
						reader.IsDBNull(2) ? null : reader.GetString(2),
						ReadBool(reader, 3),
						ReadNullableUtc(reader, 4)),
					cancellationToken,
					Parameter("$OpenItemId", openItem.Id));
				foreach (var payment in payments)
				{
					var number = string.IsNullOrWhiteSpace(payment.Reference) ? $"PAY-{payment.Id:000000}" : payment.Reference!;
					result.Add(new WorkflowTimelineItem
					{
						Kind = WorkflowTimelineKind.ReceivablePayment,
						EntityId = payment.Id,
						DisplayNumber = number,
						Title = "Customer payment",
						Status = "Posted",
						OccurredAt = payment.CreatedAtUtc,
						Severity = WorkflowTimelineSeverity.Success
					});
					if (payment.ReversedAtUtc is { } reversed)
						result.Add(new WorkflowTimelineItem
						{
							Kind = WorkflowTimelineKind.ReceivablePaymentReversal,
							EntityId = payment.Id,
							DisplayNumber = number,
							Title = "Customer payment reversed",
							Status = "Reversed",
							OccurredAt = reversed,
							IsReversal = true,
							Severity = WorkflowTimelineSeverity.Error
						});
				}
			}
		}
		catch (DbException)
		{
			// Finance is an optional projection for sales-only test/database profiles.
		}
	}

	private async Task AppendSupplierDocumentAsync(List<WorkflowTimelineItem> result, SupplierDocumentSnapshot document, CancellationToken cancellationToken)
	{
		var isCredit = document.Kind == FinancePayableDocumentKind.CreditNote;
		var documentTitle = isCredit ? "Supplier credit note" : "Supplier invoice";
		result.Add(new WorkflowTimelineItem
		{
			Kind = WorkflowTimelineKind.SupplierInvoice,
			EntityId = document.Id,
			DisplayNumber = document.Number,
			Title = documentTitle,
			Status = document.Status.ToString(),
			OccurredAt = document.CreatedAtUtc,
			IsCorrection = isCredit,
			Severity = isCredit ? WorkflowTimelineSeverity.Warning : WorkflowTimelineSeverity.Information
		});

		var matches = await TryQueryAsync(
			"SELECT MatchStatus FROM FinanceSupplierDocumentLines WHERE DocumentId=$DocumentId ORDER BY LineNumber,Id;",
			reader => (FinancePayableMatchStatus)Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
			cancellationToken,
			Parameter("$DocumentId", document.Id));
		if (matches.Count > 0)
		{
			var hasException = matches.Any(value => value == FinancePayableMatchStatus.Exception);
			var hasMatch = matches.Any(value => value == FinancePayableMatchStatus.Matched);
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SupplierMatch,
				EntityId = document.Id,
				DisplayNumber = document.Number,
				Title = "Three-way match",
				Status = hasException ? "Exception" : hasMatch ? "Matched" : "Not Required",
				OccurredAt = document.SubmittedAtUtc ?? document.CreatedAtUtc,
				IsCorrection = hasException,
				Severity = hasException ? WorkflowTimelineSeverity.Warning : WorkflowTimelineSeverity.Success
			});
		}

		if (document.SubmittedAtUtc is { } submitted)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SupplierApproval,
				EntityId = document.Id,
				DisplayNumber = document.Number,
				Title = "Supplier document submitted",
				Status = "Pending Approval",
				OccurredAt = submitted,
				Severity = WorkflowTimelineSeverity.Information
			});

		if (document.ApprovalDecisionAtUtc is { } decision)
		{
			var rejected = document.Status == FinancePayableDocumentStatus.Rejected;
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SupplierApproval,
				EntityId = document.Id,
				DisplayNumber = document.Number,
				Title = rejected ? "Supplier document rejected" : "Supplier document approved",
				Status = rejected ? "Rejected" : "Approved",
				OccurredAt = decision,
				IsCorrection = rejected,
				Severity = rejected ? WorkflowTimelineSeverity.Warning : WorkflowTimelineSeverity.Success
			});
		}

		if (document.PostedAtUtc is { } posted)
		{
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SupplierInvoice,
				EntityId = document.Id,
				DisplayNumber = document.Number,
				Title = $"{documentTitle} posted",
				Status = "Posted",
				OccurredAt = posted,
				IsCorrection = isCredit,
				Severity = isCredit ? WorkflowTimelineSeverity.Warning : WorkflowTimelineSeverity.Success
			});
			await AppendPayableAsync(result, document, cancellationToken);
		}

		if (document.ReversedAtUtc is { } reversed)
			result.Add(new WorkflowTimelineItem
			{
				Kind = WorkflowTimelineKind.SupplierDocumentReversal,
				EntityId = document.Id,
				DisplayNumber = document.Number,
				Title = "Supplier document reversed",
				Status = "Reversed",
				OccurredAt = reversed,
				IsReversal = true,
				Severity = WorkflowTimelineSeverity.Error
			});
	}

	private async Task AppendPayableAsync(List<WorkflowTimelineItem> result, SupplierDocumentSnapshot document, CancellationToken cancellationToken)
	{
		try
		{
			var sourceType = document.Kind == FinancePayableDocumentKind.CreditNote
				? FinancePayableSourceTypes.SupplierCreditNote
				: FinancePayableSourceTypes.SupplierInvoice;
			var openItems = await Database.QueryAsync(
				"SELECT Id,CreatedAtUtc,RemainingAmount,IsVoided FROM FinancePayableOpenItems WHERE SourceType=$SourceType AND SourceId=$SourceId ORDER BY Id;",
				reader => new OpenItemSnapshot(reader.GetInt64(0), ReadUtc(reader, 1), ReadDecimal(reader, 2), ReadBool(reader, 3)),
				cancellationToken,
				Parameter("$SourceType", sourceType),
				Parameter("$SourceId", document.Id.ToString(CultureInfo.InvariantCulture)));
			foreach (var openItem in openItems)
			{
				result.Add(new WorkflowTimelineItem
				{
					Kind = WorkflowTimelineKind.Payable,
					EntityId = openItem.Id,
					DisplayNumber = document.Number,
					Title = "Payable",
					Status = openItem.IsVoided ? "Voided" : openItem.RemainingAmount == 0m ? "Settled" : "Open",
					OccurredAt = openItem.CreatedAtUtc,
					Severity = openItem.RemainingAmount == 0m ? WorkflowTimelineSeverity.Success : WorkflowTimelineSeverity.Information
				});
				var payments = await Database.QueryAsync(
					"SELECT DISTINCT p.Id,p.CreatedAtUtc,p.Reference,p.IsReversed,p.ReversedAtUtc FROM FinancePayableAllocations a INNER JOIN FinancePayablePayments p ON p.OpenItemId=a.DebitOpenItemId WHERE a.CreditOpenItemId=$OpenItemId ORDER BY p.CreatedAtUtc,p.Id;",
					reader => new PaymentSnapshot(
						reader.GetInt64(0),
						ReadUtc(reader, 1),
						reader.IsDBNull(2) ? null : reader.GetString(2),
						ReadBool(reader, 3),
						ReadNullableUtc(reader, 4)),
					cancellationToken,
					Parameter("$OpenItemId", openItem.Id));
				foreach (var payment in payments)
				{
					var number = string.IsNullOrWhiteSpace(payment.Reference) ? $"PAY-{payment.Id:000000}" : payment.Reference!;
					result.Add(new WorkflowTimelineItem
					{
						Kind = WorkflowTimelineKind.SupplierPayment,
						EntityId = payment.Id,
						DisplayNumber = number,
						Title = "Supplier payment",
						Status = "Posted",
						OccurredAt = payment.CreatedAtUtc,
						Severity = WorkflowTimelineSeverity.Success
					});
					if (payment.ReversedAtUtc is { } reversed)
						result.Add(new WorkflowTimelineItem
						{
							Kind = WorkflowTimelineKind.SupplierPaymentReversal,
							EntityId = payment.Id,
							DisplayNumber = number,
							Title = "Supplier payment reversed",
							Status = "Reversed",
							OccurredAt = reversed,
							IsReversal = true,
							Severity = WorkflowTimelineSeverity.Error
						});
				}
			}
		}
		catch (DbException)
		{
			// Accounts Payable projections may be absent in procurement-only test/database profiles.
		}
	}

	private async Task<IReadOnlyList<T>> TryQueryAsync<T>(
		string sql,
		Func<DbDataReader, T> reader,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		try { return await Database.QueryAsync(sql, reader, cancellationToken, parameters); }
		catch (DbException) { return []; }
	}

	private static SupplierDocumentSnapshot ReadSupplierDocument(DbDataReader reader) => new(
		reader.GetInt64(0),
		(FinancePayableDocumentKind)Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
		reader.GetString(2),
		ReadDateUtc(reader, 3),
		(FinancePayableDocumentStatus)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
		ReadUtc(reader, 5),
		ReadNullableUtc(reader, 6),
		ReadNullableUtc(reader, 7),
		ReadNullableUtc(reader, 8),
		ReadNullableUtc(reader, 9));

	private static DateTime ReadDateUtc(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		var date = value is DateTime dateTime
			? dateTime.Date
			: DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces).Date;
		return DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime();
	}

	private static DateTime ReadUtc(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		if (value is DateTime dateTime)
		{
			if (dateTime.Kind == DateTimeKind.Utc) return dateTime;
			if (dateTime.Kind == DateTimeKind.Local) return dateTime.ToUniversalTime();
			return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
		}
		var parsed = DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		return parsed.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : parsed.ToUniversalTime();
	}

	private static DateTime? ReadNullableUtc(DbDataReader reader, int ordinal) =>
		reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);

	private static bool ReadBool(DbDataReader reader, int ordinal) =>
		Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

	private static decimal ReadDecimal(DbDataReader reader, int ordinal) =>
		Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

	private sealed record SalesOrderSnapshot(long Id, string OrderNumber, DateTime OrderDateUtc, SalesOrderStatus Status, DateTime? SubmittedAtUtc, DateTime? ApprovalDecisionAtUtc, DateTime? ReleasedAtUtc, DateTime? CancelledAtUtc);
	private sealed record ShipmentSnapshot(long Id, string Number, DateTime DateUtc, ShipmentStatus Status, DateTime? PostedAtUtc, DateTime? ReversedAtUtc);
	private sealed record InvoiceSnapshot(long Id, string Number, DateTime DateUtc, SalesInvoiceStatus Status, DateTime? PostedAtUtc);
	private sealed record CorrectionSnapshot(long Id, string Number, DateTime DateUtc, string Status, DateTime? PostedAtUtc);
	private sealed record PurchaseOrderSnapshot(long Id, string Number, DateTime DateUtc, PurchaseOrderStatus Status);
	private sealed record GoodsReceiptSnapshot(long Id, string Number, DateTime DateUtc, DateTime? ReversedAtUtc);
	private sealed record SupplierDocumentSnapshot(long Id, FinancePayableDocumentKind Kind, string Number, DateTime DateUtc, FinancePayableDocumentStatus Status, DateTime CreatedAtUtc, DateTime? SubmittedAtUtc, DateTime? ApprovalDecisionAtUtc, DateTime? PostedAtUtc, DateTime? ReversedAtUtc);
	private sealed record OpenItemSnapshot(long Id, DateTime CreatedAtUtc, decimal RemainingAmount, bool IsVoided);
	private sealed record PaymentSnapshot(long Id, DateTime CreatedAtUtc, string? Reference, bool IsReversed, DateTime? ReversedAtUtc);
}
