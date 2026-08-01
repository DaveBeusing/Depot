// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record PurchaseOrderApprovalFilter(
	string? SearchText,
	string? SupplierFilter,
	string? CreatorFilter,
	DateTime? SubmittedFromUtc,
	DateTime? SubmittedToUtcExclusive);

public sealed record PurchaseOrderApprovalWorkItem(
	long Id,
	string OrderNumber,
	long SupplierId,
	string SupplierName,
	DateTime OrderDate,
	DateTime? ExpectedDeliveryDate,
	string? Notes,
	long? CreatedByUserId,
	string CreatorDisplayName,
	DateTime SubmittedAtUtc,
	decimal TotalAmount,
	long Version)
{
	public DateTime SubmittedAtLocal => SubmittedAtUtc.ToLocalTime();
}

public sealed record PurchaseOrderApprovalSummary(
	long OpenCount,
	DateTime? OldestSubmittedAtUtc,
	decimal TotalAmount)
{
	public DateTime? OldestSubmittedAtLocal => OldestSubmittedAtUtc?.ToLocalTime();
}

public sealed record PurchaseOrderApprovalPage(
	PageResult<PurchaseOrderApprovalWorkItem> Page,
	PurchaseOrderApprovalSummary Summary);

public sealed record PurchaseOrderApprovalHistoryItem(
	DateTime TimestampUtc,
	string UserEmail,
	string Action,
	string StatusChange,
	IReadOnlyList<AuditValueChange> Changes)
{
	public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
}

public sealed record PurchaseOrderApprovalDetails(
	PurchaseOrder Order,
	IReadOnlyList<PurchaseOrderApprovalHistoryItem> History)
{
	public decimal TotalAmount => Order.Lines.Sum(line => line.Quantity * line.UnitPrice);
}
