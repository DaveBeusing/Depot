using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Depot.Models;

namespace DepotManager;

public partial class MainWindow
{
    private Button? _cancelOperationButton;
    private bool _completionUiInitialized;
    private bool _operationCancellationAllowed = true;

    internal void InitializeCompletionUi()
    {
        if (_completionUiInitialized) return;
        _completionUiInitialized = true;

        if (ManagerVersionText.Parent is Grid footer)
        {
            footer.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(ManagerVersionText, 2);
            _cancelOperationButton = new Button
            {
                Content = "Cancel",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Margin = new Thickness(16, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(_cancelOperationButton, 1);
            _cancelOperationButton.Click += CancelOperation_Click;
            footer.Children.Add(_cancelOperationButton);
        }

        InstallationWizard.IsEnabledChanged += OperationPanel_IsEnabledChanged;
        MaintenancePanel.IsEnabledChanged += OperationPanel_IsEnabledChanged;
        UpdateHardeningButton.Click -= UpdateHardening_Click;
        UpdateHardeningButton.Click += UpdateWithRecovery_Click;
        RewireDiagnosticsButtons();
        UpdateCancellationUi();
    }

    private void OperationPanel_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateCancellationUi();

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        var operation = _operation;
        if (operation is null || operation.IsCancellationRequested || !_operationCancellationAllowed) return;
        _operationCancellationAllowed = false;
        if (_cancelOperationButton is not null) _cancelOperationButton.IsEnabled = false;
        Log("Cancellation requested. Waiting for the current safe checkpoint…");
        operation.Cancel();
    }

    private void SetOperationCancellationAllowed(bool allowed)
    {
        _operationCancellationAllowed = allowed;
        UpdateCancellationUi();
    }

    private void UpdateCancellationUi()
    {
        if (_cancelOperationButton is null) return;
        var busy = !InstallationWizard.IsEnabled && !MaintenancePanel.IsEnabled;
        if (!busy) _operationCancellationAllowed = true;
        _cancelOperationButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _cancelOperationButton.IsEnabled = busy && _operationCancellationAllowed && _operation is { IsCancellationRequested: false };
    }

    private async void UpdateWithRecovery_Click(object sender, RoutedEventArgs e)
    {
        string? safetyBackup = null;
        InstallationService? installation = null;
        var deployedNewExecutable = false;
        try
        {
            SetBusy(true);
            SetOperationCancellationAllowed(true);
            var operationToken = _operation!.Token;
            installation = Installation;
            var installed = installation.InstalledVersion
                ?? throw new InvalidOperationException("No existing Depot installation was found.");
            var releaseClient = new GitHubReleaseClient(_http);
            var release = _latestDepotRelease ?? await releaseClient.GetLatestAsync(operationToken);
            if (!VersionRules.IsUpdate(installed, release.Version))
            {
                Log("The installed Depot version is already current.");
                return;
            }

            var metadata = await new DepotReleaseMetadataClient(_http).GetAsync(release, operationToken);
            if (metadata.ManagerCommandProtocol < 1)
                throw new InvalidOperationException("The selected Depot release does not declare the required manager health-check protocol.");

            var settings = LoadInstalledSettings(installation);
            await new ManagerDatabaseConnectionValidator().ValidateAsync(settings, operationToken);
            var currentSchema = await new DatabaseSchemaInspector().ReadSchemaVersionAsync(settings, operationToken);
            if (metadata.DatabaseSchemaVersion < currentSchema)
                throw new InvalidOperationException($"Update blocked because release schema {metadata.DatabaseSchemaVersion} is older than database schema {currentSchema}.");

            if (metadata.DatabaseSchemaVersion > currentSchema)
            {
                if (settings.Provider == DatabaseProvider.Local)
                {
                    safetyBackup = await new MigrationSafetyService().CreateSqliteSafetyBackupAsync(
                        settings, installation.InstallDirectory, installed, currentSchema, operationToken);
                    Log($"SQLite migration safety backup created: {safetyBackup}");
                }
                else
                {
                    var remoteBackupConfirmed = MessageBox.Show(
                        $"Depot {VersionRules.VersionText(release.Version)} requires database schema {metadata.DatabaseSchemaVersion}; the current schema is {currentSchema}.\n\n" +
                        "For remote SQL Server or MySQL/MariaDB databases, Depot Manager does not assume server backup privileges. Confirm that a current server-side backup exists before continuing.",
                        "Database backup required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (remoteBackupConfirmed != MessageBoxResult.Yes)
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
                await releaseClient.DownloadAsync(release, temp, new Progress<int>(value => Progress.Value = value), operationToken);
                operationToken.ThrowIfCancellationRequested();

                // From executable replacement through migration/health validation the operation is deliberately non-cancellable.
                // Interrupting this critical section could leave the application and database at different compatibility levels.
                SetOperationCancellationAllowed(false);
                installation.Deploy(temp, release.Version, createBackup: true);
                deployedNewExecutable = true;
                RollbackMetadataService.Write(installation.BackupDirectory, installed, currentSchema);

                if (metadata.DatabaseSchemaVersion > currentSchema)
                    await InvokeDepotMaintenanceModeAsync("--manager-migrate", CancellationToken.None);
                await InvokeDepotMaintenanceModeAsync("--manager-health-check", CancellationToken.None);

                installation.CopyManagerToInstallLocation();
                new WindowsIntegrationService().Repair(installation, release.Version);
                Progress.Value = 100;
                Log($"Depot update to {VersionRules.VersionText(release.Version)} completed and health check passed.");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        catch (OperationCanceledException)
        {
            var recovery = await TryRecoverPreviousExecutableAsync(installation, deployedNewExecutable);
            Log($"Depot update cancelled. {recovery}");
        }
        catch (Exception exception)
        {
            var recovery = await TryRecoverPreviousExecutableAsync(installation, deployedNewExecutable);
            var backupNote = safetyBackup is null ? string.Empty : $"\n\nMigration safety backup preserved at:\n{safetyBackup}";
            Log($"Depot update failed: {exception.Message} Recovery: {recovery}");
            MessageBox.Show(
                $"{exception.Message}\n\n{recovery}{backupNote}",
                "Depot Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshMaintenanceStateAsync();
        }
    }

    private async Task<string> TryRecoverPreviousExecutableAsync(InstallationService? installation, bool deployedNewExecutable)
    {
        if (!deployedNewExecutable || installation is null) return "No installed executable was changed.";

        var candidate = RollbackMetadataService.Read(installation.BackupDirectory);
        if (candidate is null) return "Automatic executable recovery is unavailable because no rollback backup exists.";
        if (!candidate.IsValid) return $"Automatic executable recovery is unavailable: {candidate.Message}";

        try
        {
            var settings = LoadInstalledSettings(installation);
            await new ManagerDatabaseConnectionValidator().ValidateAsync(settings, CancellationToken.None);
            var currentSchema = await new DatabaseSchemaInspector().ReadSchemaVersionAsync(settings, CancellationToken.None);
            if (!UpdateRecoveryRules.CanRestorePreviousExecutable(currentSchema, candidate.SupportedSchemaVersion))
            {
                return $"The previous executable was not restored because database schema {currentSchema} no longer matches schema {candidate.SupportedSchemaVersion}. Database downgrade is never automatic.";
            }

            RollbackMetadataService.Apply(installation, candidate, currentSchema);
            new WindowsIntegrationService().Repair(installation, candidate.Version);
            return $"Depot {VersionRules.VersionText(candidate.Version)} was restored automatically because the database schema remained compatible.";
        }
        catch (Exception recoveryException)
        {
            return $"Automatic executable recovery could not be completed: {recoveryException.Message}";
        }
    }

    private void RewireDiagnosticsButtons()
    {
        foreach (var button in FindVisualChildren<Button>(MaintenancePanel))
        {
            if (button.Content is not string content) continue;
            if (string.Equals(content, "Copy diagnostics", StringComparison.Ordinal))
            {
                button.Click -= CopyDiagnostics_Click;
                button.Click += CopyDiagnosticsCompleted_Click;
            }
            else if (string.Equals(content, "Create support package", StringComparison.Ordinal))
            {
                button.Click -= CreateSupportPackage_Click;
                button.Click += CreateSupportPackageCompleted_Click;
            }
        }
    }

    private async void CopyDiagnosticsCompleted_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = _lastSnapshot ?? await new InstallationInspector().InspectAsync(InstallPathBox.Text, CancellationToken.None);
            var service = new ManagerDiagnosticsService();
            Clipboard.SetText(service.ToJson(service.CreateDocument(snapshot, GetRunningManagerVersion(), BuildDiagnosticsContext(snapshot))));
            Log("Diagnostics copied to the clipboard without credentials or connection strings.");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void CreateSupportPackageCompleted_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = _lastSnapshot ?? await new InstallationInspector().InspectAsync(InstallPathBox.Text, CancellationToken.None);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Create Depot support package",
                Filter = "ZIP archive (*.zip)|*.zip",
                AddExtension = true,
                DefaultExt = ".zip",
                FileName = $"DepotSupport-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip"
            };
            if (dialog.ShowDialog(this) != true) return;
            var service = new ManagerDiagnosticsService();
            var path = service.CreateSupportPackage(dialog.FileName, snapshot, GetRunningManagerVersion(), BuildDiagnosticsContext(snapshot));
            Log($"Support package created: {path}");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private ManagerDiagnosticsContext BuildDiagnosticsContext(InstallationSnapshot snapshot)
    {
        var rollback = RollbackMetadataService.Read(Path.Combine(snapshot.InstallDirectory, "Backup"));
        var rollbackStatus = rollback is null
            ? "Unavailable"
            : rollback.IsValid
                ? $"Depot {VersionRules.VersionText(rollback.Version)} / schema {rollback.SupportedSchemaVersion}"
                : rollback.Message;
        return new ManagerDiagnosticsContext(
            _latestDepotRelease is null ? "Unknown" : VersionRules.VersionText(_latestDepotRelease.Version),
            _latestManagerRelease is null ? "Unknown" : VersionRules.VersionText(_latestManagerRelease.Version),
            _latestDepotMetadata?.DatabaseSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown",
            _latestDepotMetadata?.PublishedAt?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown",
            rollbackStatus);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
