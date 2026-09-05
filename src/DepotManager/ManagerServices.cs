using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace DepotManager;

public sealed record ReleaseInfo(Version Version, string Tag, string AssetName, Uri DownloadUri, long Size, string? Sha256);

public sealed class GitHubReleaseClient(HttpClient httpClient)
{
	private readonly HttpClient _http = httpClient;

	public async Task<ReleaseInfo> GetLatestAsync(CancellationToken token)
	{
		using var request = CreateRequest("https://api.github.com/repos/DaveBeusing/Depot/releases?per_page=20");
		using var response = await _http.SendAsync(request, token);
		response.EnsureSuccessStatusCode();
		using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
		foreach (var release in document.RootElement.EnumerateArray())
		{
			if (TryReadRelease(release, null, out var result)) return result;
		}

		throw new InvalidOperationException("No installable published Depot release with the expected single-file asset was found.");
	}

	public async Task<ReleaseInfo> GetAsync(Version version, CancellationToken token)
	{
		var requested = VersionRules.ReleaseVersion(version);
		var tag = VersionRules.VersionText(requested);
		using var request = CreateRequest($"https://api.github.com/repos/DaveBeusing/Depot/releases/tags/{Uri.EscapeDataString(tag)}");
		using var response = await _http.SendAsync(request, token);
		if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
			throw new InvalidOperationException($"The published GitHub release for installed Depot version {tag} was not found.");
		response.EnsureSuccessStatusCode();
		using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
		if (!TryReadRelease(document.RootElement, requested, out var result))
			throw new InvalidOperationException($"Release {tag} is not an installable stable Depot release with a valid {VersionRules.AssetName(requested)} asset.");
		return result;
	}

	public async Task DownloadAsync(ReleaseInfo release, string destination, IProgress<int>? progress, CancellationToken token)
	{
		using var response = await _http.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token);
		response.EnsureSuccessStatusCode();
		await using var source = await response.Content.ReadAsStreamAsync(token);
		await using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
		{
			var buffer = new byte[1024 * 1024];
			long total = 0;
			int read;
			while ((read = await source.ReadAsync(buffer, token)) > 0)
			{
				await target.WriteAsync(buffer.AsMemory(0, read), token);
				total += read;
				progress?.Report((int)Math.Min(100, total * 100 / release.Size));
			}
			await target.FlushAsync(token);
			if (total <= 0 || total != release.Size) throw new InvalidOperationException("The downloaded Depot asset is incomplete.");
		}

		PortableExecutableValidator.ValidateWindowsExecutable(destination);
		if (!string.IsNullOrWhiteSpace(release.Sha256))
		{
			await using var stream = File.OpenRead(destination);
			var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
			if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The downloaded Depot asset failed SHA-256 validation.");
		}
	}

	private static HttpRequestMessage CreateRequest(string uri)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DepotManager", "1.0"));
		return request;
	}

	private static bool TryReadRelease(JsonElement release, Version? requestedVersion, out ReleaseInfo result)
	{
		result = null!;
		if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return false;
		var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
		if (!VersionRules.TryParseReleaseTag(tag, out var version)) return false;
		if (requestedVersion is not null && version != requestedVersion) return false;
		var expected = VersionRules.AssetName(version);
		foreach (var asset in release.GetProperty("assets").EnumerateArray())
		{
			if (!string.Equals(asset.GetProperty("name").GetString(), expected, StringComparison.Ordinal)) continue;
			var size = asset.GetProperty("size").GetInt64();
			if (size <= 0) return false;
			var download = asset.GetProperty("browser_download_url").GetString();
			if (!Uri.TryCreate(download, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps) return false;
			var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
			result = new ReleaseInfo(version, tag, expected, downloadUri, size, digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null);
			return true;
		}
		return false;
	}
}

public sealed class InstallationService(string installDirectory, Action<string> log)
{
	private const int MoveFileDelayUntilReboot = 0x4;
	public string InstallDirectory { get; } = Path.GetFullPath(installDirectory);
	public string DepotPath => Path.Combine(InstallDirectory, "Depot.exe");
	public string ManagerPath => Path.Combine(InstallDirectory, "DepotManager.exe");
	public string BackupDirectory => Path.Combine(InstallDirectory, "Backup");
	public string SettingsPath => Path.Combine(InstallDirectory, "depot.settings");
	public string? InstalledVersionText => File.Exists(DepotPath) ? FileVersionInfo.GetVersionInfo(DepotPath).FileVersion : null;
	public Version? InstalledVersion => Version.TryParse(InstalledVersionText, out var version) ? VersionRules.ReleaseVersion(version) : null;
	public bool IsProvisioned => File.Exists(SettingsPath);

	public void EnsureDepotStopped()
	{
		var processes = Process.GetProcessesByName("Depot").Where(p =>
		{
			try { return string.Equals(Path.GetFullPath(p.MainModule?.FileName ?? string.Empty), DepotPath, StringComparison.OrdinalIgnoreCase); }
			catch { return false; }
		}).ToArray();
		if (processes.Length > 0) throw new InvalidOperationException("Depot is currently running. Close Depot normally and try again.");
	}

