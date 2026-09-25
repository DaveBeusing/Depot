// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ServiceCasePriority
{
	Low = 1,
	Normal = 2,
	High = 3,
	Critical = 4
}

public enum ServiceCaseStatus
{
	New = 1,
	Open = 2,
	InProgress = 3,
	Waiting = 4,
	Resolved = 5,
	Closed = 6,
	Cancelled = 7
}

public enum ServiceOrderStatus
{
	Planned = 1,
	InProgress = 2,
	Completed = 3,
	Cancelled = 4
}

public enum ServicePartMovementKind
{
	Consumption = 1,
	Return = 2
}

public sealed record ServiceCase
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public string CaseNumber { get; init; } = string.Empty;
	public long CustomerId { get; init; }
	public string CustomerName { get; init; } = string.Empty;
	public long? CustomerContactId { get; init; }
	public string? ContactName { get; init; }
	public string Subject { get; init; } = string.Empty;
	public string? Description { get; init; }
	public string? Category { get; init; }
	public ServiceCasePriority Priority { get; init; } = ServiceCasePriority.Normal;
	public ServiceCaseStatus Status { get; init; } = ServiceCaseStatus.New;
	public long? OwnerUserId { get; init; }
	public string? OwnerDisplayName { get; init; }
	public long? SalesOrderId { get; init; }
	public long? ItemId { get; init; }
	public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
	public long CreatedByUserId { get; init; }
	public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
	public long UpdatedByUserId { get; init; }
	public DateTime? DueAtUtc { get; init; }
	public DateTime? ResolvedAtUtc { get; init; }
	public long? ResolvedByUserId { get; init; }
	public DateTime? ClosedAtUtc { get; init; }
	public long? ClosedByUserId { get; init; }
	public DateTime? CancelledAtUtc { get; init; }
	public long? CancelledByUserId { get; init; }
	public bool IsOverdue(DateTime nowUtc) => DueAtUtc is { } due && due < nowUtc && Status is not ServiceCaseStatus.Resolved and not ServiceCaseStatus.Closed and not ServiceCaseStatus.Cancelled;
}

public sealed record ServiceCaseHistory(
	long Id,
	long ServiceCaseId,
	ServiceCaseStatus? PreviousStatus,
	ServiceCaseStatus Status,
	string? Note,
	DateTime CreatedAtUtc,
	long CreatedByUserId);

public sealed record ServiceOrder
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public string OrderNumber { get; init; } = string.Empty;
	public long ServiceCaseId { get; init; }
	public string CaseNumber { get; init; } = string.Empty;
	public long CustomerId { get; init; }
	public string CustomerName { get; init; } = string.Empty;
	public long? AssignedOwnerUserId { get; init; }
	public string? AssignedOwnerDisplayName { get; init; }
	public long? ServicedItemId { get; init; }
	public string? ServicedPartNumber { get; init; }
	public long? ServicedInventoryId { get; init; }
	public string? SerialLotReference { get; init; }
	public ServiceOrderStatus Status { get; init; } = ServiceOrderStatus.Planned;
	public DateTime? PlannedStartAtUtc { get; init; }
	public DateTime? PlannedEndAtUtc { get; init; }
	public string? CompletionNotes { get; init; }
	public DateTime? CompletedAtUtc { get; init; }
	public long? CompletedByUserId { get; init; }
	public long? SalesInvoiceId { get; init; }
	public string? SalesInvoiceNumber { get; init; }
	public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
	public long CreatedByUserId { get; init; }
	public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
	public long UpdatedByUserId { get; init; }
}

public sealed record ServiceWorkLine
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public long ServiceOrderId { get; init; }
	public string Description { get; init; } = string.Empty;
	public decimal Quantity { get; init; } = 1m;
	public decimal UnitPrice { get; init; }
	public bool Billable { get; init; } = true;
	public decimal TaxRate { get; init; }
	public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
	public long CreatedByUserId { get; init; }
}

public sealed record ServicePartEvidence
{
	public long Id { get; init; }
	public long ServiceOrderId { get; init; }
	public long StockMovementId { get; init; }
	public ServicePartMovementKind MovementKind { get; init; }
	public long InventoryId { get; init; }
	public long ItemId { get; init; }
	public string PartNumber { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public int Quantity { get; init; }
	public decimal UnitPrice { get; init; }
	public bool Billable { get; init; }
	public decimal TaxRate { get; init; }
	public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
	public long CreatedByUserId { get; init; }
}

public sealed record ServiceCaseFilter(
	string? SearchText = null,
	ServiceCaseStatus? Status = null,
	ServiceCasePriority? Priority = null,
	long? OwnerUserId = null,
	long? CustomerId = null);

public sealed record ServiceInvoiceGenerationResult(ServiceOrder Order, SalesInvoice? Invoice);
