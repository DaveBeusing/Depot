using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DepotManager;

public partial class MainWindow : Window
{
	private readonly HttpClient _http = new();
	private readonly ManagerMutex _mutex = new();
	private CancellationTokenSource? _operation;
	private string? _validatedDatabaseFingerprint;
	private int _currentStep;
	private bool _setupInProgress;
	private InstallationService Installation => new(InstallPathBox.Text, Log);

	public MainWindow()
	{
		InitializeComponent();
		InstallPathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Depot");
		MaintenancePathBox.Text = InstallPathBox.Text;
		SqlitePathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Depot", "Data", "depot.db");
		if (!_mutex.Acquired)
		{
			MessageBox.Show("Another Depot Manager instance is already changing this installation.", "Depot Manager");
			Close();
			return;
		}
		Loaded += (_, _) =>
		{
			RefreshStatus();
			Log($"Depot Manager {GetManagerVersion()} started.");
		};
	}

	private void RefreshStatus()
	{
		var service = Installation;
		if (service.InstalledVersion is { } version)
		{
			StatusText.Text = $"Installed · {VersionRules.VersionText(version)}";
			MaintenancePathBox.Text = service.InstallDirectory;
			if (!service.IsProvisioned)
			{
				_setupInProgress = true;
				ShowInstallation();
				ShowStep(1);
			}
			else if (_setupInProgress)
			{
				ShowInstallation();
			}
			else
			{
				ShowMaintenance();
			}
		}
		else
		{
			StatusText.Text = "Not installed";
			_setupInProgress = false;
			ShowInstallation();
			ShowStep(0);
		}
	}

	private void ShowInstallation()
	{
		InstallationWizard.Visibility = Visibility.Visible;
		MaintenancePanel.Visibility = Visibility.Collapsed;
	}

	private void ShowMaintenance()
	{
		InstallationWizard.Visibility = Visibility.Collapsed;
		MaintenancePanel.Visibility = Visibility.Visible;
	}

