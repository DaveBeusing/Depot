// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record PermissionDefinition(
	ApplicationPermission Permission,
	string Code,
	string Module,
	string Action,
	string Name);
