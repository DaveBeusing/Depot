// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record SalesReservationRequest(
	long SalesOrderLineId,
	long InventoryId,
	int Quantity);
