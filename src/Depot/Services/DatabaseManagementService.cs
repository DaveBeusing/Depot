// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed partial class DatabaseManagementService
{
	private const string BackupExtension = ".depotbackup";
	private readonly IDatabaseConnectionFactory _connectionFactory;
	private readonly SettingsService _settingsService;

	public DatabaseManagementService(IDatabaseConnectionFactory connectionFactory, SettingsService settingsService, IAuthorizationService authorization)
	{
		_connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
		_settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
		_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
	}

	public async Task<DatabaseOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
	{
		RequireView();
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		var version = Convert.ToInt32(await DatabaseBackupSchema.ScalarAsync(connection, null, "SELECT Version FROM DatabaseInfo;", cancellationToken), CultureInfo.InvariantCulture);
		var size = await GetDatabaseSizeAsync(connection, cancellationToken);
		var settings = _settingsService.CurrentSettings;
		return new DatabaseOverview(settings.Provider, GetProviderDisplayName(settings.Provider), GetConnectionDisplayName(settings), version, size, settings.LastSuccessfulBackupUtc);
	}

	public Task<DatabaseBackupInfo> CreateBackupAsync(string filePath, IProgress<DatabaseOperationProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		RequireManage();
		return CreateBackupCoreAsync(filePath, progress, cancellationToken);
	}

	internal Task<DatabaseBackupInfo> CreateScheduledBackupAsync(CancellationToken cancellationToken = default)
	{
		if (!_settingsService.CurrentSettings.AutomaticBackupsEnabled)
			throw new InvalidOperationException("Automatic backups are disabled.");
		return CreateBackupCoreAsync(GetAutomaticBackupPath(), null, cancellationToken);
	}

	private async Task<DatabaseBackupInfo> CreateBackupCoreAsync(string filePath, IProgress<DatabaseOperationProgress>? progress, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var normalizedPath = NormalizeBackupPath(filePath);
		progress?.Report(new DatabaseOperationProgress(5, "Opening a consistent database snapshot..."));
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using var transaction = await _connectionFactory.BeginWriteTransactionAsync(connection, cancellationToken);
		var schema = await DatabaseBackupSchema.ReadAsync(connection, transaction, _connectionFactory.Provider, cancellationToken);
		await schema.ValidateForeignKeysAsync(connection, transaction, cancellationToken);
		var backup = await WriteSnapshotAsync(normalizedPath, connection, transaction, schema, progress, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		_settingsService.RecordSuccessfulBackup(DateTime.UtcNow);
		progress?.Report(new DatabaseOperationProgress(100, "Backup completed."));
		return backup;
	}

	private static async Task<DatabaseBackupInfo> WriteSnapshotAsync(
		string path, DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema,
		IProgress<DatabaseOperationProgress>? progress, CancellationToken cancellationToken)
	{
		var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("The backup path must contain a directory.", nameof(path));
		Directory.CreateDirectory(directory);
		var temporaryPath = path + $".tmp-{Guid.NewGuid():N}";
		try
		{
			await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
			{
				await DatabaseBackupArchive.WriteAsync(target, connection, transaction, schema, progress, cancellationToken);
				await target.FlushAsync(cancellationToken);
				target.Flush(flushToDisk: true);
			}
			progress?.Report(new DatabaseOperationProgress(80, "Validating the complete backup..."));
			DatabaseBackupInfo result;
			using (var archive = await DatabaseBackupArchive.OpenValidatedAsync(temporaryPath, schema, cancellationToken))
				result = BackupInfo(path, archive);
			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporaryPath, path, overwrite: true);
			return result;
		}
		finally
		{
			DeleteTemporaryFile(temporaryPath);
		}
	}

	public Task<DatabaseBackupInfo> ValidateBackupAsync(string filePath, CancellationToken cancellationToken = default)
	{
		RequireView();
		return ValidateBackupCoreAsync(filePath, cancellationToken);
	}

	private async Task<DatabaseBackupInfo> ValidateBackupCoreAsync(string filePath, CancellationToken cancellationToken)
	{
		try
		{
			var fullPath = Path.GetFullPath(filePath);
			if (!File.Exists(fullPath)) return InvalidBackup(fullPath, "The backup file does not exist.");
			await using var connection = _connectionFactory.CreateConnection();
			await connection.OpenAsync(cancellationToken);
			var schema = await DatabaseBackupSchema.ReadAsync(connection, null, _connectionFactory.Provider, cancellationToken);
			using var archive = await DatabaseBackupArchive.OpenValidatedAsync(fullPath, schema, cancellationToken);
			return BackupInfo(fullPath, archive);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or OverflowException)
		{
			return InvalidBackup(filePath, $"The backup is unreadable, incomplete, incompatible, or damaged: {exception.Message}");
		}
	}

	public async Task<string> RestoreBackupAsync(string filePath, IProgress<DatabaseOperationProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		RequireManage();
		cancellationToken.ThrowIfCancellationRequested();
		progress?.Report(new DatabaseOperationProgress(2, "Validating the complete selected backup..."));
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		var initialSchema = await DatabaseBackupSchema.ReadAsync(connection, null, _connectionFactory.Provider, cancellationToken);
		using var archive = await OpenForRestoreAsync(filePath, initialSchema, cancellationToken);
		var safetyPath = Path.Combine(GetAbsoluteBackupDirectory(), $"Depot-Safety-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}{BackupExtension}");

		// Foreign-key switches are connection-local. SQLite requires changing its switch
		// before the transaction starts. Every relation is checked before the commit.
		var previousForeignKeySetting = await GetForeignKeySettingAsync(connection, cancellationToken);
		try
		{
			await SetForeignKeySettingAsync(connection, enabled: false, cancellationToken);
			await using var transaction = await _connectionFactory.BeginWriteTransactionAsync(connection, cancellationToken);
			try
			{
				var schema = await DatabaseBackupSchema.ReadAsync(connection, transaction, _connectionFactory.Provider, cancellationToken);
				archive.RequireCompatible(schema);
				progress?.Report(new DatabaseOperationProgress(8, "Creating a safety backup of the locked current database..."));
				await WriteSnapshotAsync(safetyPath, connection, transaction, schema, null, cancellationToken);
				await SuspendTransactionalObjectsAsync(connection, transaction, schema, cancellationToken);
				foreach (var table in schema.Tables)
					await DatabaseBackupArchive.ExecuteAsync(connection, transaction, $"DELETE FROM {schema.TableName(table.Name)};", cancellationToken);
				for (var index = 0; index < schema.Tables.Length; index++)
				{
					var table = schema.Tables[index];
					progress?.Report(new DatabaseOperationProgress(30 + index * 50 / schema.Tables.Length, $"Restoring {table.Name}..."));
					await archive.RestoreTableAsync(connection, transaction, schema, table, cancellationToken);
				}
				await RestoreIdentityHighWaterMarksAsync(connection, transaction, schema, archive.Manifest, cancellationToken);
				await ResumeTransactionalObjectsAsync(connection, transaction, schema, cancellationToken);
				progress?.Report(new DatabaseOperationProgress(85, "Verifying restored values and foreign keys..."));
				await schema.ValidateForeignKeysAsync(connection, transaction, cancellationToken);
				await archive.VerifyRestoredRowsAsync(connection, transaction, schema, cancellationToken);
				await transaction.CommitAsync(cancellationToken);
			}
			catch
			{
				await transaction.RollbackAsync(CancellationToken.None);
				throw;
			}
		}
		finally
		{
			await SetForeignKeySettingAsync(connection, previousForeignKeySetting, CancellationToken.None);
		}
		progress?.Report(new DatabaseOperationProgress(100, "Restore completed and verified."));
		return safetyPath;
	}

	private static async Task<DatabaseBackupArchive> OpenForRestoreAsync(string filePath, DatabaseBackupSchema schema, CancellationToken cancellationToken)
	{
		try { return await DatabaseBackupArchive.OpenValidatedAsync(Path.GetFullPath(filePath), schema, cancellationToken); }
		catch (Exception exception) when (exception is IOException or JsonException or FormatException or OverflowException)
		{
			throw new InvalidDataException($"The selected backup is unreadable or damaged: {exception.Message}", exception);
		}
	}

	private async Task<bool> GetForeignKeySettingAsync(DbConnection connection, CancellationToken cancellationToken)
	{
		var sql = _connectionFactory.Provider switch
		{
			DatabaseProvider.Local => "PRAGMA foreign_keys;",
			DatabaseProvider.MySql => "SELECT @@SESSION.FOREIGN_KEY_CHECKS;",
			_ => null
		};
		return sql is null || Convert.ToInt32(await DatabaseBackupSchema.ScalarAsync(connection, null, sql, cancellationToken), CultureInfo.InvariantCulture) != 0;
	}

	private Task SetForeignKeySettingAsync(DbConnection connection, bool enabled, CancellationToken cancellationToken)
	{
		var sql = _connectionFactory.Provider switch
		{
			DatabaseProvider.Local => $"PRAGMA foreign_keys={(enabled ? 1 : 0)};",
			DatabaseProvider.MySql => $"SET SESSION FOREIGN_KEY_CHECKS={(enabled ? 1 : 0)};",
			_ => null
		};
		return sql is null ? Task.CompletedTask : DatabaseBackupArchive.ExecuteAsync(connection, null, sql, cancellationToken);
	}

	private static bool IsSqliteAfterTrigger(DatabaseBackupTrigger trigger) =>
		Regex.IsMatch(trigger.Definition, @"\bAFTER\s+(INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static async Task SuspendTransactionalObjectsAsync(DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema, CancellationToken cancellationToken)
	{
		if (schema.Provider == DatabaseProvider.Local)
		{
			// BEFORE triggers enforce validation rules and remain enabled. AFTER triggers
			// must not rewrite the archived CRM links during bulk insertion.
			foreach (var trigger in schema.Triggers.Where(IsSqliteAfterTrigger))
				await DatabaseBackupArchive.ExecuteAsync(connection, transaction, $"DROP TRIGGER {schema.Quote(trigger.Name)};", cancellationToken);
		}
		if (schema.Provider == DatabaseProvider.SqlServer)
		{
			foreach (var table in schema.Tables)
				await DatabaseBackupArchive.ExecuteAsync(connection, transaction, $"ALTER TABLE {schema.TableName(table.Name)} NOCHECK CONSTRAINT ALL;", cancellationToken);
			foreach (var trigger in schema.Triggers)
				await DatabaseBackupArchive.ExecuteAsync(connection, transaction, $"DISABLE TRIGGER {schema.Quote(trigger.Name)} ON {schema.TableName(trigger.Table)};", cancellationToken);
		}
	}

	private static async Task ResumeTransactionalObjectsAsync(DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema, CancellationToken cancellationToken)
	{
		if (schema.Provider == DatabaseProvider.Local)
			foreach (var trigger in schema.Triggers.Where(IsSqliteAfterTrigger))
				await DatabaseBackupArchive.ExecuteAsync(connection, transaction, trigger.Definition, cancellationToken);
		if (schema.Provider == DatabaseProvider.SqlServer)
		{
			foreach (var table in schema.Tables)
				await DatabaseBackupArchive.ExecuteAsync(connection, transaction, $"ALTER TABLE {schema.TableName(table.Name)} WITH CHECK CHECK CONSTRAINT ALL;", cancellationToken);
			foreach (var trigger in schema.Triggers)
				await DatabaseBackupArchive.ExecuteAsync(connection, transaction, $"ENABLE TRIGGER {schema.Quote(trigger.Name)} ON {schema.TableName(trigger.Table)};", cancellationToken);
		}
	}

	private static async Task RestoreIdentityHighWaterMarksAsync(DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema, DatabaseBackupManifest manifest, CancellationToken cancellationToken)
	{
		if (schema.Provider == DatabaseProvider.SqlServer)
		{
			foreach (var table in schema.Tables.Where(table => table.HasIdentity))
			{
				var sourceHighWater = manifest.IdentityHighWaterMarks[table.Name];
				var lastValue = await DatabaseBackupSchema.ScalarAsync(connection, transaction,
					$"SELECT last_value FROM sys.identity_columns WHERE object_id=OBJECT_ID(N'dbo.{table.Name}');", cancellationToken);
				var neverUsed = lastValue is null or DBNull;
				var current = neverUsed ? 0 : Convert.ToInt64(lastValue, CultureInfo.InvariantCulture);
				if (sourceHighWater > current)
				{
					// A never-populated or truncated table uses the reseed value itself
					// for its next INSERT; an existing identity uses reseed + 1.
					var reseed = neverUsed ? checked(sourceHighWater + 1) : sourceHighWater;
					await DatabaseBackupArchive.ExecuteAsync(connection, transaction,
						$"DBCC CHECKIDENT (N'dbo.{table.Name}', RESEED, {reseed.ToString(CultureInfo.InvariantCulture)}) WITH NO_INFOMSGS;", cancellationToken);
				}
			}
			return;
		}
		if (schema.Provider != DatabaseProvider.Local) return;
		foreach (var table in schema.Tables.Where(table => table.HasIdentity))
		{
			if (!manifest.IdentityHighWaterMarks.TryGetValue(table.Name, out var value)) continue;
			await using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "UPDATE sqlite_sequence SET seq=MAX(seq,@Sequence) WHERE name=@Name; INSERT INTO sqlite_sequence(name,seq) SELECT @Name,@Sequence WHERE NOT EXISTS(SELECT 1 FROM sqlite_sequence WHERE name=@Name);";
			var name = command.CreateParameter(); name.ParameterName = "@Name"; name.Value = table.Name; command.Parameters.Add(name);
			var sequence = command.CreateParameter(); sequence.ParameterName = "@Sequence"; sequence.Value = value; command.Parameters.Add(sequence);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}
	}

	public async Task CompactAsync(IProgress<DatabaseOperationProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		RequireManage();
		if (_connectionFactory.Provider != DatabaseProvider.Local) throw new NotSupportedException("Compaction is available for local SQLite databases only.");
		var integrity = await CheckIntegrityAsync(cancellationToken);
		if (!integrity.IsValid) throw new InvalidOperationException(integrity.Message);
		progress?.Report(new DatabaseOperationProgress(20, "Compacting the local database..."));
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await DatabaseBackupArchive.ExecuteAsync(connection, null, "VACUUM;", cancellationToken);
		progress?.Report(new DatabaseOperationProgress(100, "Database compaction completed."));
	}

	public async Task<DatabaseIntegrityResult> CheckIntegrityAsync(CancellationToken cancellationToken = default)
	{
		RequireView();
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		if (_connectionFactory.Provider == DatabaseProvider.Local)
		{
			var rows = await DatabaseBackupSchema.QueryAsync(connection, null, "PRAGMA integrity_check;", cancellationToken);
			if (rows.Count != 1 || !string.Equals(Convert.ToString(rows[0][0], CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
				return new(false, "SQLite reported a database integrity problem.");
			if ((await DatabaseBackupSchema.QueryAsync(connection, null, "PRAGMA foreign_key_check;", cancellationToken)).Count > 0)
				return new(false, "SQLite reported a foreign key integrity problem.");
			return new(true, "SQLite integrity check completed successfully.");
		}
		if (_connectionFactory.Provider == DatabaseProvider.SqlServer)
		{
			await DatabaseBackupArchive.ExecuteAsync(connection, null, "DBCC CHECKDB WITH NO_INFOMSGS;", cancellationToken);
			return new(true, "SQL Server integrity check completed successfully.");
		}
		var schema = await DatabaseBackupSchema.ReadAsync(connection, null, _connectionFactory.Provider, cancellationToken);
		var result = await DatabaseBackupSchema.QueryAsync(connection, null, $"CHECK TABLE {string.Join(",", schema.Tables.Select(table => schema.TableName(table.Name)))};", cancellationToken);
		return result.Any(row => !string.Equals(Convert.ToString(row[3], CultureInfo.InvariantCulture), "OK", StringComparison.OrdinalIgnoreCase))
			? new(false, "MySQL/MariaDB reported a database integrity problem.")
			: new(true, "MySQL/MariaDB integrity check completed successfully.");
	}

	public async Task<IReadOnlyList<DatabaseBackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default)
	{
		RequireView();
		var directory = GetAbsoluteBackupDirectory();
		if (!Directory.Exists(directory)) return [];
		var result = new List<DatabaseBackupInfo>();
		foreach (var path in Directory.EnumerateFiles(directory, $"*{BackupExtension}"))
		{
			cancellationToken.ThrowIfCancellationRequested();
			result.Add(await ValidateBackupCoreAsync(path, cancellationToken));
		}
		return result.OrderByDescending(item => item.CreatedUtc).ToList();
	}

	public string GetAutomaticBackupPath() => Path.Combine(GetAbsoluteBackupDirectory(), $"Depot-Auto-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}{BackupExtension}");

	private async Task<long> GetDatabaseSizeAsync(DbConnection connection, CancellationToken cancellationToken)
	{
		if (_connectionFactory.Provider == DatabaseProvider.Local)
		{
			var path = Path.GetFullPath(_settingsService.CurrentSettings.LocalDatabasePath);
			return File.Exists(path) ? new FileInfo(path).Length : 0;
		}
		var sql = _connectionFactory.Provider == DatabaseProvider.SqlServer
			? "SELECT COALESCE(SUM(size), 0) * 8192 FROM sys.database_files;"
			: "SELECT COALESCE(SUM(DATA_LENGTH + INDEX_LENGTH), 0) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE();";
		return Convert.ToInt64(await DatabaseBackupSchema.ScalarAsync(connection, null, sql, cancellationToken), CultureInfo.InvariantCulture);
	}

	private string GetAbsoluteBackupDirectory() => Path.GetFullPath(_settingsService.CurrentSettings.BackupDirectory);

	private static string NormalizeBackupPath(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A backup path is required.", nameof(filePath));
		var path = Path.GetFullPath(filePath.Trim());
		return string.Equals(Path.GetExtension(path), BackupExtension, StringComparison.OrdinalIgnoreCase) ? path : path + BackupExtension;
	}

	private static string GetProviderDisplayName(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => "SQLite", DatabaseProvider.SqlServer => "Microsoft SQL Server", DatabaseProvider.MySql => "MySQL / MariaDB", _ => provider.ToString()
	};

	private static string GetConnectionDisplayName(DatabaseConnectionSettings settings) => settings.Provider switch
	{
		DatabaseProvider.Local => Path.GetFullPath(settings.LocalDatabasePath),
		DatabaseProvider.SqlServer => $"{settings.SqlServerHost}:{settings.SqlServerPort} / {settings.SqlServerDatabase}",
		DatabaseProvider.MySql => $"{settings.MySqlHost}:{settings.MySqlPort} / {settings.MySqlDatabase}", _ => "Unknown database"
	};

	private static DatabaseBackupInfo BackupInfo(string path, DatabaseBackupArchive archive) =>
		new(path, archive.Manifest.CreatedUtc, archive.Length, archive.Manifest.Provider, archive.Manifest.SchemaVersion, true, "Complete backup is valid for the current schema.");

	private static DatabaseBackupInfo InvalidBackup(string path, string message)
	{
		var info = File.Exists(path) ? new FileInfo(path) : null;
		return new(path, info?.LastWriteTimeUtc ?? DateTime.MinValue, info?.Length ?? 0, DatabaseProvider.Local, 0, false, message);
	}

	private static void DeleteTemporaryFile(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); }
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}
}

