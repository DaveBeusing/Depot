// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class User
{
	public long Id { get; set; }

	public string UserName { get; set; }
		= string.Empty;

	public string DisplayName { get; set; }
		= string.Empty;

	public bool IsAdministrator { get; set; }

	public bool IsActive { get; set; }

	public DateTime CreatedUtc { get; set; }
}
