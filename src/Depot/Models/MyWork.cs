// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum MyWorkSectionKind
{
	NeedsMyAction = 1,
	MyDrafts = 2,
	Waiting = 3,
	Exceptions = 4,
	RecentlyCompleted = 5
}

public enum MyWorkItemKind
{
	PurchaseOrder = 1,
	PurchaseOrderApproval = 2,
	SalesOrder = 3,
	SalesOrderApproval = 4,
	Shipment = 5,
	InventoryCount = 6,
	ReceivableOpenItem = 7,
	SupplierDocument = 8,
	BankStatementLine = 9,
	PaymentRun = 10
}

public enum MyWorkPriority
{
	Low = 1,
	Normal = 2,
	High = 3,
	Critical = 4
}

public sealed record MyWorkItem(
	MyWorkSectionKind Section,
	MyWorkItemKind Kind,
	long EntityId,
	string DisplayNumber,
	string Title,
	string? Context,
	string Status,
	decimal? AmountOrQuantity,
	DateTime? DueAt,
	int? AgeDays,
	MyWorkPriority Priority,
	string RouteId,
	string PrimaryAction,
	long? SourceUserId,
	DateTime? CompletedAtUtc = null)
{
	public string DueOrAgeDisplay =>
		DueAt is not null ? DueAt.Value.ToString("d") :
		AgeDays is not null ? $"{AgeDays.Value:N0} d" :
		string.Empty;
}

public sealed record MyWorkSection(
	MyWorkSectionKind Kind,
	string Title,
	IReadOnlyList<MyWorkItem> Items)
{
	public bool IsEmpty => Items.Count == 0;
}

public sealed record MyWorkProviderFailure(string Provider, string Message);

public sealed record MyWorkSnapshot(
	IReadOnlyList<MyWorkSection> Sections,
	IReadOnlyList<MyWorkProviderFailure> Failures)
{
	public bool HasFailures => Failures.Count > 0;
}

public sealed record MyWorkQuery(long UserId, DateTime NowUtc, int ProviderLimit);
