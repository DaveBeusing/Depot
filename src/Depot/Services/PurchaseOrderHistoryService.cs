// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class PurchaseOrderHistoryService
{
	private const int HistoryLimit = 100;
	private readonly AuditRepository _audit;
	private readonly IAuthorizationService _authorization;
	private readonly AuditJsonSanitizer _sanitizer;

	public PurchaseOrderHistoryService(
		AuditRepository audit,
		IAuthorizationService authorization,
		AuditJsonSanitizer sanitizer)
	{
		_audit = audit;
		_authorization = authorization;
		_sanitizer = sanitizer;
	}

	public async Task<IReadOnlyList<PurchaseOrderHistoryItem>> GetHistoryAsync(
		long purchaseOrderId,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.PurchaseOrdersView);
		var entries = await _audit.GetEntityHistoryAsync(
			nameof(PurchaseOrder), purchaseOrderId, HistoryLimit, cancellationToken);
		return entries.Select(ToHistoryItem).ToArray();
	}

	private PurchaseOrderHistoryItem ToHistoryItem(AuditLogDetails entry)
	{
		var before = ReadStatus(entry.BeforeJson);
		var after = ReadStatus(entry.AfterJson);
		var changes = _sanitizer.Compare(entry.BeforeJson, entry.AfterJson);
		return new PurchaseOrderHistoryItem(
			entry.TimestampUtc,
			DisplayStatus(before),
			DisplayStatus(after ?? before),
			entry.UserEmail,
			DescribeComment(entry.Action, changes));
	}

	private static PurchaseOrderStatus? ReadStatus(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try
		{
			using var document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("status", out var value) ||
				!value.TryGetInt32(out var status) ||
				!Enum.IsDefined(typeof(PurchaseOrderStatus), status)) return null;
			return (PurchaseOrderStatus)status;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string DescribeComment(string action, IReadOnlyList<AuditValueChange> changes)
	{
		var comment = changes.FirstOrDefault(change =>
			change.Property.EndsWith(".approvalComment", StringComparison.OrdinalIgnoreCase) ||
			change.Property.EndsWith(".closeReason", StringComparison.OrdinalIgnoreCase) ||
			change.Property.EndsWith(".notes", StringComparison.OrdinalIgnoreCase));
		if (comment is not null && !IsEmptyValue(comment.After)) return comment.After;
		return action switch
		{
			"Created" => "Purchase order created",
			"Updated" => "Purchase order updated",
			_ => action
		};
	}

	private static bool IsEmptyValue(string value) =>
		string.IsNullOrWhiteSpace(value) ||
		value is "null" or "—";

	private static string DisplayStatus(PurchaseOrderStatus? status) => status switch
	{
		PurchaseOrderStatus.PendingApproval => "Pending Approval",
		PurchaseOrderStatus.PartiallyReceived => "Partially Received",
		null => "—",
		_ => status.Value.ToString()
	};
}
