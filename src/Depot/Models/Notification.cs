// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum NotificationType
{
	Workflow = 1,
	System = 2,
	Announcement = 3
}

public enum NotificationSeverity
{
	Information = 1,
	Success = 2,
	Warning = 3,
	Error = 4
}

public sealed class Notification
{
	public long Id { get; set; }
	public NotificationType Type { get; set; }
	public NotificationSeverity Severity { get; set; }
	public string Title { get; set; } = string.Empty;
	public string Message { get; set; } = string.Empty;
	public string? SourceType { get; set; }
	public long? SourceId { get; set; }
	public string? SourceNumber { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public long? CreatedByUserId { get; set; }
	public DateTime? ExpiresAtUtc { get; set; }
	public long Version { get; set; } = 1;
}

public sealed class NotificationRecipient
{
	public long Id { get; set; }
	public long NotificationId { get; set; }
	public long UserId { get; set; }
	public DateTime? ReadAtUtc { get; set; }
	public DateTime? ArchivedAtUtc { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public long Version { get; set; } = 1;
}

public enum NotificationInboxFilter
{
	All,
	Unread,
	Archived
}

public sealed record NotificationFilter(
	string? SearchText,
	NotificationInboxFilter Inbox,
	NotificationType? Type,
	NotificationSeverity? Severity,
	DateTime? FromUtc,
	DateTime? ToUtcExclusive);

public sealed class NotificationListItem
{
	public long RecipientId { get; init; }
	public long NotificationId { get; init; }
	public NotificationType Type { get; init; }
	public NotificationSeverity Severity { get; init; }
	public string Title { get; init; } = string.Empty;
	public string MessagePreview { get; init; } = string.Empty;
	public string? SourceType { get; init; }
	public long? SourceId { get; init; }
	public string? SourceNumber { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public DateTime? ReadAtUtc { get; set; }
	public DateTime? ArchivedAtUtc { get; set; }
	public long RecipientVersion { get; set; }
	public bool IsUnread => ReadAtUtc is null;
	public bool IsArchived => ArchivedAtUtc is not null;
	public DateTime CreatedLocal => CreatedAtUtc.ToLocalTime();
}

public sealed record NotificationDetails(
	long RecipientId,
	long NotificationId,
	NotificationType Type,
	NotificationSeverity Severity,
	string Title,
	string Message,
	string? SourceType,
	long? SourceId,
	string? SourceNumber,
	DateTime CreatedAtUtc,
	DateTime? ExpiresAtUtc,
	DateTime? ReadAtUtc,
	DateTime? ArchivedAtUtc,
	long RecipientVersion)
{
	public DateTime CreatedLocal => CreatedAtUtc.ToLocalTime();
	public bool IsUnread => ReadAtUtc is null;
	public bool IsArchived => ArchivedAtUtc is not null;
}

public sealed record NotificationRequest(
	NotificationType Type,
	NotificationSeverity Severity,
	string Title,
	string Message,
	string? SourceType = null,
	long? SourceId = null,
	string? SourceNumber = null,
	long? CreatedByUserId = null,
	DateTime? ExpiresAtUtc = null);

public static class NotificationSourceTypes
{
	public const string PurchaseOrder = "PurchaseOrder";
	public const string PurchaseOrderApproval = "PurchaseOrderApproval";
	public const string InventoryCount = "InventoryCount";
	public const string DatabaseAdministration = "DatabaseAdministration";
	public const string SalesOrder = "SalesOrder";
	public const string SalesOrderApproval = "SalesOrderApproval";
	public const string Shipment = "Shipment";
	public const string CustomerReturn = "CustomerReturn";
	public const string SalesInvoice = "SalesInvoice";
	public const string SalesCreditNote = "SalesCreditNote";
}