	public void Deploy(string downloadedFile, Version targetVersion, bool createBackup)
	{
		ValidateTargetVersion(downloadedFile, targetVersion);
		Directory.CreateDirectory(InstallDirectory);
		Directory.CreateDirectory(BackupDirectory);
		EnsureDepotStopped();
		if (createBackup && File.Exists(DepotPath))
		{
			var current = InstalledVersion ?? new Version(0, 0, 0);
			ExecutableDeployment.BackupCurrent(DepotPath, BackupDirectory, current);
		}
		ExecutableDeployment.Replace(downloadedFile, DepotPath);
		log($"Deployed Depot {VersionRules.VersionText(targetVersion)}.");
	}

	public static void ValidateTargetVersion(string file, Version target)
	{
		var text = FileVersionInfo.GetVersionInfo(file).FileVersion;
		if (!Version.TryParse(text, out var actual) || VersionRules.ReleaseVersion(actual) != VersionRules.ReleaseVersion(target))
			throw new InvalidOperationException("The downloaded executable version does not match the selected release.");
	}

	public void CopyManagerToInstallLocation()
	{
		var source = Environment.ProcessPath ?? throw new InvalidOperationException("Manager executable path is unavailable.");
		ExecutableDeployment.InstallManagerCopy(source, ManagerPath);
		log($"Depot Manager installed at {ManagerPath}. The original download location is no longer required.");
	}

	public void RegisterInstalledApp(Version version)
	{
		if (!File.Exists(ManagerPath))
			throw new InvalidOperationException("Depot Manager must be installed in the Depot application directory before Windows integration is registered.");

		using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Depot");
		key.SetValue("DisplayName", "Depot");
		key.SetValue("DisplayVersion", VersionRules.VersionText(version));
		key.SetValue("Publisher", "David Beusing");
		key.SetValue("InstallLocation", InstallDirectory);
		key.SetValue("DisplayIcon", DepotPath);
		key.SetValue("UninstallString", $"\"{ManagerPath}\"");
		key.SetValue("ModifyPath", $"\"{ManagerPath}\"");
		key.SetValue("NoModify", 0, RegistryValueKind.DWord);
		key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
		try { CreateStartMenuShortcut(); }
		catch (Exception exception) { log($"Start menu shortcut could not be created: {exception.Message}"); }
	}

	public void CreateDesktopShortcut()
	{
		try
		{
			CreateShortcut(GetDesktopShortcutPath());
			log("Desktop shortcut created.");
		}
		catch (Exception exception)
		{
			log($"Desktop shortcut could not be created: {exception.Message}");
		}
	}

	public void StartDepot()
	{
		if (!IsProvisioned) throw new InvalidOperationException("Complete database and administrator provisioning before the first Depot start.");
		Process.Start(new ProcessStartInfo(DepotPath) { WorkingDirectory = InstallDirectory, UseShellExecute = true });
		log("Depot started. Closing Depot Manager.");
		var application = Application.Current;
		if (application is not null) application.Dispatcher.BeginInvoke(new Action(application.Shutdown));
	}

	public void Uninstall(bool removeConfiguration)
	{
		EnsureDepotStopped();
		if (File.Exists(DepotPath)) File.Delete(DepotPath);
		if (Directory.Exists(BackupDirectory)) Directory.Delete(BackupDirectory, true);
		if (removeConfiguration && File.Exists(SettingsPath)) File.Delete(SettingsPath);
		Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Depot", false);
		var startMenuShortcut = GetStartMenuShortcutPath();
		if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut);
		var desktopShortcut = GetDesktopShortcutPath();
		if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut);
		if (File.Exists(ManagerPath) && string.Equals(Path.GetFullPath(Environment.ProcessPath ?? string.Empty), ManagerPath, StringComparison.OrdinalIgnoreCase))
		{
			if (!MoveFileEx(ManagerPath, null, MoveFileDelayUntilReboot)) log("DepotManager.exe could not be scheduled for removal; the application data remains untouched.");
		}
		else if (File.Exists(ManagerPath)) File.Delete(ManagerPath);
	}

	private void CreateStartMenuShortcut() => CreateShortcut(GetStartMenuShortcutPath());

	private void CreateShortcut(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var shellType = Type.GetTypeFromProgID("WScript.Shell");
		if (shellType is null) throw new InvalidOperationException("Windows shortcut support is unavailable.");
		dynamic shell = Activator.CreateInstance(shellType)!;
		dynamic shortcut = shell.CreateShortcut(path);
		shortcut.TargetPath = DepotPath;
		shortcut.WorkingDirectory = InstallDirectory;
		shortcut.IconLocation = DepotPath + ",0";
		shortcut.Description = "Depot ERP";
		shortcut.Save();
	}

	private static string GetStartMenuShortcutPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Depot.lnk");
	private static string GetDesktopShortcutPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Depot.lnk");

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}

public sealed class ManagerMutex : IDisposable
{
	private readonly Mutex _mutex;
	public bool Acquired { get; }
	public ManagerMutex() { _mutex = new Mutex(true, @"Local\DepotManager.InstallationLock", out var created); Acquired = created; }
	public void Dispose() { if (Acquired) _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
