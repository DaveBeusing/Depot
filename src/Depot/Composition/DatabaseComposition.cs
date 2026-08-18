// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

namespace Depot.Composition;

internal sealed class DatabaseComposition : IDisposable
{
	private DatabaseComposition(
		DatabaseAccess dataAccess,
		IDatabaseTransactionRunner transactionRunner,
		SettingsService settings,
		ConnectionStatusService connectionStatus,
		DatabaseConnectionTester connectionTester,
		DatabaseManagementService management,
		DatabaseBackupScheduler backupScheduler)
	{
		DataAccess = dataAccess;
		TransactionRunner = transactionRunner;
		Settings = settings;
		ConnectionStatus = connectionStatus;
		ConnectionTester = connectionTester;
		Management = management;
		BackupScheduler = backupScheduler;
	}

	public DatabaseAccess DataAccess { get; }
	public IDatabaseTransactionRunner TransactionRunner { get; }
	public SettingsService Settings { get; }
	public ConnectionStatusService ConnectionStatus { get; }
	public DatabaseConnectionTester ConnectionTester { get; }
	public DatabaseManagementService Management { get; }
	private DatabaseBackupScheduler BackupScheduler { get; }

	public static DatabaseComposition Create()
	{
		var settingsRepository = new SettingsRepository("depot.settings");
		var settings = new SettingsService(settingsRepository);
		var connectionStatus = new ConnectionStatusService();
		var connectionSettings = settings.LoadOrCreate();
		var connectionFactory = DatabaseProviderFactory.CreateConnectionFactory(connectionSettings);
		var dataAccess = new DatabaseAccess(connectionFactory);
		var database = DatabaseProviderFactory.CreateInitializer(connectionFactory);
		database.Initialize();
		SalesSchemaInitializer.Ensure(connectionFactory);
		connectionStatus.SetConnected(connectionSettings);
		var management = new DatabaseManagementService(connectionFactory, settings);
		return new DatabaseComposition(
			dataAccess,
			new DatabaseTransactionRunner(dataAccess),
			settings,
			connectionStatus,
			new DatabaseConnectionTester(),
			management,
			new DatabaseBackupScheduler(management, settings));
	}

	public void StartBackgroundServices() => BackupScheduler.Start();
	public void ConfigureNotifications(NotificationService notifications) => BackupScheduler.ConfigureNotifications(notifications);

	public void Dispose() => BackupScheduler.Dispose();
}
