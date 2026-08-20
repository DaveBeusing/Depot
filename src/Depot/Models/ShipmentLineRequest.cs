// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record ShipmentLineRequest(long InventoryReservationId, int Quantity);
