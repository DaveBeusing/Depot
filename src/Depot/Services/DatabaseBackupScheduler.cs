// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Diagnostics;

namespace Depot.Services;

public sealed class DatabaseBackupScheduler : IDisposable
{
	private readonly DatabaseManagementService _databaseManagementService;
	private readonly SettingsService _settingsService;
	private readonly SemaphoreSlim _executionLock = new(1, 1);
	private readonly CancellationTokenSource _cancellation = new();
	private Timer? _timer;

	public DatabaseBackupScheduler(
		DatabaseManagementService databaseManagementService,
		SettingsService settingsService)
	{
		_databaseManagementService = databaseManagementService;
		_settingsService = settingsService;
	}

	public void Start()
	{
		_timer ??= new Timer(
			_ => _ = RunDueBackupAsync(_cancellation.Token),
			state: null,
			dueTime: TimeSpan.FromSeconds(10),
			period: TimeSpan.FromHours(1));
	}

	public async Task<bool> RunDueBackupAsync(CancellationToken cancellationToken = default)
	{
		var settings = _settingsService.CurrentSettings;
		if (!settings.AutomaticBackupsEnabled) return false;
		var nextBackupUtc = settings.LastSuccessfulBackupUtc?.AddDays(settings.BackupIntervalDays);
		if (nextBackupUtc is not null && nextBackupUtc > DateTime.UtcNow) return false;
		if (!await _executionLock.WaitAsync(0, cancellationToken)) return false;

		try
		{
			await _databaseManagementService.CreateBackupAsync(
				_databaseManagementService.GetAutomaticBackupPath(),
				progress: null,
				cancellationToken);
			StartupDiagnostics.Log("Scheduled database backup completed.");
			return true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.LogException(exception);
			return false;
		}
		finally
		{
			_executionLock.Release();
		}
	}

	public void Dispose()
	{
		_cancellation.Cancel();
		_timer?.Dispose();
	}
}
