// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum PurchaseOrderStatus
{
	Draft = 1,
	Ordered = 2,
	PartiallyReceived = 3,
	Received = 4,
	Cancelled = 5,
	PendingApproval = 6,
	Approved = 7,
	Closed = 8,
	Rejected = 9
}
