using Depot.Models;
using System.Windows;

namespace DepotManager;

public partial class MainWindow
{
	private async void ValidateConnection_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (_selectedProvider < 0) throw new InvalidOperationException("Choose a database type first.");
			SetBusy(true);
			PrepareDatabaseTarget();

			var settings = BuildValidationSettings();
			var validator = new ManagerDatabaseConnectionValidator();
			await validator.ValidateAsync(settings, _operation!.Token);

			_validatedDatabaseFingerprint = CreateDatabaseFingerprint();
			ConfigurationNextButton.IsEnabled = true;
			ShowConnectionFeedback(true, $"Validation successful · {DescribeDatabaseTarget()}");
			Log($"Database validation succeeded inside Depot Manager for {DescribeDatabaseTarget()}.");
		}
		catch (Exception ex)
		{
			_validatedDatabaseFingerprint = null;
			ConfigurationNextButton.IsEnabled = false;
			ShowConnectionFeedback(false, $"Validation failed · {ex.Message}");
			Log($"Database validation failed inside Depot Manager: {ex.Message}");
		}
		finally
		{
			SetBusy(false);
		}
	}

	private DatabaseConnectionSettings BuildValidationSettings()
	{
		var depotProvider = ManagerDatabaseProviderSelection.ToDepotProviderIndex(_selectedProvider);
		int.TryParse(PortBox.Text, out var port);
		var requireTls = TlsBox.IsChecked == true;

		return new DatabaseConnectionSettings
		{
			Provider = (DatabaseProvider)depotProvider,
			LocalDatabasePath = SqlitePathBox.Text.Trim(),
			SqlServerHost = depotProvider == 1 ? HostBox.Text.Trim() : string.Empty,
			SqlServerPort = depotProvider == 1 ? (port == 0 ? 1433 : port) : 1433,
			SqlServerDatabase = depotProvider == 1 ? DatabaseBox.Text.Trim() : string.Empty,
			SqlServerUserName = depotProvider == 1 ? UserBox.Text.Trim() : string.Empty,
			SqlServerPassword = depotProvider == 1 ? DatabasePasswordBox.PasswordValue : string.Empty,
			EncryptSqlServerConnection = requireTls,
			TrustSqlServerCertificate = false,
			MySqlHost = depotProvider == 2 ? HostBox.Text.Trim() : string.Empty,
			MySqlPort = depotProvider == 2 ? (port == 0 ? 3306 : port) : 3306,
			MySqlDatabase = depotProvider == 2 ? DatabaseBox.Text.Trim() : string.Empty,
			MySqlUserName = depotProvider == 2 ? UserBox.Text.Trim() : string.Empty,
			MySqlPassword = depotProvider == 2 ? DatabasePasswordBox.PasswordValue : string.Empty,
			UseMySqlTls = requireTls,
			AutomaticBackupsEnabled = false,
			BackupDirectory = "Backups",
			BackupIntervalDays = 1
		};
	}
}
