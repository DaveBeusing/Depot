// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ProcurementSourcingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ProcurementSourcingRepository _sourcing;
	private readonly SupplierRepository _suppliers;
	private readonly ItemRepository _items;
	private readonly AuditRepository _auditRepository;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly ApprovalPolicyService _approvalPolicies;
	private readonly PurchaseOrderService _purchaseOrders;

	public ProcurementSourcingService(
		IDatabaseTransactionRunner transactions,
		ProcurementSourcingRepository sourcing,
		SupplierRepository suppliers,
		ItemRepository items,
		AuditRepository auditRepository,
		AuditService audit,
		IAuthorizationService authorization,
		ApprovalPolicyService approvalPolicies,
		PurchaseOrderService purchaseOrders)
	{
		_transactions = transactions;
		_sourcing = sourcing;
		_suppliers = suppliers;
		_items = items;
		_auditRepository = auditRepository;
		_audit = audit;
		_authorization = authorization;
		_approvalPolicies = approvalPolicies;
		_purchaseOrders = purchaseOrders;
	}

	public bool CanViewRequisitions => _authorization.HasPermission(ApplicationPermission.PurchaseRequisitionsView);
	public bool CanManageRequisitions => _authorization.HasPermission(ApplicationPermission.PurchaseRequisitionsManage);
	public bool CanApproveRequisitions => _authorization.HasPermission(ApplicationPermission.PurchaseRequisitionsApprove);
	public bool CanViewSourcing => _authorization.HasPermission(ApplicationPermission.SupplierSourcingView);
	public bool CanManageSourcing => _authorization.HasPermission(ApplicationPermission.SupplierSourcingManage);
	public bool CanConvertSourcing => _authorization.HasPermission(ApplicationPermission.SupplierSourcingConvert);

	public Task<PageResult<PurchaseRequisition>> SearchRequisitionsAsync(string? searchText = null, PurchaseRequisitionStatus? status = null, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseRequisitionsView);
		return _sourcing.SearchRequisitionsAsync(searchText, status, pageNumber, pageSize, cancellationToken);
	}

	public Task<PageResult<RequestForQuotation>> SearchRfqsAsync(RequestForQuotationStatus? status = null, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingView);
		return _sourcing.SearchRfqsAsync(status, pageNumber, pageSize, cancellationToken);
	}

	public Task<PurchaseRequisition?> GetRequisitionAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseRequisitionsView);
		return _sourcing.GetRequisitionAsync(id, cancellationToken);
	}

	public Task<RequestForQuotation?> GetRfqAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingView);
		return _sourcing.GetRfqAsync(id, cancellationToken);
	}

	public Task<IReadOnlyList<SupplierQuoteResponse>> ListQuoteResponsesAsync(long rfqId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingView);
		return _sourcing.ListQuoteResponsesAsync(rfqId, cancellationToken);
	}

	public async Task<PurchaseRequisition> SaveRequisitionAsync(PurchaseRequisition value, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseRequisitionsManage);
		ArgumentNullException.ThrowIfNull(value);
		var user = CurrentUser();
		if (value.Id != 0 && value.Status is not (PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Returned))
			throw new InvalidOperationException("Only draft or returned requisitions can be edited.");
		value.RequestedByUserId = value.RequestedByUserId <= 0 ? user.Id : value.RequestedByUserId;
		if (value.Id == 0)
		{
			value.CreatedByUserId = user.Id;
			value.CreatedAtUtc = DateTime.UtcNow;
			value.Status = PurchaseRequisitionStatus.Draft;
		}
		await ValidateRequisitionAsync(value, cancellationToken);
		var before = value.Id == 0 ? null : await _sourcing.GetRequisitionAsync(value.Id, cancellationToken);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var saved = await _sourcing.SaveRequisitionAsync(transaction, value, token);
			await _auditRepository.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(saved.Id, saved) : _audit.CreateUpdatedEntry(saved.Id, before, saved), token);
			return saved;
		}, cancellationToken);
	}

	public async Task<PurchaseRequisition> SubmitAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseRequisitionsManage);
		var before = await RequireRequisitionAsync(id, cancellationToken);
		if (before.Version != version) throw new ConcurrencyConflictException("purchase requisition");
		if (before.Status is not (PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Returned))
			throw new InvalidOperationException("Only draft or returned requisitions can be submitted.");
		await ValidateRequisitionAsync(before, cancellationToken);
		var subjectId = id.ToString(CultureInfo.InvariantCulture);
		var snapshot = await _approvalPolicies.TryResolveSnapshotAsync(new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseRequisition, subjectId), cancellationToken);
		var submittedAt = DateTime.UtcNow;
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _sourcing.SetRequisitionStatusAsync(transaction, id, version, before.Status, PurchaseRequisitionStatus.Submitted, null, submittedAt, null, null, token))
				throw new ConcurrencyConflictException("purchase requisition");
			if (snapshot is not null) await _approvalPolicies.StartResolvedAsync(transaction, snapshot, token);
			var after = await _sourcing.GetRequisitionAsync(transaction, id, token) ?? throw new InvalidOperationException("Submitted requisition could not be reloaded.");
			await _auditRepository.CreateAsync(transaction, _audit.CreateActionEntry(id, "Submitted", before, after), token);
			return after;
		}, cancellationToken);
	}

	public Task<PurchaseRequisition> ApproveAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default) =>
		DecideAsync(id, version, ApprovalDecisionKind.Approved, false, comment, cancellationToken);

	public Task<PurchaseRequisition> RejectAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default) =>
		DecideAsync(id, version, ApprovalDecisionKind.Rejected, false, comment, cancellationToken);

	public Task<PurchaseRequisition> ReturnAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default) =>
		DecideAsync(id, version, ApprovalDecisionKind.Rejected, true, comment, cancellationToken);

	private async Task<PurchaseRequisition> DecideAsync(long id, long version, ApprovalDecisionKind decision, bool returnForChanges, string? comment, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseRequisitionsApprove);
		var before = await RequireRequisitionAsync(id, cancellationToken);
		if (before.Version != version) throw new ConcurrencyConflictException("purchase requisition");
		if (before.Status != PurchaseRequisitionStatus.Submitted) throw new InvalidOperationException("Only submitted requisitions can be decided.");
		var user = CurrentUser();
		if (before.CreatedByUserId == user.Id || before.RequestedByUserId == user.Id)
			throw new InvalidOperationException("A requisition cannot be decided by its creator or requester.");
		comment = Normalize(comment, 2000);
		var subjectId = id.ToString(CultureInfo.InvariantCulture);
		ApprovalPreparedDecision? prepared = null;
		if (await _approvalPolicies.GetPendingInstanceAsync(ApprovalSubjectKind.PurchaseRequisition, subjectId, cancellationToken) is not null)
			prepared = await _approvalPolicies.PrepareDecisionAsync(ApprovalSubjectKind.PurchaseRequisition, subjectId, decision, comment, cancellationToken);

		var target = returnForChanges
			? PurchaseRequisitionStatus.Returned
			: decision == ApprovalDecisionKind.Approved
				? prepared is { IsFinalApproval: false } ? PurchaseRequisitionStatus.Submitted : PurchaseRequisitionStatus.Approved
				: PurchaseRequisitionStatus.Rejected;
		var now = DateTime.UtcNow;
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _sourcing.SetRequisitionStatusAsync(transaction, id, version, PurchaseRequisitionStatus.Submitted, target,
				target == PurchaseRequisitionStatus.Submitted ? null : user.Id, null,
				target == PurchaseRequisitionStatus.Submitted ? null : now, comment, token))
				throw new ConcurrencyConflictException("purchase requisition");
			if (prepared is not null) await _approvalPolicies.ApplyPreparedDecisionAsync(transaction, prepared, token);
			var after = await _sourcing.GetRequisitionAsync(transaction, id, token) ?? throw new InvalidOperationException("Requisition decision could not be reloaded.");
			await _auditRepository.CreateAsync(transaction, _audit.CreateActionEntry(id, target.ToString(), before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<PurchaseRequisition> CancelRequisitionAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseRequisitionsManage);
		var before = await RequireRequisitionAsync(id, cancellationToken);
		if (before.Version != version) throw new ConcurrencyConflictException("purchase requisition");
		if (before.Status is not (PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Returned))
			throw new InvalidOperationException("Only draft or returned requisitions can be cancelled.");
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _sourcing.SetRequisitionStatusAsync(transaction, id, version, before.Status, PurchaseRequisitionStatus.Cancelled, CurrentUser().Id, null, DateTime.UtcNow, "Cancelled", token))
				throw new ConcurrencyConflictException("purchase requisition");
			var after = await _sourcing.GetRequisitionAsync(transaction, id, token) ?? throw new InvalidOperationException("Cancelled requisition could not be reloaded.");
			await _auditRepository.CreateAsync(transaction, _audit.CreateActionEntry(id, "Cancelled", before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<RequestForQuotation> CreateRfqAsync(long requisitionId, IReadOnlyCollection<long> supplierIds, DateTime? responseDueDate = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingManage);
		if (supplierIds.Count == 0) throw new ArgumentException("At least one supplier is required.", nameof(supplierIds));
		var requisition = await RequireRequisitionAsync(requisitionId, cancellationToken);
		if (requisition.Status != PurchaseRequisitionStatus.Approved) throw new InvalidOperationException("RFQs can only be created from approved requisitions.");
		foreach (var supplierId in supplierIds.Distinct())
		{
			var supplier = await _suppliers.GetByIdAsync(supplierId, cancellationToken) ?? throw new InvalidOperationException($"Supplier '{supplierId}' was not found.");
			if (!supplier.IsActive) throw new InvalidOperationException($"Supplier '{supplier.Name}' is inactive.");
		}
		var user = CurrentUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var rfq = await _sourcing.CreateRfqAsync(transaction, requisition, supplierIds.Distinct().Order().ToArray(), responseDueDate, user.Id, token);
			await _auditRepository.CreateAsync(transaction, _audit.CreateCreatedEntry(rfq.Id, rfq), token);
			return rfq;
		}, cancellationToken);
	}

	public async Task<RequestForQuotation> CancelRfqAsync(long rfqId, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingManage);
		var before = await _sourcing.GetRfqAsync(rfqId, cancellationToken) ?? throw new InvalidOperationException("RFQ was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("request for quotation");
		if (before.Status != RequestForQuotationStatus.Open) throw new InvalidOperationException("Only open RFQs can be cancelled.");
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _sourcing.SetRfqStatusAsync(transaction, rfqId, version, RequestForQuotationStatus.Open, RequestForQuotationStatus.Cancelled, token))
				throw new ConcurrencyConflictException("request for quotation");
			var after = await _sourcing.GetRfqAsync(transaction, rfqId, token) ?? throw new InvalidOperationException("Cancelled RFQ could not be reloaded.");
			await _auditRepository.CreateAsync(transaction, _audit.CreateActionEntry(rfqId, "Cancelled", before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<SupplierQuoteResponse> CaptureQuoteResponseAsync(SupplierQuoteResponse response, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingManage);
		ArgumentNullException.ThrowIfNull(response);
		var rfq = await _sourcing.GetRfqAsync(response.RequestForQuotationId, cancellationToken) ?? throw new InvalidOperationException("RFQ was not found.");
		if (rfq.Status != RequestForQuotationStatus.Open) throw new InvalidOperationException("Supplier responses can only be captured for open RFQs.");
		if (!rfq.Suppliers.Any(value => value.SupplierId == response.SupplierId)) throw new InvalidOperationException("The supplier is not a recipient of this RFQ.");
		response.Currency = NormalizeCurrency(response.Currency);
		response.SupplierReference = Normalize(response.SupplierReference, 250);
		response.CapturedByUserId = CurrentUser().Id;
		response.ReceivedAtUtc = DateTime.UtcNow;
		response.Status = response.ValidUntil is not null && response.ValidUntil.Value.Date < DateTime.UtcNow.Date ? SupplierQuoteResponseStatus.Expired : SupplierQuoteResponseStatus.Active;
		if (response.Lines.Count != rfq.Lines.Count) throw new InvalidOperationException("The quote response must contain exactly one line for every RFQ line.");
		var rfqLines = rfq.Lines.ToDictionary(value => value.Id);
		foreach (var line in response.Lines)
		{
			if (!rfqLines.TryGetValue(line.RequestForQuotationLineId, out var requested) || requested.ItemId != line.ItemId)
				throw new InvalidOperationException("A quote line does not correspond to the requested RFQ line.");
			if (line.Quantity <= 0 || line.UnitPrice < 0) throw new InvalidOperationException("Quoted quantity must be positive and unit price cannot be negative.");
			if (line.Quantity != requested.Quantity) throw new InvalidOperationException("Quoted quantity must match the RFQ requested quantity; use MOQ to capture supplier minimums.");
			if (line.MinimumOrderQuantity is <= 0) throw new InvalidOperationException("MOQ must be positive when supplied.");
			if (line.LeadTimeDays is < 0) throw new InvalidOperationException("Lead time cannot be negative.");
		}
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var created = await _sourcing.CreateQuoteResponseAsync(transaction, response, token);
			await _auditRepository.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<IReadOnlyList<SupplierQuoteComparisonRow>> CompareAsync(long rfqId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingView);
		var rfq = await _sourcing.GetRfqAsync(rfqId, cancellationToken) ?? throw new InvalidOperationException("RFQ was not found.");
		var responses = await _sourcing.ListQuoteResponsesAsync(rfqId, cancellationToken);
		var details = new List<SupplierQuoteResponse>();
		foreach (var response in responses)
		{
			var full = await _sourcing.GetQuoteResponseAsync(response.Id, cancellationToken);
			if (full is not null) details.Add(full);
		}
		var today = DateTime.UtcNow.Date;
		return details.SelectMany(response => response.Lines.Select(line =>
		{
			var requested = rfq.Lines.Single(value => value.Id == line.RequestForQuotationLineId);
			return new SupplierQuoteComparisonRow(requested.Id, requested.ItemId, requested.ItemPartNumber, requested.ItemDescription, requested.Quantity,
				response.Id, response.SupplierId, response.SupplierName, response.Currency, line.UnitPrice, line.UnitPrice * line.Quantity,
				line.MinimumOrderQuantity, line.LeadTimeDays, response.ValidUntil, response.ValidUntil is not null && response.ValidUntil.Value.Date < today,
				rfq.SelectedQuoteResponseId == response.Id);
		})).OrderBy(value => value.RequestForQuotationLineId)
			.ThenBy(value => value.Currency, StringComparer.Ordinal)
			.ThenBy(value => value.UnitPrice)
			.ThenBy(value => value.SupplierName, StringComparer.Ordinal)
			.ThenBy(value => value.SupplierQuoteResponseId)
			.ToArray();
	}

	public async Task<RequestForQuotation> SelectQuoteAsync(long rfqId, long version, long quoteResponseId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingManage);
		var rfq = await _sourcing.GetRfqAsync(rfqId, cancellationToken) ?? throw new InvalidOperationException("RFQ was not found.");
		if (rfq.Version != version) throw new ConcurrencyConflictException("request for quotation");
		var quote = await _sourcing.GetQuoteResponseAsync(quoteResponseId, cancellationToken) ?? throw new InvalidOperationException("Supplier quote response was not found.");
		if (quote.RequestForQuotationId != rfqId || quote.Status != SupplierQuoteResponseStatus.Active || quote.ValidUntil is not null && quote.ValidUntil.Value.Date < DateTime.UtcNow.Date)
			throw new InvalidOperationException("The selected quote is not eligible for award.");
		var user = CurrentUser();
		var now = DateTime.UtcNow;
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _sourcing.SelectQuoteAsync(transaction, rfqId, version, quoteResponseId, user.Id, now, token))
				throw new ConcurrencyConflictException("request for quotation");
			var after = await _sourcing.GetRfqAsync(transaction, rfqId, token) ?? throw new InvalidOperationException("Selected RFQ could not be reloaded.");
			await _auditRepository.CreateAsync(transaction, _audit.CreateActionEntry(rfqId, "QuoteSelected", rfq, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<long> ConvertSelectedQuoteToPurchaseOrderAsync(long rfqId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingConvert);
		var existing = await _sourcing.GetRfqAsync(rfqId, cancellationToken) ?? throw new InvalidOperationException("RFQ was not found.");
		if (existing.ConvertedPurchaseOrderId is not null) return existing.ConvertedPurchaseOrderId.Value;
		if (existing.Status != RequestForQuotationStatus.Awarded || existing.SelectedQuoteResponseId is null)
			throw new InvalidOperationException("An eligible supplier quote must be explicitly selected before conversion.");

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var rfq = await _sourcing.GetRfqAsync(transaction, rfqId, token) ?? throw new InvalidOperationException("RFQ was not found.");
			if (rfq.ConvertedPurchaseOrderId is not null) return rfq.ConvertedPurchaseOrderId.Value;
			if (rfq.SelectedQuoteResponseId is null) throw new InvalidOperationException("No supplier quote is selected.");
			var quote = await _sourcing.GetQuoteResponseAsync(transaction, rfq.SelectedQuoteResponseId.Value, token) ?? throw new InvalidOperationException("Selected supplier quote was not found.");
			if (quote.Status != SupplierQuoteResponseStatus.Active || quote.ValidUntil is not null && quote.ValidUntil.Value.Date < DateTime.UtcNow.Date)
				throw new InvalidOperationException("The selected supplier quote is no longer eligible.");

			var order = new PurchaseOrder
			{
				SupplierId = quote.SupplierId,
				OrderDate = DateTime.Today,
				ExpectedDeliveryDate = rfq.Lines.Where(value => value.RequiredByDate is not null).Select(value => value.RequiredByDate).Order().FirstOrDefault(),
				Notes = $"Created from {rfq.RfqNumber} / {rfq.RequisitionNumber}.",
				Lines = quote.Lines.OrderBy(value => value.RequestForQuotationLineId)
					.Select(value => new PurchaseOrderLine { ItemId = value.ItemId, Quantity = value.Quantity, UnitPrice = value.UnitPrice }).ToArray()
			};
			var created = await _purchaseOrders.CreateSourcedDraftAsync(transaction, order, token);
			var user = CurrentUser();
			if (!await _sourcing.CompleteConversionAsync(transaction, rfq, created.Id, user.Id, DateTime.UtcNow, token))
				throw new ConcurrencyConflictException("request for quotation");
			var after = await _sourcing.GetRfqAsync(transaction, rfqId, token) ?? throw new InvalidOperationException("Converted RFQ could not be reloaded.");
			await _auditRepository.CreateAsync(transaction, _audit.CreateActionEntry(rfq.Id, "ConvertedToPurchaseOrder", rfq, after), token);
			return created.Id;
		}, cancellationToken);
	}

	public Task<ProcurementSourcingEvidence?> GetEvidenceByPurchaseOrderAsync(long purchaseOrderId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierSourcingView);
		return _sourcing.GetEvidenceByPurchaseOrderAsync(purchaseOrderId, cancellationToken);
	}

	private async Task ValidateRequisitionAsync(PurchaseRequisition value, CancellationToken cancellationToken)
	{
		if (value.Lines.Count == 0) throw new InvalidOperationException("A purchase requisition requires at least one line.");
		if (value.BusinessJustification?.Trim().Length > 4000) throw new ArgumentException("Business justification must not exceed 4000 characters.");
		if (value.PreferredSupplierId is not null && await _suppliers.GetByIdAsync(value.PreferredSupplierId.Value, cancellationToken) is null)
			throw new InvalidOperationException("Preferred supplier was not found.");
		var seen = new HashSet<long>();
		foreach (var line in value.Lines)
		{
			if (line.Quantity <= 0) throw new InvalidOperationException("Requisition quantities must be positive.");
			if (!seen.Add(line.ItemId)) throw new InvalidOperationException("An item may appear only once per requisition.");
			if (await _items.GetByIdAsync(line.ItemId, cancellationToken) is null) throw new InvalidOperationException($"Item '{line.ItemId}' was not found.");
			if (line.Notes?.Trim().Length > 2000) throw new ArgumentException("Line notes must not exceed 2000 characters.");
		}
	}

	private async Task<PurchaseRequisition> RequireRequisitionAsync(long id, CancellationToken cancellationToken) =>
		await _sourcing.GetRequisitionAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase requisition was not found.");

	private User CurrentUser() =>
		_authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required.");

	private static string NormalizeCurrency(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Currency is required.", nameof(value));
		var result = value.Trim().ToUpperInvariant();
		if (result.Length != 3 || !result.All(char.IsLetter)) throw new ArgumentException("Currency must be a three-letter ISO code.", nameof(value));
		return result;
	}

	private static string? Normalize(string? value, int maxLength)
	{
		var result = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		if (result?.Length > maxLength) throw new ArgumentException($"Value must not exceed {maxLength} characters.");
		return result;
	}
}
