// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class UserAuthentication
{
	public required User User { get; init; }

	public required string PasswordHash { get; init; }
}
