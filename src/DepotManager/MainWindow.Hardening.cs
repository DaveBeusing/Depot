using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Depot.Models;
using Depot.Repositories;
using Microsoft.Win32;

namespace DepotManager;

public partial class MainWindow
{
    private ReleaseInfo? _latestDepotRelease;
    private DepotReleaseMetadata? _latestDepotMetadata;
    private ManagerReleaseInfo? _latestManagerRelease;
    private InstallationSnapshot? _lastSnapshot;

    private async void HardeningWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ManagerVersionText.Text = $"Depot Manager {VersionRules.VersionText(GetRunningManagerVersion())}";
        var registered = InstallationInspector.GetRegisteredInstallDirectory();
        if (!string.IsNullOrWhiteSpace(registered))
        {
            InstallPathBox.Text = registered;
            MaintenancePathBox.Text = registered;
            RefreshStatus();
        }
        await RefreshMaintenanceStateAsync();
    }

    private void BrowseInstallLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose Depot installation folder",
            InitialDirectory = Directory.Exists(InstallPathBox.Text) ? InstallPathBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (dialog.ShowDialog() == true) InstallPathBox.Text = dialog.FolderName;
    }

    private void BrowseSqlitePath_Click(object sender, RoutedEventArgs e)
    {
        var current = SqlitePathBox.Text.Trim();
        var dialog = new SaveFileDialog
        {
            Title = "Choose local Depot database",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".db",
            FileName = string.IsNullOrWhiteSpace(current) ? "depot.db" : Path.GetFileName(current),
            InitialDirectory = !string.IsNullOrWhiteSpace(current) && Directory.Exists(Path.GetDirectoryName(current))
                ? Path.GetDirectoryName(current)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Depot", "Data")
        };
        if (dialog.ShowDialog(this) == true) SqlitePathBox.Text = dialog.FileName;
    }

    private async void InstallHardening_Click(object sender, RoutedEventArgs e)
    {
        await InstallOrUpdateAsync(false, false);
        if (Installation.InstalledVersion is not null)
        {
            var integration = new WindowsIntegrationService();
            integration.SetDesktopShortcutPreference(CreateDesktopShortcutBox.IsChecked == true);
            await RefreshMaintenanceStateAsync();
        }
    }

    private async void UpdateHardening_Click(object sender, RoutedEventArgs e)
    {
        string? safetyBackup = null;
        try
        {
            SetBusy(true);
            var service = Installation;
            var installed = service.InstalledVersion ?? throw new InvalidOperationException("No existing Depot installation was found.");
            var releaseClient = new GitHubReleaseClient(_http);
            var release = _latestDepotRelease ?? await releaseClient.GetLatestAsync(_operation!.Token);
            if (!VersionRules.IsUpdate(installed, release.Version))
            {
                Log("The installed Depot version is already current.");
                return;
            }

            var metadata = await new DepotReleaseMetadataClient(_http).GetAsync(release, _operation!.Token);
            if (metadata.ManagerCommandProtocol < 1)
                throw new InvalidOperationException("The selected Depot release does not declare the required manager health-check protocol.");

            var settings = LoadInstalledSettings(service);
            await new ManagerDatabaseConnectionValidator().ValidateAsync(settings, _operation.Token);
            var currentSchema = await new DatabaseSchemaInspector().ReadSchemaVersionAsync(settings, _operation.Token);
            if (metadata.DatabaseSchemaVersion < currentSchema)
                throw new InvalidOperationException($"Update blocked because release schema {metadata.DatabaseSchemaVersion} is older than database schema {currentSchema}.");

            if (metadata.DatabaseSchemaVersion > currentSchema)
            {
                if (settings.Provider == DatabaseProvider.Local)
                {
                    safetyBackup = await new MigrationSafetyService().CreateSqliteSafetyBackupAsync(
                        settings, service.InstallDirectory, installed, currentSchema, _operation.Token);
                    Log($"SQLite migration safety backup created: {safetyBackup}");
                }
                else
                {
                    var confirmation = MessageBox.Show(
                        $"Depot {VersionRules.VersionText(release.Version)} requires database schema {metadata.DatabaseSchemaVersion}; the current schema is {currentSchema}.\n\n" +
                        "For remote SQL Server or MySQL/MariaDB databases, Depot Manager does not assume server backup privileges. Confirm that a current server-side backup exists before continuing.",
                        "Database backup required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (confirmation != MessageBoxResult.Yes)
                    {
                        Log("Update cancelled because a required remote database backup was not confirmed.");
                        return;
                    }
                }
            }

            var confirmationText = $"Update Depot from {VersionRules.VersionText(installed)} to {VersionRules.VersionText(release.Version)}?\n\n" +
                $"Database schema: {currentSchema} → {metadata.DatabaseSchemaVersion}" +
                (metadata.DatabaseSchemaVersion > currentSchema ? "\nA database migration will be performed." : "\nNo database migration is required.");
            if (MessageBox.Show(confirmationText, "Depot update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

            Progress.Value = 0;
            var temp = Path.Combine(Path.GetTempPath(), $"Depot-{VersionRules.VersionText(release.Version)}-{Guid.NewGuid():N}.exe");
            try
            {
                await releaseClient.DownloadAsync(release, temp, new Progress<int>(value => Progress.Value = value), _operation.Token);
                service.Deploy(temp, release.Version, createBackup: true);
                RollbackMetadataService.Write(service.BackupDirectory, installed, currentSchema);
                if (metadata.DatabaseSchemaVersion > currentSchema)
                    await InvokeDepotMaintenanceModeAsync("--manager-migrate", _operation.Token);
                await InvokeDepotMaintenanceModeAsync("--manager-health-check", _operation.Token);
                service.CopyManagerToInstallLocation();
                new WindowsIntegrationService().Repair(service, release.Version);
                Progress.Value = 100;
                Log($"Depot update to {VersionRules.VersionText(release.Version)} completed and health check passed.");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        catch (Exception exception)
        {
            var backupNote = safetyBackup is null ? string.Empty : $"\n\nThe migration safety backup was preserved at:\n{safetyBackup}";
            Log($"Depot update failed: {exception.Message}");
            MessageBox.Show(exception.Message + backupNote, "Depot Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshMaintenanceStateAsync();
        }
    }

    private async void RepairHardening_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            var inspector = new InstallationInspector();
            var snapshot = await inspector.InspectAsync(InstallPathBox.Text, _operation!.Token);
            if (!string.Equals(snapshot.InstallDirectory, InstallPathBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                InstallPathBox.Text = snapshot.InstallDirectory;
                MaintenancePathBox.Text = snapshot.InstallDirectory;
            }
            var service = Installation;
            var integration = new WindowsIntegrationService();
            var targetVersion = snapshot.DepotVersion ?? integration.GetRegisteredDepotVersion();
            var releaseClient = new GitHubReleaseClient(_http);
            ReleaseInfo release;
            if (targetVersion is null)
            {
                release = await releaseClient.GetLatestAsync(_operation.Token);
                if (MessageBox.Show(
                    $"The installed Depot version cannot be determined. Repair can restore the latest stable release {VersionRules.VersionText(release.Version)}. Continue?",
                    "Depot repair",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            else
            {
                release = await releaseClient.GetAsync(targetVersion, _operation.Token);
            }

            var temp = Path.Combine(Path.GetTempPath(), $"Depot-repair-{Guid.NewGuid():N}.exe");
            try
            {
                await releaseClient.DownloadAsync(release, temp, new Progress<int>(value => Progress.Value = value), _operation.Token);
                service.Deploy(temp, release.Version, createBackup: false);
                service.CopyManagerToInstallLocation();
                integration.Repair(service, release.Version);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }

            var repaired = await inspector.InspectAsync(service.InstallDirectory, _operation.Token);
            if (repaired.State is InstallationHealthState.InstalledHealthy or InstallationHealthState.RepairRecommended)
            {
                Log("Repair completed and the installation health check passed.");
            }
            else
            {
                Log($"Application repair completed, but additional recovery is required: {repaired.Message}");
                MessageBox.Show(repaired.Message, "Repair requires attention", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
            await RefreshMaintenanceStateAsync();
        }
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            var snapshot = _lastSnapshot ?? await new InstallationInspector().InspectAsync(InstallPathBox.Text, _operation!.Token);
            if (snapshot.DatabaseSchemaVersion is null) throw new InvalidOperationException("Rollback is unavailable because the current database schema cannot be determined.");
            var candidate = RollbackMetadataService.Read(Installation.BackupDirectory)
                ?? throw new InvalidOperationException("No previous Depot executable backup is available.");
            RollbackMetadataService.EnsureCompatible(candidate, snapshot.DatabaseSchemaVersion.Value);
            if (MessageBox.Show(
                $"Roll back Depot to {VersionRules.VersionText(candidate.Version)}?\n\nDatabase schema {snapshot.DatabaseSchemaVersion} is compatible. Database schema downgrade is not performed.",
                "Depot rollback",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            RollbackMetadataService.Apply(Installation, candidate, snapshot.DatabaseSchemaVersion.Value);
            var settings = LoadInstalledSettings(Installation);
            await new ManagerDatabaseConnectionValidator().ValidateAsync(settings, _operation.Token);
            Log($"Depot rolled back to {VersionRules.VersionText(candidate.Version)}. Database connectivity check passed.");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
            await RefreshMaintenanceStateAsync();
        }
    }

    private async void UpdateManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            var client = new ManagerReleaseClient(_http);
            var release = _latestManagerRelease ?? await client.GetLatestAsync(_operation!.Token);
            var running = GetRunningManagerVersion();
            if (!ManagerReleaseClient.IsUpdateAvailable(running, release.Version))
            {
                Log("Depot Manager is already current.");
                return;
            }
            if (MessageBox.Show(
                $"Update Depot Manager from {VersionRules.VersionText(running)} to {VersionRules.VersionText(release.Version)}?\n\nThe manager will restart automatically.",
                "Depot Manager update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) != MessageBoxResult.Yes) return;

            var service = Installation;
            if (!File.Exists(service.ManagerPath)) service.CopyManagerToInstallLocation();
            await new ManagerSelfUpdateService().StageAndLaunchAsync(
                client, release, service.ManagerPath, new Progress<int>(value => Progress.Value = value), _operation.Token);
            Log($"Depot Manager {VersionRules.VersionText(release.Version)} staged. Restarting.");
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            if (_operation is not null) SetBusy(false);
        }
    }

    private async void RefreshMaintenance_Click(object sender, RoutedEventArgs e) => await RefreshMaintenanceStateAsync();

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var diagnostics = new ManagerDiagnosticsService();
        Directory.CreateDirectory(diagnostics.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", diagnostics.LogDirectory) { UseShellExecute = true });
    }

    private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = _lastSnapshot ?? await new InstallationInspector().InspectAsync(InstallPathBox.Text, CancellationToken.None);
            var service = new ManagerDiagnosticsService();
            Clipboard.SetText(service.ToJson(service.CreateDocument(snapshot, GetRunningManagerVersion())));
            Log("Diagnostics copied to the clipboard without credentials or connection strings.");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void CreateSupportPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = _lastSnapshot ?? await new InstallationInspector().InspectAsync(InstallPathBox.Text, CancellationToken.None);
            var dialog = new SaveFileDialog
            {
                Title = "Create Depot support package",
                Filter = "ZIP archive (*.zip)|*.zip",
                AddExtension = true,
                DefaultExt = ".zip",
                FileName = $"DepotSupport-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip"
            };
            if (dialog.ShowDialog(this) != true) return;
            var path = new ManagerDiagnosticsService().CreateSupportPackage(dialog.FileName, snapshot, GetRunningManagerVersion());
            Log($"Support package created: {path}");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task RefreshMaintenanceStateAsync()
    {
        try
        {
            var snapshot = await new InstallationInspector().InspectAsync(InstallPathBox.Text, CancellationToken.None);
            _lastSnapshot = snapshot;
            if (!string.Equals(snapshot.InstallDirectory, InstallPathBox.Text, StringComparison.OrdinalIgnoreCase) && snapshot.RegistryPresent)
            {
                InstallPathBox.Text = snapshot.InstallDirectory;
                MaintenancePathBox.Text = snapshot.InstallDirectory;
            }

            InstalledDepotVersionText.Text = snapshot.DepotVersion is null ? "Not installed" : VersionRules.VersionText(snapshot.DepotVersion);
            InstalledManagerVersionText.Text = snapshot.InstalledManagerVersion is null ? "Missing" : VersionRules.VersionText(snapshot.InstalledManagerVersion);
            DatabaseSchemaText.Text = snapshot.DatabaseSchemaVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
            InstallationHealthText.Text = $"{snapshot.State} · {snapshot.Message}";
            StartDepotButton.IsEnabled = snapshot.CanStartDepot;
            RepairHardeningButton.IsEnabled = snapshot.IsInstalled;
            ApplyHardeningStatus(snapshot.State, snapshot.Message);

            var rollback = RollbackMetadataService.Read(Path.Combine(snapshot.InstallDirectory, "Backup"));
            RollbackButton.IsEnabled = rollback is { IsValid: true } && snapshot.DatabaseSchemaVersion == rollback.SupportedSchemaVersion;
            RollbackInfoText.Text = rollback is null ? "No rollback backup available." : rollback.IsValid
                ? $"Rollback: Depot {VersionRules.VersionText(rollback.Version)} · schema {rollback.SupportedSchemaVersion}"
                : rollback.Message;

            if (!snapshot.IsInstalled)
            {
                AvailableDepotVersionText.Text = "—";
                AvailableManagerVersionText.Text = "—";
                UpdateHardeningButton.IsEnabled = false;
                ManagerUpdateButton.IsEnabled = false;
                return;
            }

            try
            {
                _latestDepotRelease = await new GitHubReleaseClient(_http).GetLatestAsync(CancellationToken.None);
                AvailableDepotVersionText.Text = VersionRules.VersionText(_latestDepotRelease.Version);
                UpdateHardeningButton.IsEnabled = snapshot.DepotVersion is not null && VersionRules.IsUpdate(snapshot.DepotVersion, _latestDepotRelease.Version);
                try
                {
                    _latestDepotMetadata = await new DepotReleaseMetadataClient(_http).GetAsync(_latestDepotRelease, CancellationToken.None);
                    ReleaseInfoText.Text = BuildReleaseSummary(_latestDepotMetadata, snapshot.DatabaseSchemaVersion);
                }
                catch (Exception metadataException)
                {
                    _latestDepotMetadata = null;
                    ReleaseInfoText.Text = $"Release migration metadata unavailable: {metadataException.Message}";
                }
            }
            catch (Exception exception)
            {
                AvailableDepotVersionText.Text = "Unavailable";
                UpdateHardeningButton.IsEnabled = false;
                ReleaseInfoText.Text = $"Depot release information unavailable: {exception.Message}";
            }

            try
            {
                _latestManagerRelease = await new ManagerReleaseClient(_http).GetLatestAsync(CancellationToken.None);
                AvailableManagerVersionText.Text = VersionRules.VersionText(_latestManagerRelease.Version);
                ManagerUpdateButton.IsEnabled = ManagerReleaseClient.IsUpdateAvailable(GetRunningManagerVersion(), _latestManagerRelease.Version);
            }
            catch (Exception exception)
            {
                AvailableManagerVersionText.Text = "Unavailable";
                ManagerUpdateButton.IsEnabled = false;
                Log($"Manager release information unavailable: {exception.Message}");
            }
        }
        catch (Exception exception)
        {
            Log($"Installation inspection failed: {exception.Message}");
        }
    }

    private async Task<int?> InvokeDepotMaintenanceModeAsync(string mode, CancellationToken cancellationToken)
    {
        var service = Installation;
        if (!File.Exists(service.DepotPath)) throw new InvalidOperationException("Depot.exe is missing.");
        var responsePath = Path.Combine(Path.GetTempPath(), $"depot-maintenance-{Guid.NewGuid():N}.response.json");
        try
        {
            var startInfo = new ProcessStartInfo(service.DepotPath, $"{mode} \"{responsePath}\"")
            {
                WorkingDirectory = service.InstallDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Depot maintenance process could not be started.");
            await process.WaitForExitAsync(cancellationToken);
            if (!File.Exists(responsePath)) throw new InvalidOperationException("Depot maintenance did not return a result.");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(responsePath, cancellationToken));
            var success = document.RootElement.GetProperty("Success").GetBoolean();
            var message = document.RootElement.GetProperty("Message").GetString() ?? "Unknown Depot maintenance result.";
            if (!success || process.ExitCode != 0) throw new InvalidOperationException(message);
            if (document.RootElement.TryGetProperty("DatabaseSchemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number)
                return schema.GetInt32();
            return null;
        }
        finally
        {
            if (File.Exists(responsePath)) File.Delete(responsePath);
        }
    }

    private static DatabaseConnectionSettings LoadInstalledSettings(InstallationService service)
    {
        var repository = new SettingsRepository(service.SettingsPath);
        if (!repository.Exists()) throw new InvalidOperationException("Depot configuration is incomplete because depot.settings is missing.");
        return repository.Load();
    }

    private static Version GetRunningManagerVersion()
    {
        var text = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? string.Empty).FileVersion;
        return Version.TryParse(text, out var version) ? VersionRules.ReleaseVersion(version) : new Version(0, 0, 0);
    }

    private static string BuildReleaseSummary(DepotReleaseMetadata metadata, int? currentSchema)
    {
        var migration = currentSchema is null ? "Database schema unknown" : metadata.DatabaseSchemaVersion > currentSchema
            ? $"Migration required: schema {currentSchema} → {metadata.DatabaseSchemaVersion}"
            : $"Schema {currentSchema} is compatible; no migration required";
        var published = metadata.PublishedAt?.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) ?? "unknown date";
        var name = string.IsNullOrWhiteSpace(metadata.ReleaseName) ? $"Depot {VersionRules.VersionText(metadata.DepotVersion)}" : metadata.ReleaseName;
        return $"{name} · published {published}\n{migration}";
    }

    private void ApplyHardeningStatus(InstallationHealthState state, string message)
    {
        StatusText.Text = message;
        var brushPrefix = state switch
        {
            InstallationHealthState.InstalledHealthy => "Success",
            InstallationHealthState.RepairRecommended or InstallationHealthState.DatabaseMigrationRequired or InstallationHealthState.DatabaseUnavailable or InstallationHealthState.ProvisioningIncomplete => "Warning",
            _ => "Error"
        };
        StatusBadge.Background = (Brush)FindResource($"{brushPrefix}Brush");
        StatusBadge.BorderBrush = (Brush)FindResource($"{brushPrefix}ForegroundBrush");
        StatusText.Foreground = (Brush)FindResource($"{brushPrefix}ForegroundBrush");
    }
}
