using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace DepotManager;

public sealed record ReleaseInfo(Version Version, string Tag, string AssetName, Uri DownloadUri, long Size, string? Sha256);

public static class VersionRules
{
	public static bool TryParseReleaseTag(string tag, out Version version) => Version.TryParse(tag.Trim().TrimStart('v'), out version!);
	public static string AssetName(Version version) => $"Depot-{version}.exe";
	public static bool IsUpdate(Version installed, Version remote) => remote > installed;
}

public sealed class GitHubReleaseClient(HttpClient httpClient)
{
	private readonly HttpClient _http = httpClient;
	public async Task<ReleaseInfo> GetLatestAsync(CancellationToken token)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/DaveBeusing/Depot/releases?per_page=20");
		request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DepotManager", "1.0"));
		using var response = await _http.SendAsync(request, token);
		response.EnsureSuccessStatusCode();
		using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
		foreach (var release in document.RootElement.EnumerateArray())
		{
			if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
			var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
			if (!VersionRules.TryParseReleaseTag(tag, out var version)) continue;
			var expected = VersionRules.AssetName(version);
			foreach (var asset in release.GetProperty("assets").EnumerateArray())
			{
				if (!string.Equals(asset.GetProperty("name").GetString(), expected, StringComparison.Ordinal)) continue;
				var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
				return new ReleaseInfo(version, tag, expected, new Uri(asset.GetProperty("browser_download_url").GetString()!), asset.GetProperty("size").GetInt64(), digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null);
			}
		}
		throw new InvalidOperationException("No valid Depot release with the expected single-file asset was found.");
	}

	public async Task DownloadAsync(ReleaseInfo release, string destination, IProgress<int>? progress, CancellationToken token)
	{
		using var response = await _http.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token);
		response.EnsureSuccessStatusCode();
		await using var source = await response.Content.ReadAsStreamAsync(token);
		await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
		var buffer = new byte[1024 * 1024]; long total = 0; int read;
		while ((read = await source.ReadAsync(buffer, token)) > 0) { await target.WriteAsync(buffer.AsMemory(0, read), token); total += read; if (release.Size > 0) progress?.Report((int)Math.Min(100, total * 100 / release.Size)); }
		await target.FlushAsync(token);
		if (total <= 0 || (release.Size > 0 && total != release.Size)) throw new InvalidOperationException("The downloaded Depot asset is incomplete.");
		ValidatePortableExecutable(destination);
		if (!string.IsNullOrWhiteSpace(release.Sha256))
		{
			await using var stream = File.OpenRead(destination); var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
			if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The downloaded Depot asset failed SHA-256 validation.");
		}
	}

	private static void ValidatePortableExecutable(string path)
	{
		using var stream = File.OpenRead(path); using var reader = new PEReader(stream);
		if (!reader.HasMetadata || reader.PEHeaders.PEHeader is null) throw new InvalidOperationException("The downloaded asset is not a valid managed Windows executable.");
	}
}

public sealed class InstallationService(string installDirectory, Action<string> log)
{
	public string InstallDirectory { get; } = Path.GetFullPath(installDirectory);
	public string DepotPath => Path.Combine(InstallDirectory, "Depot.exe");
	public string ManagerPath => Path.Combine(InstallDirectory, "DepotManager.exe");
	public string BackupDirectory => Path.Combine(InstallDirectory, "Backup");
	public string? InstalledVersionText => File.Exists(DepotPath) ? FileVersionInfo.GetVersionInfo(DepotPath).FileVersion : null;
	public Version? InstalledVersion => Version.TryParse(InstalledVersionText, out var version) ? version : null;

	public void EnsureDepotStopped()
	{
		var processes = Process.GetProcessesByName("Depot").Where(p => { try { return string.Equals(Path.GetFullPath(p.MainModule?.FileName ?? string.Empty), DepotPath, StringComparison.OrdinalIgnoreCase); } catch { return false; } }).ToArray();
		if (processes.Length > 0) throw new InvalidOperationException("Depot is currently running. Close Depot normally and try again.");
	}

	public void Deploy(string downloadedFile, Version targetVersion, bool createBackup)
	{
		Directory.CreateDirectory(InstallDirectory); Directory.CreateDirectory(BackupDirectory); EnsureDepotStopped();
		if (createBackup && File.Exists(DepotPath))
		{
			foreach (var old in Directory.EnumerateFiles(BackupDirectory, "Depot-*.exe")) File.Delete(old);
			var current = InstalledVersion ?? new Version(0,0,0,0);
			File.Copy(DepotPath, Path.Combine(BackupDirectory, $"Depot-{current}.exe"), true);
		}
		var staged = DepotPath + ".new"; File.Copy(downloadedFile, staged, true);
		try { File.Move(staged, DepotPath, true); }
		catch { if (File.Exists(staged)) File.Delete(staged); throw; }
		var installed = InstalledVersion;
		if (installed is null || installed.Major != targetVersion.Major || installed.Minor != targetVersion.Minor || installed.Build != targetVersion.Build) throw new InvalidOperationException("The installed Depot executable does not match the target release version.");
		log($"Deployed Depot {targetVersion}.");
	}

	public void CopyManagerToInstallLocation()
	{
		var source = Environment.ProcessPath ?? throw new InvalidOperationException("Manager executable path is unavailable.");
		if (!string.Equals(Path.GetFullPath(source), ManagerPath, StringComparison.OrdinalIgnoreCase)) File.Copy(source, ManagerPath, true);
	}

	public void RegisterInstalledApp(Version version)
	{
		using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Depot");
		key.SetValue("DisplayName", "Depot"); key.SetValue("DisplayVersion", version.ToString(3)); key.SetValue("Publisher", "David Beusing"); key.SetValue("InstallLocation", InstallDirectory); key.SetValue("DisplayIcon", DepotPath); key.SetValue("UninstallString", $"\"{ManagerPath}\""); key.SetValue("ModifyPath", $"\"{ManagerPath}\""); key.SetValue("NoModify", 0, RegistryValueKind.DWord); key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
	}

	public void StartDepot() => Process.Start(new ProcessStartInfo(DepotPath) { WorkingDirectory = InstallDirectory, UseShellExecute = true });
	public void Uninstall(bool removeConfiguration)
	{
		EnsureDepotStopped();
		if (File.Exists(DepotPath)) File.Delete(DepotPath); if (Directory.Exists(BackupDirectory)) Directory.Delete(BackupDirectory, true);
		if (removeConfiguration) { var settings = Path.Combine(InstallDirectory, "depot.settings"); if (File.Exists(settings)) File.Delete(settings); }
		Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Depot", false);
	}
}

public sealed class ManagerMutex : IDisposable
{
	private readonly Mutex _mutex; public bool Acquired { get; }
	public ManagerMutex() { _mutex = new Mutex(true, @"Local\DepotManager.InstallationLock", out var created); Acquired = created; }
	public void Dispose() { if (Acquired) _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
