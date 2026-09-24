// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ProcurementSourcingTests
{
	[Fact]
	public async Task RequisitionToPurchaseOrderRequiresExplicitAwardAndPreservesEvidence()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Sourcing.SaveRequisitionAsync(new PurchaseRequisition
		{
			RequiredByDate = DateTime.Today.AddDays(10),
			BusinessJustification = "Production material demand",
			Lines = [new PurchaseRequisitionLine { ItemId = context.ItemId, Quantity = 5, Notes = "Required for build" }]
		});
		Assert.Equal(PurchaseRequisitionStatus.Draft, draft.Status);

		var submitted = await context.Sourcing.SubmitAsync(draft.Id, draft.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Sourcing.ApproveAsync(submitted.Id, submitted.Version, "Self approval must fail"));

		context.SignInApprover();
		var approved = await context.Sourcing.ApproveAsync(submitted.Id, submitted.Version, "Approved");
		Assert.Equal(PurchaseRequisitionStatus.Approved, approved.Status);

		context.SignInAdministrator();
		var rfq = await context.Sourcing.CreateRfqAsync(approved.Id, [context.SupplierId], DateTime.Today.AddDays(3));
		Assert.Equal(RequestForQuotationStatus.Open, rfq.Status);
		var rfqDetails = await context.Sourcing.GetRfqAsync(rfq.Id) ?? throw new InvalidOperationException();
		var requestedLine = Assert.Single(rfqDetails.Lines);

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Sourcing.CaptureQuoteResponseAsync(new SupplierQuoteResponse
		{
			RequestForQuotationId = rfq.Id,
			SupplierId = context.SupplierId,
			Currency = "EUR",
			Lines = [new SupplierQuoteResponseLine { RequestForQuotationLineId = requestedLine.Id, ItemId = requestedLine.ItemId, Quantity = 6, UnitPrice = 12.5m }]
		}));

		var expensive = await context.Sourcing.CaptureQuoteResponseAsync(Quote(rfq.Id, requestedLine, context.SupplierId, 14m, "Q-HIGH"));
		var preferred = await context.Sourcing.CaptureQuoteResponseAsync(Quote(rfq.Id, requestedLine, context.SupplierId, 11m, "Q-LOW"));
		var comparison = await context.Sourcing.CompareAsync(rfq.Id);
		Assert.Equal([preferred.Id, expensive.Id], comparison.Select(row => row.SupplierQuoteResponseId).ToArray());
		Assert.All(comparison, row => Assert.False(row.IsSelected));

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Sourcing.ConvertSelectedQuoteToPurchaseOrderAsync(rfq.Id));
		var awarded = await context.Sourcing.SelectQuoteAsync(rfq.Id, rfqDetails.Version, preferred.Id);
		Assert.Equal(RequestForQuotationStatus.Awarded, awarded.Status);

		var purchaseOrderId = await context.Sourcing.ConvertSelectedQuoteToPurchaseOrderAsync(rfq.Id);
		Assert.Equal(purchaseOrderId, await context.Sourcing.ConvertSelectedQuoteToPurchaseOrderAsync(rfq.Id));
		var evidence = await context.Sourcing.GetEvidenceByPurchaseOrderAsync(purchaseOrderId);
		Assert.NotNull(evidence);
		Assert.Equal(draft.Id, evidence!.PurchaseRequisitionId);
		Assert.Equal(rfq.Id, evidence.RequestForQuotationId);
		Assert.Equal(preferred.Id, evidence.SupplierQuoteResponseId);
		var purchaseOrder = await context.Orders.GetByIdAsync(purchaseOrderId) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Draft, purchaseOrder.Status);
		Assert.Equal(context.SupplierId, purchaseOrder.SupplierId);
		Assert.Equal(5, Assert.Single(purchaseOrder.Lines).Quantity);
		Assert.Equal(11m, purchaseOrder.Lines[0].UnitPrice);
	}

	[Fact]
	public async Task ExpiredQuoteCannotBeAwardedAndQuoteEvidenceLocksAfterAward()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var rfq = await CreateApprovedRfqAsync(context);
		var details = await context.Sourcing.GetRfqAsync(rfq.Id) ?? throw new InvalidOperationException();
		var line = Assert.Single(details.Lines);

		var expired = Quote(rfq.Id, line, context.SupplierId, 10m, "Q-EXPIRED");
		expired.ValidUntil = DateTime.Today.AddDays(-1);
		expired = await context.Sourcing.CaptureQuoteResponseAsync(expired);
		Assert.Equal(SupplierQuoteResponseStatus.Expired, expired.Status);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Sourcing.SelectQuoteAsync(rfq.Id, details.Version, expired.Id));

		var active = await context.Sourcing.CaptureQuoteResponseAsync(Quote(rfq.Id, line, context.SupplierId, 10.5m, "Q-ACTIVE"));
		var attachment = await context.BusinessAttachments.AddAsync(
			BusinessAttachmentEntityKind.SupplierQuoteResponse,
			active.Id,
			"quote.pdf",
			"application/pdf",
			new MemoryStream([1, 2, 3]),
			"Supplier quotation",
			"Quote");
		await context.Sourcing.SelectQuoteAsync(rfq.Id, details.Version, active.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.BusinessAttachments.ReplaceAsync(
			attachment.Id,
			attachment.Version,
			"quote-revised.pdf",
			"application/pdf",
			new MemoryStream([4, 5, 6])));
	}

	private static SupplierQuoteResponse Quote(long rfqId, RequestForQuotationLine line, long supplierId, decimal unitPrice, string reference) => new()
	{
		RequestForQuotationId = rfqId,
		SupplierId = supplierId,
		SupplierReference = reference,
		Currency = "EUR",
		ValidUntil = DateTime.Today.AddDays(14),
		Lines =
		[
			new SupplierQuoteResponseLine
			{
				RequestForQuotationLineId = line.Id,
				ItemId = line.ItemId,
				Quantity = line.Quantity,
				UnitPrice = unitPrice,
				MinimumOrderQuantity = line.Quantity,
				LeadTimeDays = 5
			}
		]
	};

	private static async Task<RequestForQuotation> CreateApprovedRfqAsync(ProcurementTestContext context)
	{
		var draft = await context.Sourcing.SaveRequisitionAsync(new PurchaseRequisition
		{
			BusinessJustification = "Controlled sourcing test",
			Lines = [new PurchaseRequisitionLine { ItemId = context.ItemId, Quantity = 3 }]
		});
		var submitted = await context.Sourcing.SubmitAsync(draft.Id, draft.Version);
		context.SignInApprover();
		var approved = await context.Sourcing.ApproveAsync(submitted.Id, submitted.Version, "Approved");
		context.SignInAdministrator();
		return await context.Sourcing.CreateRfqAsync(approved.Id, [context.SupplierId], DateTime.Today.AddDays(5));
	}
}
