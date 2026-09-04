using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DepotManager;

public partial class MainWindow : Window
{
	private readonly HttpClient _http = new();
	private readonly ManagerMutex _mutex = new();
	private CancellationTokenSource? _operation;
	private InstallationService Installation => new(InstallPathBox.Text, Log);

	public MainWindow()
	{
		InitializeComponent();
		InstallPathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Depot");
		SqlitePathBox.Text = Path.Combine(InstallPathBox.Text, "depot.db");
		if (!_mutex.Acquired)
		{
			MessageBox.Show("Another Depot Manager instance is already changing this installation.", "Depot Manager");
			Close();
			return;
		}
		Loaded += (_, _) => RefreshStatus();
	}

	private void RefreshStatus()
	{
		var service = Installation;
		StatusText.Text = service.InstalledVersion is { } version
			? $"Installed version: {VersionRules.VersionText(version)}"
			: "Depot is not installed at this location.";
	}

	private async void Install_Click(object sender, RoutedEventArgs e) => await InstallOrUpdateAsync(false, false);
	private async void Update_Click(object sender, RoutedEventArgs e) => await InstallOrUpdateAsync(true, false);
	private async void Repair_Click(object sender, RoutedEventArgs e) => await InstallOrUpdateAsync(true, true);

	private async Task InstallOrUpdateAsync(bool existingRequired, bool repair)
	{
		try
		{
			SetBusy(true);
			var service = Installation;
			var installed = service.InstalledVersion;
			if (existingRequired && installed is null) throw new InvalidOperationException("No existing Depot installation was found.");
			var client = new GitHubReleaseClient(_http);
			var release = repair
				? await client.GetAsync(installed!, _operation!.Token)
				: await client.GetLatestAsync(_operation!.Token);
			if (!repair && installed is not null && !VersionRules.IsUpdate(installed, release.Version))
			{
				Log("The installed Depot version is already current.");
				return;
			}

			var temp = Path.Combine(Path.GetTempPath(), $"Depot-{VersionRules.VersionText(release.Version)}-{Guid.NewGuid():N}.exe");
			try
			{
				await client.DownloadAsync(release, temp, new Progress<int>(v => Progress.Value = v), _operation.Token);
				service.Deploy(temp, release.Version, installed is not null);
				service.CopyManagerToInstallLocation();
				service.RegisterInstalledApp(release.Version);
			}
			finally { if (File.Exists(temp)) File.Delete(temp); }
			RefreshStatus();
			Wizard.SelectedIndex = installed is null ? 1 : 0;
			Log(repair ? "Repair completed." : installed is null ? "Depot installed. Configure the database and administrator before first start." : "Update completed.");
		}
		catch (Exception ex) { ShowError(ex); }
		finally { SetBusy(false); }
	}

	private async void TestConnection_Click(object sender, RoutedEventArgs e)
	{
		try { SetBusy(true); await InvokeDepotManagerModeAsync("--manager-test-database", false); Log("Database connection test succeeded."); }
		catch (Exception ex) { ShowError(ex); }
		finally { SetBusy(false); }
	}

