// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ProcurementSourcingRepository : DatabaseRepository
{
	private const string RequisitionColumns = "pr.Id, pr.RequisitionNumber, pr.Status, pr.RequestedByUserId, requester.DisplayName, pr.CreatedByUserId, creator.DisplayName, pr.CreatedAtUtc, pr.SubmittedAtUtc, pr.ApprovalDecisionAtUtc, pr.ApprovalDecisionByUserId, approver.DisplayName, pr.ApprovalComment, pr.RequiredByDate, pr.PreferredSupplierId, preferred.Name, pr.BusinessJustification, pr.Version";
	private const string RequisitionFrom = "FROM PurchaseRequisitions pr INNER JOIN Users requester ON requester.Id=pr.RequestedByUserId INNER JOIN Users creator ON creator.Id=pr.CreatedByUserId LEFT JOIN Users approver ON approver.Id=pr.ApprovalDecisionByUserId LEFT JOIN Suppliers preferred ON preferred.Id=pr.PreferredSupplierId";
	private const string QuoteLineSql = "SELECT ql.Id,ql.SupplierQuoteResponseId,ql.RequestForQuotationLineId,ql.ItemId,i.PartNumber,i.Description,ql.Quantity,ql.UnitPrice,ql.MinimumOrderQuantity,ql.LeadTimeDays,ql.Version FROM SupplierQuoteResponseLines ql INNER JOIN Items i ON i.Id=ql.ItemId WHERE ql.SupplierQuoteResponseId=$Id ORDER BY ql.RequestForQuotationLineId,ql.Id;";

	public ProcurementSourcingRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<PurchaseRequisition>> SearchRequisitionsAsync(string? searchText, PurchaseRequisitionStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			predicates.Add("(pr.RequisitionNumber LIKE $Search OR requester.DisplayName LIKE $Search OR pr.BusinessJustification LIKE $Search OR preferred.Name LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (status is not null)
		{
			predicates.Add("pr.Status=$Status");
			parameters.Add(Parameter("$Status", (int)status.Value));
		}
		var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
		return Database.QueryPageAsync($"SELECT {RequisitionColumns} {RequisitionFrom} {where} ORDER BY pr.CreatedAtUtc DESC, pr.Id DESC", $"SELECT COUNT(*) {RequisitionFrom} {where}", ReadRequisition, pageNumber, pageSize, cancellationToken, parameters.ToArray());
	}

	public async Task<PurchaseRequisition?> GetRequisitionAsync(long id, CancellationToken cancellationToken)
	{
		var value = await Database.QuerySingleOrDefaultAsync($"SELECT {RequisitionColumns} {RequisitionFrom} WHERE pr.Id=$Id;", ReadRequisition, cancellationToken, Parameter("$Id", id));
		if (value is not null) value.Lines = await ListRequisitionLinesAsync(id, cancellationToken);
		return value;
	}

	internal async Task<PurchaseRequisition?> GetRequisitionAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {RequisitionColumns} {RequisitionFrom} WHERE pr.Id=$Id;", ReadRequisition, cancellationToken, Parameter("$Id", id));
		if (value is not null) value.Lines = await ListRequisitionLinesAsync(transaction, id, cancellationToken);
		return value;
	}

	public Task<IReadOnlyList<PurchaseRequisitionLine>> ListRequisitionLinesAsync(long id, CancellationToken cancellationToken) =>
		Database.QueryAsync("SELECT prl.Id,prl.PurchaseRequisitionId,prl.LineNumber,prl.ItemId,i.PartNumber,i.Description,prl.Quantity,prl.Notes,prl.Version FROM PurchaseRequisitionLines prl INNER JOIN Items i ON i.Id=prl.ItemId WHERE prl.PurchaseRequisitionId=$Id ORDER BY prl.LineNumber,prl.Id;", ReadRequisitionLine, cancellationToken, Parameter("$Id", id));

	private static Task<IReadOnlyList<PurchaseRequisitionLine>> ListRequisitionLinesAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT prl.Id,prl.PurchaseRequisitionId,prl.LineNumber,prl.ItemId,i.PartNumber,i.Description,prl.Quantity,prl.Notes,prl.Version FROM PurchaseRequisitionLines prl INNER JOIN Items i ON i.Id=prl.ItemId WHERE prl.PurchaseRequisitionId=$Id ORDER BY prl.LineNumber,prl.Id;", ReadRequisitionLine, cancellationToken, Parameter("$Id", id));

	public async Task<PurchaseRequisition> SaveRequisitionAsync(DatabaseTransactionContext transaction, PurchaseRequisition value, CancellationToken cancellationToken)
	{
		if (value.Id == 0)
		{
			var temporary = $"PENDING-{Guid.NewGuid():N}";
			value.Id = await transaction.Session.InsertAsync(
				"INSERT INTO PurchaseRequisitions(RequisitionNumber,Status,RequestedByUserId,CreatedByUserId,CreatedAtUtc,RequiredByDate,PreferredSupplierId,BusinessJustification) VALUES($Number,$Status,$Requester,$Creator,$Created,$Required,$Supplier,$Justification);",
				cancellationToken, Parameter("$Number", temporary), Parameter("$Status", (int)PurchaseRequisitionStatus.Draft), Parameter("$Requester", value.RequestedByUserId), Parameter("$Creator", value.CreatedByUserId), Parameter("$Created", Utc(value.CreatedAtUtc)), Parameter("$Required", NullableDate(value.RequiredByDate)), Parameter("$Supplier", value.PreferredSupplierId), Parameter("$Justification", value.BusinessJustification));
			value.RequisitionNumber = $"PR-{value.Id:000000}";
			await transaction.Session.ExecuteAsync("UPDATE PurchaseRequisitions SET RequisitionNumber=$Number WHERE Id=$Id;", cancellationToken, Parameter("$Number", value.RequisitionNumber), Parameter("$Id", value.Id));
		}
		else
		{
			var updated = await transaction.Session.ExecuteAsync(
				"UPDATE PurchaseRequisitions SET RequestedByUserId=$Requester,RequiredByDate=$Required,PreferredSupplierId=$Supplier,BusinessJustification=$Justification,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status IN ($Draft,$Returned);",
				cancellationToken, Parameter("$Requester", value.RequestedByUserId), Parameter("$Required", NullableDate(value.RequiredByDate)), Parameter("$Supplier", value.PreferredSupplierId), Parameter("$Justification", value.BusinessJustification), Parameter("$Id", value.Id), Parameter("$Version", value.Version), Parameter("$Draft", (int)PurchaseRequisitionStatus.Draft), Parameter("$Returned", (int)PurchaseRequisitionStatus.Returned));
			if (updated != 1) throw new Services.ConcurrencyConflictException("purchase requisition");
			value.Version++;
		}

		await transaction.Session.ExecuteAsync("DELETE FROM PurchaseRequisitionLines WHERE PurchaseRequisitionId=$Id;", cancellationToken, Parameter("$Id", value.Id));
		var lineNumber = 1;
		foreach (var line in value.Lines)
		{
			line.PurchaseRequisitionId = value.Id;
			line.LineNumber = lineNumber++;
			line.Id = await transaction.Session.InsertAsync("INSERT INTO PurchaseRequisitionLines(PurchaseRequisitionId,LineNumber,ItemId,Quantity,Notes) VALUES($Header,$Line,$Item,$Quantity,$Notes);", cancellationToken, Parameter("$Header", value.Id), Parameter("$Line", line.LineNumber), Parameter("$Item", line.ItemId), Parameter("$Quantity", line.Quantity), Parameter("$Notes", line.Notes));
			line.Version = 1;
		}
		return value;
	}

	public async Task<bool> SetRequisitionStatusAsync(DatabaseTransactionContext transaction, long id, long version, PurchaseRequisitionStatus expected, PurchaseRequisitionStatus status, long? decisionUserId, DateTime? submittedAtUtc, DateTime? decisionAtUtc, string? comment, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE PurchaseRequisitions SET Status=$Status,SubmittedAtUtc=COALESCE($Submitted,SubmittedAtUtc),ApprovalDecisionByUserId=$DecisionUser,ApprovalDecisionAtUtc=$DecisionAt,ApprovalComment=$Comment,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Expected;",
			cancellationToken, Parameter("$Status", (int)status), Parameter("$Submitted", submittedAtUtc is null ? null : Utc(submittedAtUtc.Value)), Parameter("$DecisionUser", decisionUserId), Parameter("$DecisionAt", decisionAtUtc is null ? null : Utc(decisionAtUtc.Value)), Parameter("$Comment", comment), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Expected", (int)expected)) == 1;

	public Task<PageResult<RequestForQuotation>> SearchRfqsAsync(RequestForQuotationStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var where = status is null ? string.Empty : "WHERE r.Status=$Status";
		var parameters = status is null ? Array.Empty<DatabaseParameter>() : [Parameter("$Status", (int)status.Value)];
		return Database.QueryPageAsync($"SELECT r.Id,r.RfqNumber,r.PurchaseRequisitionId,pr.RequisitionNumber,r.Status,r.CreatedAtUtc,r.CreatedByUserId,r.ResponseDueDate,r.SelectedQuoteResponseId,r.SelectedByUserId,r.SelectedAtUtc,r.ConvertedPurchaseOrderId,r.Version FROM RequestsForQuotation r INNER JOIN PurchaseRequisitions pr ON pr.Id=r.PurchaseRequisitionId {where} ORDER BY r.CreatedAtUtc DESC,r.Id DESC", $"SELECT COUNT(*) FROM RequestsForQuotation r {where}", ReadRfq, pageNumber, pageSize, cancellationToken, parameters);
	}

	public async Task<RequestForQuotation?> GetRfqAsync(long id, CancellationToken cancellationToken)
	{
		var value = await Database.QuerySingleOrDefaultAsync("SELECT r.Id,r.RfqNumber,r.PurchaseRequisitionId,pr.RequisitionNumber,r.Status,r.CreatedAtUtc,r.CreatedByUserId,r.ResponseDueDate,r.SelectedQuoteResponseId,r.SelectedByUserId,r.SelectedAtUtc,r.ConvertedPurchaseOrderId,r.Version FROM RequestsForQuotation r INNER JOIN PurchaseRequisitions pr ON pr.Id=r.PurchaseRequisitionId WHERE r.Id=$Id;", ReadRfq, cancellationToken, Parameter("$Id", id));
		if (value is not null) { value.Suppliers = await ListRfqSuppliersAsync(id, cancellationToken); value.Lines = await ListRfqLinesAsync(id, cancellationToken); }
		return value;
	}

	internal async Task<RequestForQuotation?> GetRfqAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.QuerySingleOrDefaultAsync("SELECT r.Id,r.RfqNumber,r.PurchaseRequisitionId,pr.RequisitionNumber,r.Status,r.CreatedAtUtc,r.CreatedByUserId,r.ResponseDueDate,r.SelectedQuoteResponseId,r.SelectedByUserId,r.SelectedAtUtc,r.ConvertedPurchaseOrderId,r.Version FROM RequestsForQuotation r INNER JOIN PurchaseRequisitions pr ON pr.Id=r.PurchaseRequisitionId WHERE r.Id=$Id;", ReadRfq, cancellationToken, Parameter("$Id", id));
		if (value is not null) { value.Suppliers = await ListRfqSuppliersAsync(transaction, id, cancellationToken); value.Lines = await ListRfqLinesAsync(transaction, id, cancellationToken); }
		return value;
	}

	public async Task<RequestForQuotation> CreateRfqAsync(DatabaseTransactionContext transaction, PurchaseRequisition requisition, IReadOnlyList<long> supplierIds, DateTime? responseDueDate, long createdByUserId, CancellationToken cancellationToken)
	{
		var value = new RequestForQuotation { PurchaseRequisitionId = requisition.Id, RequisitionNumber = requisition.RequisitionNumber, Status = RequestForQuotationStatus.Open, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = createdByUserId, ResponseDueDate = responseDueDate, Version = 1 };
		var temporary = $"PENDING-{Guid.NewGuid():N}";
		value.Id = await transaction.Session.InsertAsync("INSERT INTO RequestsForQuotation(RfqNumber,PurchaseRequisitionId,Status,CreatedAtUtc,CreatedByUserId,ResponseDueDate) VALUES($Number,$Req,$Status,$Created,$User,$Due);", cancellationToken, Parameter("$Number", temporary), Parameter("$Req", requisition.Id), Parameter("$Status", (int)value.Status), Parameter("$Created", Utc(value.CreatedAtUtc)), Parameter("$User", createdByUserId), Parameter("$Due", NullableDate(responseDueDate)));
		value.RfqNumber = $"RFQ-{value.Id:000000}";
		await transaction.Session.ExecuteAsync("UPDATE RequestsForQuotation SET RfqNumber=$Number WHERE Id=$Id;", cancellationToken, Parameter("$Number", value.RfqNumber), Parameter("$Id", value.Id));
		foreach (var supplierId in supplierIds.Distinct().Order()) await transaction.Session.ExecuteAsync("INSERT INTO RequestForQuotationSuppliers(RequestForQuotationId,SupplierId) VALUES($Rfq,$Supplier);", cancellationToken, Parameter("$Rfq", value.Id), Parameter("$Supplier", supplierId));
		foreach (var line in requisition.Lines.OrderBy(x => x.LineNumber))
			await transaction.Session.InsertAsync("INSERT INTO RequestForQuotationLines(RequestForQuotationId,PurchaseRequisitionLineId,ItemId,Quantity,RequiredByDate) VALUES($Rfq,$ReqLine,$Item,$Quantity,$Required);", cancellationToken, Parameter("$Rfq", value.Id), Parameter("$ReqLine", line.Id), Parameter("$Item", line.ItemId), Parameter("$Quantity", line.Quantity), Parameter("$Required", NullableDate(requisition.RequiredByDate)));
		return value;
	}

	public Task<IReadOnlyList<RequestForQuotationSupplier>> ListRfqSuppliersAsync(long rfqId, CancellationToken cancellationToken) =>
		Database.QueryAsync("SELECT rs.RequestForQuotationId,rs.SupplierId,s.Name FROM RequestForQuotationSuppliers rs INNER JOIN Suppliers s ON s.Id=rs.SupplierId WHERE rs.RequestForQuotationId=$Id ORDER BY s.Name,s.Id;", ReadRfqSupplier, cancellationToken, Parameter("$Id", rfqId));

	private static Task<IReadOnlyList<RequestForQuotationSupplier>> ListRfqSuppliersAsync(DatabaseTransactionContext transaction, long rfqId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT rs.RequestForQuotationId,rs.SupplierId,s.Name FROM RequestForQuotationSuppliers rs INNER JOIN Suppliers s ON s.Id=rs.SupplierId WHERE rs.RequestForQuotationId=$Id ORDER BY s.Name,s.Id;", ReadRfqSupplier, cancellationToken, Parameter("$Id", rfqId));

	public Task<IReadOnlyList<RequestForQuotationLine>> ListRfqLinesAsync(long rfqId, CancellationToken cancellationToken) =>
		Database.QueryAsync("SELECT rl.Id,rl.RequestForQuotationId,rl.PurchaseRequisitionLineId,rl.ItemId,i.PartNumber,i.Description,rl.Quantity,rl.RequiredByDate,rl.Version FROM RequestForQuotationLines rl INNER JOIN Items i ON i.Id=rl.ItemId WHERE rl.RequestForQuotationId=$Id ORDER BY rl.Id;", ReadRfqLine, cancellationToken, Parameter("$Id", rfqId));

	private static Task<IReadOnlyList<RequestForQuotationLine>> ListRfqLinesAsync(DatabaseTransactionContext transaction, long rfqId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT rl.Id,rl.RequestForQuotationId,rl.PurchaseRequisitionLineId,rl.ItemId,i.PartNumber,i.Description,rl.Quantity,rl.RequiredByDate,rl.Version FROM RequestForQuotationLines rl INNER JOIN Items i ON i.Id=rl.ItemId WHERE rl.RequestForQuotationId=$Id ORDER BY rl.Id;", ReadRfqLine, cancellationToken, Parameter("$Id", rfqId));

	public async Task<SupplierQuoteResponse> CreateQuoteResponseAsync(DatabaseTransactionContext transaction, SupplierQuoteResponse value, CancellationToken cancellationToken)
	{
		value.Id = await transaction.Session.InsertAsync("INSERT INTO SupplierQuoteResponses(RequestForQuotationId,SupplierId,SupplierReference,Currency,ValidUntil,ReceivedAtUtc,CapturedByUserId,Status) VALUES($Rfq,$Supplier,$Reference,$Currency,$ValidUntil,$Received,$User,$Status);", cancellationToken, Parameter("$Rfq", value.RequestForQuotationId), Parameter("$Supplier", value.SupplierId), Parameter("$Reference", value.SupplierReference), Parameter("$Currency", value.Currency), Parameter("$ValidUntil", NullableDate(value.ValidUntil)), Parameter("$Received", Utc(value.ReceivedAtUtc)), Parameter("$User", value.CapturedByUserId), Parameter("$Status", (int)value.Status));
		foreach (var line in value.Lines.OrderBy(x => x.RequestForQuotationLineId))
		{
			line.SupplierQuoteResponseId = value.Id;
			line.Id = await transaction.Session.InsertAsync("INSERT INTO SupplierQuoteResponseLines(SupplierQuoteResponseId,RequestForQuotationLineId,ItemId,Quantity,UnitPrice,MinimumOrderQuantity,LeadTimeDays) VALUES($Response,$RfqLine,$Item,$Quantity,$Price,$Moq,$Lead);", cancellationToken, Parameter("$Response", value.Id), Parameter("$RfqLine", line.RequestForQuotationLineId), Parameter("$Item", line.ItemId), Parameter("$Quantity", line.Quantity), Parameter("$Price", line.UnitPrice), Parameter("$Moq", line.MinimumOrderQuantity), Parameter("$Lead", line.LeadTimeDays));
		}
		return value;
	}

	public Task<IReadOnlyList<SupplierQuoteResponse>> ListQuoteResponsesAsync(long rfqId, CancellationToken cancellationToken) =>
		Database.QueryAsync("SELECT q.Id,q.RequestForQuotationId,q.SupplierId,s.Name,q.SupplierReference,q.Currency,q.ValidUntil,q.ReceivedAtUtc,q.CapturedByUserId,q.Status,q.Version FROM SupplierQuoteResponses q INNER JOIN Suppliers s ON s.Id=q.SupplierId WHERE q.RequestForQuotationId=$Id ORDER BY s.Name,q.ReceivedAtUtc,q.Id;", ReadQuoteResponse, cancellationToken, Parameter("$Id", rfqId));

	public async Task<SupplierQuoteResponse?> GetQuoteResponseAsync(long id, CancellationToken cancellationToken)
	{
		var value = await Database.QuerySingleOrDefaultAsync("SELECT q.Id,q.RequestForQuotationId,q.SupplierId,s.Name,q.SupplierReference,q.Currency,q.ValidUntil,q.ReceivedAtUtc,q.CapturedByUserId,q.Status,q.Version FROM SupplierQuoteResponses q INNER JOIN Suppliers s ON s.Id=q.SupplierId WHERE q.Id=$Id;", ReadQuoteResponse, cancellationToken, Parameter("$Id", id));
		if (value is not null) value.Lines = await Database.QueryAsync(QuoteLineSql, ReadQuoteLine, cancellationToken, Parameter("$Id", id));
		return value;
	}

	internal async Task<SupplierQuoteResponse?> GetQuoteResponseAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.QuerySingleOrDefaultAsync("SELECT q.Id,q.RequestForQuotationId,q.SupplierId,s.Name,q.SupplierReference,q.Currency,q.ValidUntil,q.ReceivedAtUtc,q.CapturedByUserId,q.Status,q.Version FROM SupplierQuoteResponses q INNER JOIN Suppliers s ON s.Id=q.SupplierId WHERE q.Id=$Id;", ReadQuoteResponse, cancellationToken, Parameter("$Id", id));
		if (value is not null) value.Lines = await transaction.Session.QueryAsync(QuoteLineSql, ReadQuoteLine, cancellationToken, Parameter("$Id", id));
		return value;
	}

	public async Task<bool> SetRfqStatusAsync(DatabaseTransactionContext transaction, long rfqId, long version, RequestForQuotationStatus expected, RequestForQuotationStatus status, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE RequestsForQuotation SET Status=$Status,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Expected AND ConvertedPurchaseOrderId IS NULL;",
			cancellationToken, Parameter("$Status",(int)status), Parameter("$Id",rfqId), Parameter("$Version",version), Parameter("$Expected",(int)expected)) == 1;

	public async Task<bool> SelectQuoteAsync(DatabaseTransactionContext transaction, long rfqId, long version, long quoteId, long userId, DateTime atUtc, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync("UPDATE RequestsForQuotation SET SelectedQuoteResponseId=$Quote,SelectedByUserId=$User,SelectedAtUtc=$At,Status=$Awarded,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Open AND ConvertedPurchaseOrderId IS NULL;", cancellationToken, Parameter("$Quote", quoteId), Parameter("$User", userId), Parameter("$At", Utc(atUtc)), Parameter("$Awarded", (int)RequestForQuotationStatus.Awarded), Parameter("$Id", rfqId), Parameter("$Version", version), Parameter("$Open", (int)RequestForQuotationStatus.Open)) == 1;

	public async Task<bool> CompleteConversionAsync(DatabaseTransactionContext transaction, RequestForQuotation rfq, long purchaseOrderId, long userId, DateTime atUtc, CancellationToken cancellationToken)
	{
		if (rfq.SelectedQuoteResponseId is null) throw new InvalidOperationException("A supplier quote must be selected before conversion.");
		var changed = await transaction.Session.ExecuteAsync("UPDATE RequestsForQuotation SET ConvertedPurchaseOrderId=$Order,Status=$Converted,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Awarded AND ConvertedPurchaseOrderId IS NULL;", cancellationToken, Parameter("$Order", purchaseOrderId), Parameter("$Converted", (int)RequestForQuotationStatus.Converted), Parameter("$Id", rfq.Id), Parameter("$Version", rfq.Version), Parameter("$Awarded", (int)RequestForQuotationStatus.Awarded));
		if (changed != 1) return false;
		await transaction.Session.ExecuteAsync("INSERT INTO ProcurementSourcingEvidence(PurchaseOrderId,PurchaseRequisitionId,RequestForQuotationId,SupplierQuoteResponseId,SelectedByUserId,SelectedAtUtc) VALUES($Order,$Req,$Rfq,$Quote,$User,$At);", cancellationToken, Parameter("$Order", purchaseOrderId), Parameter("$Req", rfq.PurchaseRequisitionId), Parameter("$Rfq", rfq.Id), Parameter("$Quote", rfq.SelectedQuoteResponseId.Value), Parameter("$User", userId), Parameter("$At", Utc(atUtc)));
		await transaction.Session.ExecuteAsync("UPDATE PurchaseRequisitions SET Status=$Converted,Version=Version+1 WHERE Id=$Id AND Status=$Approved;", cancellationToken, Parameter("$Converted", (int)PurchaseRequisitionStatus.Converted), Parameter("$Id", rfq.PurchaseRequisitionId), Parameter("$Approved", (int)PurchaseRequisitionStatus.Approved));
		return true;
	}

	public Task<ProcurementSourcingEvidence?> GetEvidenceByPurchaseOrderAsync(long purchaseOrderId, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync("SELECT PurchaseOrderId,PurchaseRequisitionId,RequestForQuotationId,SupplierQuoteResponseId,SelectedByUserId,SelectedAtUtc FROM ProcurementSourcingEvidence WHERE PurchaseOrderId=$Id;", ReadEvidence, cancellationToken, Parameter("$Id", purchaseOrderId));

	private static PurchaseRequisition ReadRequisition(DbDataReader r) => new() { Id=r.GetInt64(0),RequisitionNumber=r.GetString(1),Status=(PurchaseRequisitionStatus)r.GetInt32(2),RequestedByUserId=r.GetInt64(3),RequestedByUserDisplay=r.IsDBNull(4)?null:r.GetString(4),CreatedByUserId=r.GetInt64(5),CreatedByUserDisplay=r.IsDBNull(6)?null:r.GetString(6),CreatedAtUtc=ParseUtc(r.GetValue(7)),SubmittedAtUtc=r.IsDBNull(8)?null:ParseUtc(r.GetValue(8)),ApprovalDecisionAtUtc=r.IsDBNull(9)?null:ParseUtc(r.GetValue(9)),ApprovalDecisionByUserId=r.IsDBNull(10)?null:r.GetInt64(10),ApprovalDecisionByUserDisplay=r.IsDBNull(11)?null:r.GetString(11),ApprovalComment=r.IsDBNull(12)?null:r.GetString(12),RequiredByDate=r.IsDBNull(13)?null:ParseDate(r.GetValue(13)),PreferredSupplierId=r.IsDBNull(14)?null:r.GetInt64(14),PreferredSupplierName=r.IsDBNull(15)?null:r.GetString(15),BusinessJustification=r.IsDBNull(16)?null:r.GetString(16),Version=r.GetInt64(17) };
	private static PurchaseRequisitionLine ReadRequisitionLine(DbDataReader r) => new() { Id=r.GetInt64(0),PurchaseRequisitionId=r.GetInt64(1),LineNumber=r.GetInt32(2),ItemId=r.GetInt64(3),ItemPartNumber=r.GetString(4),ItemDescription=r.GetString(5),Quantity=r.GetInt32(6),Notes=r.IsDBNull(7)?null:r.GetString(7),Version=r.GetInt64(8) };
	private static RequestForQuotation ReadRfq(DbDataReader r) => new() { Id=r.GetInt64(0),RfqNumber=r.GetString(1),PurchaseRequisitionId=r.GetInt64(2),RequisitionNumber=r.GetString(3),Status=(RequestForQuotationStatus)r.GetInt32(4),CreatedAtUtc=ParseUtc(r.GetValue(5)),CreatedByUserId=r.GetInt64(6),ResponseDueDate=r.IsDBNull(7)?null:ParseDate(r.GetValue(7)),SelectedQuoteResponseId=r.IsDBNull(8)?null:r.GetInt64(8),SelectedByUserId=r.IsDBNull(9)?null:r.GetInt64(9),SelectedAtUtc=r.IsDBNull(10)?null:ParseUtc(r.GetValue(10)),ConvertedPurchaseOrderId=r.IsDBNull(11)?null:r.GetInt64(11),Version=r.GetInt64(12) };
	private static RequestForQuotationSupplier ReadRfqSupplier(DbDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2));
	private static RequestForQuotationLine ReadRfqLine(DbDataReader r) => new() { Id=r.GetInt64(0),RequestForQuotationId=r.GetInt64(1),PurchaseRequisitionLineId=r.GetInt64(2),ItemId=r.GetInt64(3),ItemPartNumber=r.GetString(4),ItemDescription=r.GetString(5),Quantity=r.GetInt32(6),RequiredByDate=r.IsDBNull(7)?null:ParseDate(r.GetValue(7)),Version=r.GetInt64(8) };
	private static SupplierQuoteResponse ReadQuoteResponse(DbDataReader r) => new() { Id=r.GetInt64(0),RequestForQuotationId=r.GetInt64(1),SupplierId=r.GetInt64(2),SupplierName=r.GetString(3),SupplierReference=r.IsDBNull(4)?null:r.GetString(4),Currency=r.GetString(5),ValidUntil=r.IsDBNull(6)?null:ParseDate(r.GetValue(6)),ReceivedAtUtc=ParseUtc(r.GetValue(7)),CapturedByUserId=r.GetInt64(8),Status=(SupplierQuoteResponseStatus)r.GetInt32(9),Version=r.GetInt64(10) };
	private static SupplierQuoteResponseLine ReadQuoteLine(DbDataReader r) => new() { Id=r.GetInt64(0),SupplierQuoteResponseId=r.GetInt64(1),RequestForQuotationLineId=r.GetInt64(2),ItemId=r.GetInt64(3),ItemPartNumber=r.GetString(4),ItemDescription=r.GetString(5),Quantity=r.GetInt32(6),UnitPrice=r.GetDecimal(7),MinimumOrderQuantity=r.IsDBNull(8)?null:r.GetInt32(8),LeadTimeDays=r.IsDBNull(9)?null:r.GetInt32(9),Version=r.GetInt64(10) };
	private static ProcurementSourcingEvidence ReadEvidence(DbDataReader r) => new(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3),r.GetInt64(4),ParseUtc(r.GetValue(5)));
	private static string Utc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static string? NullableDate(DateTime? value) => value?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private static DateTime ParseDate(object value) => value is DateTime dt ? dt.Date : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal).Date;
	private static DateTime ParseUtc(object value) => value is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
