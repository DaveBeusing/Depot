// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class DatabaseConnectionSettings
{
	public DatabaseProvider Provider { get; set; } = DatabaseProvider.Local;

	public string LocalDatabasePath { get; set; } = "depot.db";

	public string SqlServerHost { get; set; } = string.Empty;

	public int SqlServerPort { get; set; } = 1433;

	public string SqlServerDatabase { get; set; } = string.Empty;

	public string SqlServerUserName { get; set; } = string.Empty;

	public string SqlServerPassword { get; set; } = string.Empty;

	public bool EncryptSqlServerConnection { get; set; } = true;

	public bool TrustSqlServerCertificate { get; set; }
}
