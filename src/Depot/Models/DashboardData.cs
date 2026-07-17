// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record DashboardData(
	DashboardSummary Summary,
	IReadOnlyList<DashboardRecentMovement> RecentMovements);
