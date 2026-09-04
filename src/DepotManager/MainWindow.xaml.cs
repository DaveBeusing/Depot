using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DepotManager;

public partial class MainWindow : Window
{
	private readonly HttpClient _http = new();
	private readonly ManagerMutex _mutex = new();
	private CancellationTokenSource? _operation;
	private string? _validatedDatabaseFingerprint;
	private int _selectedProvider = -1;
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
			ApplyInstalledStatus(true);
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
			ApplyInstalledStatus(false);
			_setupInProgress = false;
			ShowInstallation();
			ShowStep(0);
		}
	}

	private void ApplyInstalledStatus(bool installed)
	{
		StatusBadge.Background = (Brush)FindResource(installed ? "SuccessBrush" : "ErrorBrush");
		StatusBadge.BorderBrush = (Brush)FindResource(installed ? "SuccessForegroundBrush" : "ErrorForegroundBrush");
		StatusText.Foreground = (Brush)FindResource(installed ? "SuccessForegroundBrush" : "ErrorForegroundBrush");
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
		_currentStep = Math.Clamp(step, 0, 4);
		Step1Panel.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
		Step2Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
		Step3Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
		Step4Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
		Step5Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

		var steps = new (TextBlock Text, int Step)[]
		{
			(Step1Text, 0),
			(Step2Text, 1),
			(Step3Text, 2),
			(Step4Text, 3),
			(Step5Text, 4)
		};

		foreach (var (text, stepIndex) in steps)
		{
			if (text.Visibility != Visibility.Visible) continue;
			text.Foreground = stepIndex == _currentStep
				? (Brush)FindResource("PrimaryTextBrush")
				: (Brush)FindResource("SecondaryTextBrush");
			text.FontWeight = stepIndex == _currentStep ? FontWeights.SemiBold : FontWeights.Normal;
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

			Progress.Value = 0;
			var temp = Path.Combine(Path.GetTempPath(), $"Depot-{VersionRules.VersionText(release.Version)}-{Guid.NewGuid():N}.exe");
			try
			{
				await client.DownloadAsync(release, temp, new Progress<int>(value => Progress.Value = value), _operation!.Token);
				Log("Release download and validation completed.");
				service.Deploy(temp, release.Version, installed is not null);
				service.CopyManagerToInstallLocation();
				service.RegisterInstalledApp(release.Version);
				Progress.Value = 100;
			}
			finally
			{
				if (File.Exists(temp)) File.Delete(temp);
			}

			if (installed is null)
			{
				_setupInProgress = true;
				RefreshStatus();
				ShowStep(1);
				Log("Depot installed. Choose a database type to continue.");
			}
			else
			{
				RefreshStatus();
				Log(repair ? "Repair completed." : "Update completed.");
			}
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void DatabaseChoice_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var selection)) return;
		_selectedProvider = selection;
		ConfigureProvider();
		InvalidateConnectionValidation();
		ShowStep(2);
		Log($"Database type selected: {SelectedProviderName()}.");
	}

	private void ConfigureProvider()
	{
		if (_selectedProvider < 0) return;
		var local = ManagerDatabaseProviderSelection.RequiresAdministratorStep(_selectedProvider);
		SelectedDatabaseText.Text = local
			? "Lokal (sqlite3) · choose the local database file and test access before continuing."
			: $"{SelectedProviderName()} · enter the server connection details and test the connection before continuing.";
		LocalDatabasePanel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
		RemoteDatabasePanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
		Step4Text.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
		Step5Text.Text = local ? "5   Ready" : "4   Ready";
		var defaultPort = ManagerDatabaseProviderSelection.DefaultPort(_selectedProvider);
		PortBox.Text = defaultPort == 0 ? string.Empty : defaultPort.ToString();
	}

	private void ConnectionInput_Changed(object sender, TextChangedEventArgs e) => InvalidateConnectionValidation();
	private void ConnectionPassword_Changed(object sender, RoutedEventArgs e) => InvalidateConnectionValidation();
	private void ConnectionOption_Changed(object sender, RoutedEventArgs e) => InvalidateConnectionValidation();

	private void InvalidateConnectionValidation()
	{
		_validatedDatabaseFingerprint = null;
		if (ConfigurationNextButton is not null) ConfigurationNextButton.IsEnabled = false;
		if (ConnectionResultBorder is not null) ConnectionResultBorder.Visibility = Visibility.Collapsed;
	}

	private async void TestConnection_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (_selectedProvider < 0) throw new InvalidOperationException("Choose a database type first.");
			SetBusy(true);
			PrepareDatabaseTarget();
			await InvokeDepotManagerModeAsync("--manager-test-database", false, clearDatabasePassword: false);
			_validatedDatabaseFingerprint = CreateDatabaseFingerprint();
			ConfigurationNextButton.IsEnabled = true;
			ShowConnectionFeedback(true, $"Connection successful · {DescribeDatabaseTarget()}");
			Log($"Database connection test succeeded for {DescribeDatabaseTarget()}.");
		}
		catch (Exception ex)
		{
			_validatedDatabaseFingerprint = null;
			ConfigurationNextButton.IsEnabled = false;
			ShowConnectionFeedback(false, $"Connection failed · {ex.Message}");
			Log($"Database connection test failed: {ex.Message}");
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void ShowConnectionFeedback(bool success, string message)
	{
		ConnectionResultBorder.Visibility = Visibility.Visible;
		ConnectionResultBorder.Background = (Brush)FindResource(success ? "SuccessBrush" : "ErrorBrush");
		ConnectionResultBorder.BorderBrush = (Brush)FindResource(success ? "SuccessForegroundBrush" : "ErrorForegroundBrush");
		ConnectionResultText.Foreground = (Brush)FindResource(success ? "SuccessForegroundBrush" : "ErrorForegroundBrush");
		ConnectionResultText.Text = message;
	}

	private void ConfigurationNext_Click(object sender, RoutedEventArgs e)
	{
		if (!ConnectionIsStillValidated())
		{
			ConfigurationNextButton.IsEnabled = false;
			ShowConnectionFeedback(false, "The connection details changed. Test the connection again before continuing.");
			return;
		}

		if (ManagerDatabaseProviderSelection.RequiresAdministratorStep(_selectedProvider))
		{
			ShowStep(3);
			return;
		}

		PrepareSummary("Existing remote administrator");
		ShowStep(4);
	}

	private void AdministratorNext_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			ValidateAdministratorInputs();
			if (!ConnectionIsStillValidated()) throw new InvalidOperationException("The database connection must still match the successfully tested connection.");
			PrepareSummary(AdminEmailBox.Text.Trim());
			ShowStep(4);
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private void PrepareSummary(string administrator)
	{
		var service = Installation;
		var installedVersionText = service.InstalledVersion is { } installedVersion ? VersionRules.VersionText(installedVersion) : "Unknown";
		SummaryText.Text = $"Version {installedVersionText}\n{SelectedProviderName()}\nAdministrator: {administrator}";
	}

	private void ValidateAdministratorInputs()
	{
		if (!ManagerDatabaseProviderSelection.RequiresAdministratorStep(_selectedProvider)) return;
		if (string.IsNullOrWhiteSpace(AdminNameBox.Text) || string.IsNullOrWhiteSpace(AdminEmailBox.Text) || string.IsNullOrWhiteSpace(AdminPasswordBox.PasswordValue))
			throw new InvalidOperationException("Enter the administrator name, e-mail / username and password.");
		if (AdminPasswordBox.PasswordValue != AdminConfirmBox.PasswordValue)
			throw new InvalidOperationException("Administrator passwords do not match.");
	}

	private bool ConnectionIsStillValidated() =>
		!string.IsNullOrEmpty(_validatedDatabaseFingerprint) &&
		string.Equals(_validatedDatabaseFingerprint, CreateDatabaseFingerprint(), StringComparison.Ordinal);

	private async void Continue_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (_selectedProvider < 0) throw new InvalidOperationException("Choose a database type first.");
			if (!ConnectionIsStillValidated()) throw new InvalidOperationException("Test the current database connection successfully before continuing.");
			ValidateAdministratorInputs();

			SetBusy(true);
			PrepareDatabaseTarget();
			var local = ManagerDatabaseProviderSelection.RequiresAdministratorStep(_selectedProvider);
			Log($"Completing {SelectedProviderName()} configuration for {DescribeDatabaseTarget()}.");
			await InvokeDepotManagerModeAsync("--manager-provision", local, clearDatabasePassword: true);
			Log("Depot configuration completed successfully. Starting Depot.");
			SetBusy(false);
			_setupInProgress = false;
			Installation.StartDepot();
			Close();
		}
		catch (Exception ex)
		{
			if (_operation is not null) SetBusy(false);
			ShowError(ex);
		}
	}

	private void Back_Click(object sender, RoutedEventArgs e)
	{
		switch (_currentStep)
		{
			case 2:
				ShowStep(1);
				break;
			case 3:
				ShowStep(2);
				break;
			case 4:
				ShowStep(ManagerDatabaseProviderSelection.RequiresAdministratorStep(_selectedProvider) ? 3 : 2);
				break;
		}
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
				Password = includeAdministrator ? AdminPasswordBox.PasswordValue : string.Empty
			}
		};

		try
		{
			var startInfo = new ProcessStartInfo(service.DepotPath, $"{mode} \"{responsePath}\"")
			{
				WorkingDirectory = service.InstallDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
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
				AdminPasswordBox.PasswordValue = string.Empty;
				AdminConfirmBox.PasswordValue = string.Empty;
			}
			if (clearDatabasePassword) DatabasePasswordBox.PasswordValue = string.Empty;
		}
	}

	private object BuildDatabaseSettings()
	{
		if (_selectedProvider < 0) throw new InvalidOperationException("Choose a database type first.");
		var provider = ManagerDatabaseProviderSelection.ToDepotProviderIndex(_selectedProvider);
		int.TryParse(PortBox.Text, out var port);
		var requireTls = TlsBox.IsChecked == true;
		return new
		{
			Provider = provider,
			LocalDatabasePath = SqlitePathBox.Text.Trim(),
			SqlServerHost = provider == 1 ? HostBox.Text.Trim() : string.Empty,
			SqlServerPort = provider == 1 ? (port == 0 ? 1433 : port) : 1433,
			SqlServerDatabase = provider == 1 ? DatabaseBox.Text.Trim() : string.Empty,
			SqlServerUserName = provider == 1 ? UserBox.Text.Trim() : string.Empty,
			SqlServerPassword = provider == 1 ? DatabasePasswordBox.PasswordValue : string.Empty,
			EncryptSqlServerConnection = requireTls,
			TrustSqlServerCertificate = false,
			MySqlHost = provider == 2 ? HostBox.Text.Trim() : string.Empty,
			MySqlPort = provider == 2 ? (port == 0 ? 3306 : port) : 3306,
			MySqlDatabase = provider == 2 ? DatabaseBox.Text.Trim() : string.Empty,
			MySqlUserName = provider == 2 ? UserBox.Text.Trim() : string.Empty,
			MySqlPassword = provider == 2 ? DatabasePasswordBox.PasswordValue : string.Empty,
			UseMySqlTls = requireTls,
			AutomaticBackupsEnabled = false,
			BackupDirectory = "Backups",
			BackupIntervalDays = 1
		};
	}

	private string CreateDatabaseFingerprint()
	{
		var material = string.Join("\u001f", _selectedProvider, SqlitePathBox.Text.Trim(), HostBox.Text.Trim(), PortBox.Text.Trim(), DatabaseBox.Text.Trim(), UserBox.Text.Trim(), DatabasePasswordBox.PasswordValue, TlsBox.IsChecked == true);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
	}

	private void PrepareDatabaseTarget()
	{
		if (_selectedProvider < 0 || !ManagerDatabaseProviderSelection.RequiresAdministratorStep(_selectedProvider)) return;
		var path = Path.GetFullPath(SqlitePathBox.Text.Trim());
		var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Choose a valid local database file.");
		Directory.CreateDirectory(directory);
		var probe = Path.Combine(directory, $".depot-write-{Guid.NewGuid():N}.tmp");
		try
		{
			File.WriteAllBytes(probe, []);
		}
		catch (Exception exception)
		{
			throw new InvalidOperationException("Depot cannot write to the selected local database directory.", exception);
		}
		finally
		{
			if (File.Exists(probe)) File.Delete(probe);
		}
	}

	private string DescribeDatabaseTarget()
	{
		if (_selectedProvider < 0) return "Unknown";
		var provider = ManagerDatabaseProviderSelection.ToDepotProviderIndex(_selectedProvider);
		return provider switch
		{
			1 => $"{HostBox.Text.Trim()}:{PortBox.Text.Trim()} / {DatabaseBox.Text.Trim()}",
			2 => $"{HostBox.Text.Trim()}:{PortBox.Text.Trim()} / {DatabaseBox.Text.Trim()}",
			_ => Path.GetFullPath(SqlitePathBox.Text.Trim())
		};
	}

	private static string GetManagerVersion()
	{
		var fileVersion = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? string.Empty).FileVersion;
		return Version.TryParse(fileVersion, out var version) ? VersionRules.VersionText(version) : "unknown";
	}

	private string SelectedProviderName() => _selectedProvider >= 0 ? ManagerDatabaseProviderSelection.DisplayName(_selectedProvider) : "Unknown";

	private void Start_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Installation.StartDepot();
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private void Uninstall_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (MessageBox.Show("Remove Depot application files? Database and business data will not be deleted.", "Depot Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
			Installation.Uninstall(false);
			_setupInProgress = false;
			_selectedProvider = -1;
			InvalidateConnectionValidation();
			RefreshStatus();
			Log("Depot application files removed. Configuration and data were preserved.");
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private void SetBusy(bool busy)
	{
		if (busy)
		{
			_operation = new CancellationTokenSource();
		}
		else
		{
			_operation?.Dispose();
			_operation = null;
		}

		InstallationWizard.IsEnabled = !busy;
		MaintenancePanel.IsEnabled = !busy;
	}

	private void Log(string text)
	{
		DetailText.Text = text;
		try
		{
			var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Depot", "Logs");
			Directory.CreateDirectory(directory);
			File.AppendAllText(Path.Combine(directory, "DepotManager.log"), $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	private void ShowError(Exception ex)
	{
		Log($"Operation failed: {ex.Message}");
		MessageBox.Show(ex.Message, "Depot Manager", MessageBoxButton.OK, MessageBoxImage.Error);
	}

	protected override void OnClosed(EventArgs e)
	{
		_operation?.Cancel();
		_operation?.Dispose();
		_mutex.Dispose();
		_http.Dispose();
		base.OnClosed(e);
	}
}
