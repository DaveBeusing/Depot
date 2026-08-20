// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesTimelineRepository : DatabaseRepository
{
	public SalesTimelineRepository(DatabaseAccess database) : base(database) { }

	public async Task<IReadOnlyList<SalesOrderTimelineItem>> ListAsync(SalesOrder order, CancellationToken token)
	{
		var values = new List<SalesOrderTimelineItem>
		{
			new() { TimestampUtc = DateTime.SpecifyKind(order.OrderDate.Date, DateTimeKind.Local).ToUniversalTime(), EventType="Created", Title="Sales order created", Reference=order.OrderNumber }
		};
		if(order.SubmittedAtUtc is { } submitted) values.Add(new(){TimestampUtc=submitted,EventType="Submitted",Title="Submitted for approval",Reference=order.OrderNumber});
		if(order.ApprovalDecisionAtUtc is { } decided) values.Add(new(){TimestampUtc=decided,EventType=order.Status==SalesOrderStatus.Rejected?"Rejected":"Approved",Title=order.Status==SalesOrderStatus.Rejected?"Sales order rejected":"Approval decision recorded",Details=order.ApprovalComment,Reference=order.OrderNumber});
		if(order.ReleasedAtUtc is { } released) values.Add(new(){TimestampUtc=released,EventType="Released",Title="Released for fulfillment",Reference=order.OrderNumber});
		if(order.CancelledAtUtc is { } cancelled) values.Add(new(){TimestampUtc=cancelled,EventType="Cancelled",Title="Sales order cancelled",Details=order.CancelReason,Reference=order.OrderNumber});

		var shipments = await Database.QueryAsync("SELECT ShipmentNumber,PostedAtUtc,ReversedAtUtc FROM Shipments WHERE SalesOrderId=$OrderId;", ReadShipment, token, Parameter("$OrderId", order.Id));
		foreach(var shipment in shipments)
		{
			if(shipment.PostedAtUtc is { } posted) values.Add(new(){TimestampUtc=posted,EventType="Shipment",Title=$"Shipment {shipment.Number} posted",Reference=shipment.Number});
			if(shipment.ReversedAtUtc is { } reversed) values.Add(new(){TimestampUtc=reversed,EventType="Shipment reversal",Title=$"Shipment {shipment.Number} reversed",Reference=shipment.Number});
		}
		var invoices = await Database.QueryAsync("SELECT InvoiceNumber,PostedAtUtc FROM SalesInvoices WHERE SalesOrderId=$OrderId AND PostedAtUtc IS NOT NULL;", ReadDocument, token, Parameter("$OrderId", order.Id));
		foreach(var invoice in invoices) values.Add(new(){TimestampUtc=invoice.At,EventType="Invoice",Title=$"Invoice {invoice.Number} posted",Reference=invoice.Number});
		var returns = await Database.QueryAsync("SELECT cr.ReturnNumber,cr.PostedAtUtc FROM CustomerReturns cr INNER JOIN Shipments sh ON sh.Id=cr.ShipmentId WHERE sh.SalesOrderId=$OrderId AND cr.PostedAtUtc IS NOT NULL;", ReadDocument, token, Parameter("$OrderId", order.Id));
		foreach(var item in returns) values.Add(new(){TimestampUtc=item.At,EventType="Return",Title=$"Customer return {item.Number} posted",Reference=item.Number});
		var credits = await Database.QueryAsync("SELECT cn.CreditNoteNumber,cn.PostedAtUtc FROM SalesCreditNotes cn INNER JOIN SalesInvoices si ON si.Id=cn.SalesInvoiceId WHERE si.SalesOrderId=$OrderId AND cn.PostedAtUtc IS NOT NULL;", ReadDocument, token, Parameter("$OrderId", order.Id));
		foreach(var item in credits) values.Add(new(){TimestampUtc=item.At,EventType="Credit note",Title=$"Credit note {item.Number} posted",Reference=item.Number});
		return values.OrderByDescending(value=>value.TimestampUtc).ToArray();
	}

	private static TimelineShipment ReadShipment(DbDataReader r)=>new(r.GetString(0),ParseUtc(r,1),ParseUtc(r,2));
	private static TimelineDocument ReadDocument(DbDataReader r)=>new(r.GetString(0),ParseUtc(r,1)??DateTime.MinValue);
	private static DateTime? ParseUtc(DbDataReader r,int index)=>r.IsDBNull(index)?null:DateTime.Parse(r.GetString(index),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
	private sealed record TimelineShipment(string Number,DateTime? PostedAtUtc,DateTime? ReversedAtUtc);
	private sealed record TimelineDocument(string Number,DateTime At);
}
