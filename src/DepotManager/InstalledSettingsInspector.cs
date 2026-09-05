using System.IO;
using Depot.Models;
using Depot.Repositories;

namespace DepotManager;

internal static class InstalledSettingsInspector
{
	public static string? GetLocalDatabasePath(string settingsPath, string installDirectory)
	{
		if (!File.Exists(settingsPath)) return null;

		var settings = new SettingsRepository(settingsPath).Load();
		if (settings.Provider != DatabaseProvider.Local || string.IsNullOrWhiteSpace(settings.LocalDatabasePath)) return null;

		return Path.IsPathRooted(settings.LocalDatabasePath)
			? Path.GetFullPath(settings.LocalDatabasePath)
			: Path.GetFullPath(settings.LocalDatabasePath, installDirectory);
	}
}
