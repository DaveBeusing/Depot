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

public enum MyWorkQuickFilter
{
	All = 0,
	Overdue = 1,
	Today = 2,
	HighPriority = 3
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
	PaymentRun = 10,
	SalesLeadActivity = 11,
	SalesOpportunityActivity = 12,
	SalesOpportunityFollowUp = 13,
	FinanceBudget = 14
}

public enum MyWorkPriority
{
	Low = 1,
	Normal = 2,
	High = 3,
	Critical = 4
}

public enum MyWorkValueKind
{
	Auto = 0,
	Currency = 1,
	Quantity = 2,
	Documents = 3
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
	DateTime? CompletedAtUtc = null,
	MyWorkValueKind ValueKind = MyWorkValueKind.Auto)
{
	public bool HasPrimaryAction => !string.IsNullOrWhiteSpace(PrimaryAction) && !string.IsNullOrWhiteSpace(RouteId);

	public string DueOrAgeDisplay =>
		DueAt is not null ? DueAt.Value.ToString("d") :
		AgeDays is not null ? $"{AgeDays.Value:N0} d" :
		string.Empty;

	public string AmountOrQuantityDisplay
	{
		get
		{
			if (AmountOrQuantity is not { } value) return string.Empty;
			return ResolveValueKind() switch
			{
				MyWorkValueKind.Currency => $"{value:N2} EUR",
				MyWorkValueKind.Quantity => value == decimal.Truncate(value) ? $"{value:N0} pcs" : $"{value:N2} pcs",
				MyWorkValueKind.Documents => $"{value:N0} documents",
				_ => value.ToString("N2")
			};
		}
	}

	public string PriorityDisplay => Priority switch
	{
		MyWorkPriority.Critical => "High",
		MyWorkPriority.High => "High",
		MyWorkPriority.Normal => "Normal",
		MyWorkPriority.Low => "Low",
		_ => Priority.ToString()
	};

	public string DueStateDisplay
	{
		get
		{
			if (DueAt is { } due)
			{
				if (due.Date < DateTime.Today) return "Overdue";
				if (due.Date == DateTime.Today) return "Due today";
			}
			if (Section == MyWorkSectionKind.Waiting) return "Waiting";
			if (Section == MyWorkSectionKind.Exceptions) return "Blocked";
			return DueOrAgeDisplay;
		}
	}

	private MyWorkValueKind ResolveValueKind()
	{
		if (ValueKind != MyWorkValueKind.Auto) return ValueKind;
		if (Kind == MyWorkItemKind.PurchaseOrder && Title.Contains("delivery", StringComparison.OrdinalIgnoreCase))
			return MyWorkValueKind.Quantity;
		if (Kind == MyWorkItemKind.SalesOrder &&
			(Title.Contains("fulfill", StringComparison.OrdinalIgnoreCase) || Title.Contains("backorder", StringComparison.OrdinalIgnoreCase)))
			return MyWorkValueKind.Quantity;
		return Kind switch
		{
			MyWorkItemKind.Shipment or MyWorkItemKind.InventoryCount => MyWorkValueKind.Quantity,
			MyWorkItemKind.PurchaseOrder or MyWorkItemKind.PurchaseOrderApproval or
			MyWorkItemKind.SalesOrder or MyWorkItemKind.SalesOrderApproval or
			MyWorkItemKind.ReceivableOpenItem or MyWorkItemKind.SupplierDocument or
			MyWorkItemKind.BankStatementLine or MyWorkItemKind.PaymentRun or MyWorkItemKind.FinanceBudget => MyWorkValueKind.Currency,
			_ => MyWorkValueKind.Auto
		};
	}
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
