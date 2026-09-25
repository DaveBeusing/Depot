// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum BusinessAttachmentEntityKind
{
	Customer = 1,
	Supplier = 2,
	Item = 3,
	SalesQuote = 4,
	SalesOrder = 5,
	SalesInvoice = 6,
	PurchaseOrder = 7,
	GoodsReceipt = 8,
	SupplierDocument = 9,
	PurchaseRequisition = 10,
	RequestForQuotation = 11,
	SupplierQuoteResponse = 12,
	Project = 13,
	SubscriptionContract = 14,
	ServiceCase = 15,
	ServiceOrder = 16
}

public enum BusinessAttachmentStatus
{
	Active = 1,
	Retired = 2
}

public sealed record BusinessAttachment(
	Guid Id,
	BusinessAttachmentEntityKind EntityKind,
	long EntityId,
	string FileName,
	string MediaType,
	long ByteLength,
	string Sha256,
	string? Description,
	string? Category,
	long? CreatedByUserId,
	DateTime CreatedAtUtc,
	int CurrentRevision,
	BusinessAttachmentStatus Status,
	long Version);

public sealed record BusinessAttachmentRevision(
	Guid AttachmentId,
	int Revision,
	string FileName,
	string MediaType,
	long ByteLength,
	string Sha256,
	long? CreatedByUserId,
	DateTime CreatedAtUtc);

public sealed record BusinessAttachmentContent(
	BusinessAttachment Attachment,
	BusinessAttachmentRevision Revision,
	Stream Content);

public sealed record BusinessAttachmentMetadataUpdate(
	string? Description,
	string? Category,
	long ExpectedVersion);
