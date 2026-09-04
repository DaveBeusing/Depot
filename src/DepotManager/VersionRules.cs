namespace DepotManager;

public static class VersionRules
{
	public static bool TryParseReleaseTag(string tag, out Version version)
	{
		if (Version.TryParse(tag.Trim().TrimStart('v'), out var parsed) && parsed is not null)
		{
			version = ReleaseVersion(parsed);
			return true;
		}

		version = new Version(0, 0, 0);
		return false;
	}

	public static Version ReleaseVersion(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
	public static string VersionText(Version version) => $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
	public static string AssetName(Version version) => $"Depot-{VersionText(version)}.exe";
	public static string BackupName(Version version) => AssetName(version);
	public static bool IsUpdate(Version installed, Version remote) => ReleaseVersion(remote) > ReleaseVersion(installed);
}