	private async void Provision_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (AdminPasswordBox.Password != AdminConfirmBox.Password) throw new InvalidOperationException("Administrator passwords do not match.");
			if (string.IsNullOrWhiteSpace(AdminPasswordBox.Password)) throw new InvalidOperationException("Administrator password is required.");
			SetBusy(true);
			await InvokeDepotManagerModeAsync("--manager-provision", true);
			var service = Installation;
			SummaryText.Text = $"Depot successfully installed\n\nVersion: {service.InstalledVersion is { } version ? VersionRules.VersionText(version) : "Unknown"}\nDatabase: {SelectedProviderName()}\nAdministrator: {AdminEmailBox.Text.Trim()}";
			Wizard.SelectedIndex = 3;
			Log("Database and initial administrator provisioned successfully.");
		}
		catch (Exception ex) { ShowError(ex); }
		finally { SetBusy(false); }
	}

	private async Task InvokeDepotManagerModeAsync(string mode, bool includeAdministrator)
	{
		var service = Installation;
		if (!File.Exists(service.DepotPath)) throw new InvalidOperationException("Install Depot before database configuration.");
		Directory.CreateDirectory(service.InstallDirectory);
		var responsePath = Path.Combine(Path.GetTempPath(), $"depot-provision-{Guid.NewGuid():N}.response.json");
		var request = new
		{
			Database = BuildDatabaseSettings(),
			Administrator = new
			{
				DisplayName = includeAdministrator ? AdminNameBox.Text.Trim() : string.Empty,
				Email = includeAdministrator ? AdminEmailBox.Text.Trim() : string.Empty,
				Password = includeAdministrator ? AdminPasswordBox.Password : string.Empty
			}
		};
		try
		{
			var startInfo = new ProcessStartInfo(service.DepotPath, $"{mode} \"{responsePath}\"")
			{
				WorkingDirectory = service.InstallDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true
			};
			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Depot provisioning process could not be started.");
			await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request));
			process.StandardInput.Close();
			await process.WaitForExitAsync(_operation!.Token);
			if (!File.Exists(responsePath)) throw new InvalidOperationException("Depot provisioning did not return a result.");
			using var document = JsonDocument.Parse(await File.ReadAllTextAsync(responsePath));
			var success = document.RootElement.GetProperty("Success").GetBoolean();
			var message = document.RootElement.GetProperty("Message").GetString() ?? "Unknown provisioning result.";
			if (!success || process.ExitCode != 0) throw new InvalidOperationException(message);
		}
		finally
		{
			if (File.Exists(responsePath)) File.Delete(responsePath);
			AdminPasswordBox.Password = string.Empty;
			AdminConfirmBox.Password = string.Empty;
			DatabasePasswordBox.Password = string.Empty;
		}
	}

	private object BuildDatabaseSettings()
	{
		var provider = ProviderBox.SelectedIndex;
		int.TryParse(PortBox.Text, out var port);
		return new
		{
			Provider = provider == 0 ? 0 : provider == 1 ? 1 : 2,
			LocalDatabasePath = SqlitePathBox.Text.Trim(),
			SqlServerHost = provider == 1 ? HostBox.Text.Trim() : string.Empty,
			SqlServerPort = provider == 1 ? (port == 0 ? 1433 : port) : 1433,
			SqlServerDatabase = provider == 1 ? DatabaseBox.Text.Trim() : string.Empty,
			SqlServerUserName = provider == 1 ? UserBox.Text.Trim() : string.Empty,
			SqlServerPassword = provider == 1 ? DatabasePasswordBox.Password : string.Empty,
			EncryptSqlServerConnection = true,
			TrustSqlServerCertificate = false,
			MySqlHost = provider == 2 ? HostBox.Text.Trim() : string.Empty,
			MySqlPort = provider == 2 ? (port == 0 ? 3306 : port) : 3306,
			MySqlDatabase = provider == 2 ? DatabaseBox.Text.Trim() : string.Empty,
			MySqlUserName = provider == 2 ? UserBox.Text.Trim() : string.Empty,
			MySqlPassword = provider == 2 ? DatabasePasswordBox.Password : string.Empty,
			UseMySqlTls = true,
			AutomaticBackupsEnabled = false,
			BackupDirectory = "Backups",
			BackupIntervalDays = 1
		};
	}

	private void Provider_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (PortBox is null) return;
		PortBox.Text = ProviderBox.SelectedIndex == 2 ? "3306" : "1433";
	}

	private string SelectedProviderName() => ProviderBox.SelectedIndex switch { 1 => "Microsoft SQL Server", 2 => "MySQL / MariaDB", _ => "SQLite" };
	private void Start_Click(object sender, RoutedEventArgs e) { try { Installation.StartDepot(); } catch (Exception ex) { ShowError(ex); } }
	private void Uninstall_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (MessageBox.Show("Remove Depot application files? Database and business data will not be deleted.", "Depot Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
			Installation.Uninstall(false);
			RefreshStatus();
			Log("Depot application files removed. Configuration and data were preserved.");
		}
		catch (Exception ex) { ShowError(ex); }
	}
	private void SetBusy(bool busy) { if (busy) _operation = new CancellationTokenSource(); else { _operation?.Dispose(); _operation = null; } Wizard.IsEnabled = !busy; }
	private void Log(string text)
	{
		DetailText.Text = text;
		try
		{
			var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Depot", "Logs");
			Directory.CreateDirectory(dir);
			File.AppendAllText(Path.Combine(dir, "DepotManager.log"), $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
		}
		catch { }
	}
	private void ShowError(Exception ex) { Log($"Operation failed: {ex.Message}"); MessageBox.Show(ex.Message, "Depot Manager", MessageBoxButton.OK, MessageBoxImage.Error); }
	protected override void OnClosed(EventArgs e) { _operation?.Cancel(); _operation?.Dispose(); _mutex.Dispose(); _http.Dispose(); base.OnClosed(e); }
}
