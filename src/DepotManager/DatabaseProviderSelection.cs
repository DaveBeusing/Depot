namespace DepotManager;

internal static class ManagerDatabaseProviderSelection
{
	public static int ToDepotProviderIndex(int selectionIndex) => selectionIndex switch
	{
		0 => 0, // Local SQLite
		1 => 2, // Remote MySQL / MariaDB
		2 => 1, // Remote SQL Server
		_ => throw new ArgumentOutOfRangeException(nameof(selectionIndex))
	};

	public static int DefaultPort(int selectionIndex) => selectionIndex switch
	{
		1 => 3306,
		2 => 1433,
		_ => 0
	};

	public static bool RequiresAdministratorStep(int selectionIndex) => selectionIndex == 0;

	public static string DisplayName(int selectionIndex) => selectionIndex switch
	{
		0 => "Lokal (sqlite3)",
		1 => "Remote (MySQL/MariaDB)",
		2 => "Remote (SQL)",
		_ => "Unknown"
	};
}
