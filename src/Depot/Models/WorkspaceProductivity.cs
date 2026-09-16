// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record WorkspaceProductivitySnapshot(
	IReadOnlyList<string> FavoriteRoutes,
	IReadOnlyList<string> RecentRoutes,
	string? DefaultLandingRoute);

public sealed record WorkspaceRouteDescriptor(
	string RouteId,
	string Title,
	string Subtitle,
	string IconData);

public sealed record WorkspaceShortcut(
	string RouteId,
	string Title,
	string Subtitle,
	string IconData,
	bool IsFavorite,
	bool IsDefault);
