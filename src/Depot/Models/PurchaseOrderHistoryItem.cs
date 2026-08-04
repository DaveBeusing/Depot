// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record PurchaseOrderHistoryItem(
	DateTime TimestampUtc,
	string PreviousStatus,
	string NewStatus,
	string ChangedBy,
	string Comment)
{
	public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
}