	private void ShowStep(int step)
	{
		_currentStep = Math.Clamp(step, 0, 3);
		Step1Panel.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
		Step2Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
		Step3Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
		Step4Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
		var steps = new[] { Step1Text, Step2Text, Step3Text, Step4Text };
		for (var index = 0; index < steps.Length; index++)
		{
			steps[index].Foreground = index == _currentStep
				? (System.Windows.Media.Brush)FindResource("PrimaryTextBrush")
				: (System.Windows.Media.Brush)FindResource("SecondaryTextBrush");
			steps[index].FontWeight = index == _currentStep ? FontWeights.SemiBold : FontWeights.Normal;
		}
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
			Log($"{(repair ? "Repair" : installed is null ? "Install" : "Update")} requested. Source: {(installed is null ? "none" : VersionRules.VersionText(installed))}; target: {VersionRules.VersionText(release.Version)}.");
			if (!repair && installed is not null && !VersionRules.IsUpdate(installed, release.Version))
			{
				Log("The installed Depot version is already current.");
				return;
			}

			var temp = Path.Combine(Path.GetTempPath(), $"Depot-{VersionRules.VersionText(release.Version)}-{Guid.NewGuid():N}.exe");
			try
			{
				await client.DownloadAsync(release, temp, new Progress<int>(v => Progress.Value = v), _operation.Token);
				Log("Release download and validation completed.");
				service.Deploy(temp, release.Version, installed is not null);
				service.CopyManagerToInstallLocation();
				service.RegisterInstalledApp(release.Version);
			}
			finally { if (File.Exists(temp)) File.Delete(temp); }

			if (installed is null)
			{
				_setupInProgress = true;
				RefreshStatus();
				ShowStep(1);
				Log("Depot installed. Continue with database configuration.");
			}
			else
			{
				RefreshStatus();
				Log(repair ? "Repair completed." : "Update completed.");
			}
		}
		catch (Exception ex) { ShowError(ex); }
		finally { SetBusy(false); }
	}

	private async void TestConnection_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SetBusy(true);
			PrepareDatabaseTarget();
			await InvokeDepotManagerModeAsync("--manager-test-database", false, clearDatabasePassword: false);
			_validatedDatabaseFingerprint = CreateDatabaseFingerprint();
			DatabaseNextButton.IsEnabled = true;
			Log($"Database connection test succeeded for {DescribeDatabaseTarget()}.");
		}
		catch (Exception ex)
		{
			_validatedDatabaseFingerprint = null;
			DatabaseNextButton.IsEnabled = false;
			ShowError(ex);
		}
		finally { SetBusy(false); }
	}

	private void DatabaseNext_Click(object sender, RoutedEventArgs e)
	{
		if (!string.Equals(_validatedDatabaseFingerprint, CreateDatabaseFingerprint(), StringComparison.Ordinal))
		{
			DatabaseNextButton.IsEnabled = false;
			ShowError(new InvalidOperationException("Test the current database connection successfully before continuing."));
			return;
		}
		ShowStep(2);
	}

	private void Back_Click(object sender, RoutedEventArgs e)
	{
		if (_currentStep > 1) ShowStep(_currentStep - 1);
	}

	private async void Provision_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (AdminPasswordBox.Password != AdminConfirmBox.Password) throw new InvalidOperationException("Administrator passwords do not match.");
			if (!string.Equals(_validatedDatabaseFingerprint, CreateDatabaseFingerprint(), StringComparison.Ordinal))
				throw new InvalidOperationException("Test the current database connection successfully before provisioning.");
			SetBusy(true);
			PrepareDatabaseTarget();
			Log($"Provisioning {SelectedProviderName()} target {DescribeDatabaseTarget()}.");
			await InvokeDepotManagerModeAsync("--manager-provision", true, clearDatabasePassword: true);
			_validatedDatabaseFingerprint = null;
			var service = Installation;
			var installedVersionText = service.InstalledVersion is { } installedVersion ? VersionRules.VersionText(installedVersion) : "Unknown";
			var administratorText = string.IsNullOrWhiteSpace(AdminEmailBox.Text) ? "Existing database administrator" : AdminEmailBox.Text.Trim();
			SummaryText.Text = $"Version {installedVersionText}\n{SelectedProviderName()}\nAdministrator: {administratorText}";
			ShowStep(3);
			Log("Database initialization and administrator provisioning check completed successfully.");
		}
		catch (Exception ex) { ShowError(ex); }
		finally { SetBusy(false); }
	}

	private async Task InvokeDepotManagerModeAsync(string mode, bool includeAdministrator, bool clearDatabasePassword)
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
			if (includeAdministrator)
			{
				AdminPasswordBox.Password = string.Empty;
				AdminConfirmBox.Password = string.Empty;
			}
			if (clearDatabasePassword) DatabasePasswordBox.Password = string.Empty;
		}
	}

	private object BuildDatabaseSettings()
	{
		var provider = ProviderBox.SelectedIndex;
		int.TryParse(PortBox.Text, out var port);
		var requireTls = TlsBox.IsChecked == true;
		return new
		{
			Provider = provider == 0 ? 0 : provider == 1 ? 1 : 2,
			LocalDatabasePath = SqlitePathBox.Text.Trim(),
			SqlServerHost = provider == 1 ? HostBox.Text.Trim() : string.Empty,
			SqlServerPort = provider == 1 ? (port == 0 ? 1433 : port) : 1433,
			SqlServerDatabase = provider == 1 ? DatabaseBox.Text.Trim() : string.Empty,
			SqlServerUserName = provider == 1 ? UserBox.Text.Trim() : string.Empty,
			SqlServerPassword = provider == 1 ? DatabasePasswordBox.Password : string.Empty,
			EncryptSqlServerConnection = requireTls,
			TrustSqlServerCertificate = false,
			MySqlHost = provider == 2 ? HostBox.Text.Trim() : string.Empty,
			MySqlPort = provider == 2 ? (port == 0 ? 3306 : port) : 3306,
			MySqlDatabase = provider == 2 ? DatabaseBox.Text.Trim() : string.Empty,
			MySqlUserName = provider == 2 ? UserBox.Text.Trim() : string.Empty,
			MySqlPassword = provider == 2 ? DatabasePasswordBox.Password : string.Empty,
			UseMySqlTls = requireTls,
			AutomaticBackupsEnabled = false,
			BackupDirectory = "Backups",
			BackupIntervalDays = 1
		};
	}

	private string CreateDatabaseFingerprint()
	{
		var material = string.Join("\u001f", ProviderBox.SelectedIndex, SqlitePathBox.Text.Trim(), HostBox.Text.Trim(), PortBox.Text.Trim(), DatabaseBox.Text.Trim(), UserBox.Text.Trim(), DatabasePasswordBox.Password, TlsBox.IsChecked == true);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
	}

	private void PrepareDatabaseTarget()
	{
		if (ProviderBox.SelectedIndex != 0) return;
		var path = Path.GetFullPath(SqlitePathBox.Text.Trim());
		var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Choose a valid SQLite database file.");
		Directory.CreateDirectory(directory);
		var probe = Path.Combine(directory, $".depot-write-{Guid.NewGuid():N}.tmp");
		try { File.WriteAllBytes(probe, []); }
		catch (Exception exception) { throw new InvalidOperationException("Depot cannot write to the selected SQLite database directory.", exception); }
		finally { if (File.Exists(probe)) File.Delete(probe); }
	}

	private string DescribeDatabaseTarget() => ProviderBox.SelectedIndex switch
	{
		1 => $"{HostBox.Text.Trim()} / {DatabaseBox.Text.Trim()}",
		2 => $"{HostBox.Text.Trim()}:{PortBox.Text.Trim()} / {DatabaseBox.Text.Trim()}",
		_ => Path.GetFullPath(SqlitePathBox.Text.Trim())
	};

	private static string GetManagerVersion()
	{
		var fileVersion = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? string.Empty).FileVersion;
		return Version.TryParse(fileVersion, out var version) ? VersionRules.VersionText(version) : "unknown";
	}

	private void Provider_Changed(object sender, SelectionChangedEventArgs e)
	{
		_validatedDatabaseFingerprint = null;
		if (DatabaseNextButton is not null) DatabaseNextButton.IsEnabled = false;
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
			_setupInProgress = false;
			RefreshStatus();
			Log("Depot application files removed. Configuration and data were preserved.");
		}
		catch (Exception ex) { ShowError(ex); }
	}

	private void SetBusy(bool busy)
	{
		if (busy) _operation = new CancellationTokenSource();
		else { _operation?.Dispose(); _operation = null; }
		InstallationWizard.IsEnabled = !busy;
		MaintenancePanel.IsEnabled = !busy;
	}

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
