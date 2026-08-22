// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

namespace Depot.Services;

public static class LegacyAdministratorRetirement
{
	public static int Retire(DatabaseAccess database)
	{
		ArgumentNullException.ThrowIfNull(database);
		return database.Execute(
			"UPDATE Users SET IsActive = 0, Version = Version + 1 WHERE LOWER(Email) = LOWER($Email) AND IsActive = 1;",
			new DatabaseParameter("$Email", AdministratorBootstrapService.LegacyAdministratorEmail));
	}
}
