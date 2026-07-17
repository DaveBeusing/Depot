// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed class DatabaseManagementService
{
	private const string BackupExtension = ".depotbackup";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	private static readonly TableDefinition[] Tables =
	[
		new("Manufacturers", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("Categories", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("UnitsOfMeasure", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("Packagings", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("Suppliers", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("Items", ["Id", "PartNumber", "Description", "Manufacturer", "Category", "ManufacturerId", "CategoryId", "UnitOfMeasureId", "PackagingId", "SupplierId", "IsActive", "Version"]),
		new("Purposes", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("ReasonCodes", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("Warehouses", ["Id", "Name", "Description", "IsActive", "Version"]),
		new("StorageLocations", ["Id", "WarehouseId", "Name", "Description", "IsActive", "Version"]),
		new("Users", ["Id", "Email", "DisplayName", "PasswordHash", "IsAdministrator", "IsActive", "CreatedUtc", "Version"]),
		new("Inventories", ["Id", "ItemId", "PurposeId", "StorageLocationId", "IsActive", "Version"]),
		new("StockMovements", ["Id", "InventoryId", "ReasonCodeId", "MovementType", "TimestampUtc", "Quantity", "UnitPrice", "Reference", "Notes"]),
		new("AuditEntries", ["Id", "TimestampUtc", "UserId", "UserEmail", "EntityType", "EntityId", "Action", "BeforeJson", "AfterJson"])
	];

	private readonly IDatabaseConnectionFactory _connectionFactory;
	private readonly SettingsService _settingsService;

	public DatabaseManagementService(
		IDatabaseConnectionFactory connectionFactory,
		SettingsService settingsService)
	{
		_connectionFactory = connectionFactory;
		_settingsService = settingsService;
	}

	public async Task<DatabaseOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		var version = Convert.ToInt32(
			await ExecuteScalarAsync(connection, "SELECT Version FROM DatabaseInfo;", cancellationToken),
			CultureInfo.InvariantCulture);
		var size = await GetDatabaseSizeAsync(connection, cancellationToken);
		var settings = _settingsService.CurrentSettings;
		return new DatabaseOverview(
			settings.Provider,
			GetProviderDisplayName(settings.Provider),
			GetConnectionDisplayName(settings),
			version,
			size,
			settings.LastSuccessfulBackupUtc);
	}

	public async Task<DatabaseBackupInfo> CreateBackupAsync(
		string filePath,
		IProgress<DatabaseOperationProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		var normalizedPath = NormalizeBackupPath(filePath);
		var directory = Path.GetDirectoryName(normalizedPath)
			?? throw new ArgumentException("The backup path must contain a directory.", nameof(filePath));
		Directory.CreateDirectory(directory);
		var temporaryPath = normalizedPath + ".tmp";
		if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

		try
		{
			progress?.Report(new DatabaseOperationProgress(5, "Opening a consistent database snapshot..."));
			await using var connection = _connectionFactory.CreateConnection();
			await connection.OpenAsync(cancellationToken);
			await using var transaction = await _connectionFactory.BeginWriteTransactionAsync(connection, cancellationToken);
			var schemaVersion = Convert.ToInt32(
				await ExecuteScalarAsync(connection, transaction, "SELECT Version FROM DatabaseInfo;", cancellationToken),
				CultureInfo.InvariantCulture);
			var manifest = new BackupManifest
			{
				CreatedUtc = DateTime.UtcNow,
				Provider = _connectionFactory.Provider,
				SchemaVersion = schemaVersion
			};

			await using (var stream = new FileStream(
				temporaryPath,
				FileMode.CreateNew,
				FileAccess.ReadWrite,
				FileShare.None,
				bufferSize: 81920,
				useAsync: true))
			using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
			{
				for (var index = 0; index < Tables.Length; index++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var table = Tables[index];
					progress?.Report(new DatabaseOperationProgress(
						10 + (index * 70 / Tables.Length),
						$"Backing up {table.Name}..."));
					manifest.RowCounts[table.Name] = await WriteTableAsync(
						archive,
						connection,
						transaction,
						table,
						cancellationToken);
				}

				var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
				await using var manifestStream = manifestEntry.Open();
				await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
			}

			await transaction.CommitAsync(cancellationToken);
			progress?.Report(new DatabaseOperationProgress(85, "Validating the completed backup..."));
			var validation = await ValidateBackupCoreAsync(temporaryPath, requireCurrentProvider: true, cancellationToken);
			if (!validation.IsValid) throw new InvalidDataException(validation.ValidationMessage);
			File.Move(temporaryPath, normalizedPath, overwrite: true);
			var completedUtc = DateTime.UtcNow;
			_settingsService.RecordSuccessfulBackup(completedUtc);
			progress?.Report(new DatabaseOperationProgress(100, "Backup completed."));
			return validation with
			{
				FilePath = normalizedPath,
				CreatedUtc = manifest.CreatedUtc,
				SizeBytes = new FileInfo(normalizedPath).Length
			};
		}
		catch (OperationCanceledException)
		{
			DeleteTemporaryFile(temporaryPath);
			throw;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			DeleteTemporaryFile(temporaryPath);
			throw new InvalidOperationException($"The database backup could not be created: {exception.Message}", exception);
		}
	}

	public Task<DatabaseBackupInfo> ValidateBackupAsync(
		string filePath,
		CancellationToken cancellationToken = default) =>
		ValidateBackupCoreAsync(filePath, requireCurrentProvider: true, cancellationToken);

	public async Task<string> RestoreBackupAsync(
		string filePath,
		IProgress<DatabaseOperationProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		progress?.Report(new DatabaseOperationProgress(2, "Validating the selected backup..."));
		var backup = await ValidateBackupCoreAsync(filePath, requireCurrentProvider: true, cancellationToken);
		if (!backup.IsValid) throw new InvalidDataException(backup.ValidationMessage);

		var safetyDirectory = GetAbsoluteBackupDirectory();
		var safetyPath = Path.Combine(
			safetyDirectory,
			$"Depot-Safety-{DateTime.Now:yyyyMMdd-HHmmss}{BackupExtension}");
		progress?.Report(new DatabaseOperationProgress(8, "Creating a safety backup of the current database..."));
		await CreateBackupAsync(safetyPath, null, cancellationToken);

		progress?.Report(new DatabaseOperationProgress(30, "Restoring database records..."));
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using var transaction = await _connectionFactory.BeginWriteTransactionAsync(connection, cancellationToken);
		try
		{
			for (var index = Tables.Length - 1; index >= 0; index--)
			{
				await ExecuteNonQueryAsync(
					connection,
					transaction,
					$"DELETE FROM {Tables[index].Name};",
					cancellationToken);
			}

			using var archive = ZipFile.OpenRead(filePath);
			for (var index = 0; index < Tables.Length; index++)
			{
				var table = Tables[index];
				progress?.Report(new DatabaseOperationProgress(
					35 + (index * 55 / Tables.Length),
					$"Restoring {table.Name}..."));
				await RestoreTableAsync(archive, connection, transaction, table, cancellationToken);
			}

			await transaction.CommitAsync(cancellationToken);
		}
		catch
		{
			await transaction.RollbackAsync(CancellationToken.None);
			throw;
		}

		progress?.Report(new DatabaseOperationProgress(100, "Restore completed."));
		return safetyPath;
	}

	public async Task CompactAsync(
		IProgress<DatabaseOperationProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		if (_connectionFactory.Provider != DatabaseProvider.Local)
		{
			throw new NotSupportedException("VACUUM is available only for the local SQLite database.");
		}

		progress?.Report(new DatabaseOperationProgress(10, "Checking database integrity before compacting..."));
		var integrity = await CheckIntegrityAsync(cancellationToken);
		if (!integrity.IsValid) throw new InvalidOperationException(integrity.Message);
		progress?.Report(new DatabaseOperationProgress(40, "Compacting the SQLite database..."));
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await ExecuteNonQueryAsync(connection, null, "VACUUM;", cancellationToken);
		progress?.Report(new DatabaseOperationProgress(100, "Database compacted."));
	}

	public async Task<DatabaseIntegrityResult> CheckIntegrityAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		if (_connectionFactory.Provider == DatabaseProvider.Local)
		{
			var result = Convert.ToString(
				await ExecuteScalarAsync(connection, "PRAGMA integrity_check;", cancellationToken),
				CultureInfo.InvariantCulture);
			return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
				? new DatabaseIntegrityResult(true, "SQLite integrity check completed successfully.")
				: new DatabaseIntegrityResult(false, $"SQLite reported an integrity problem: {result}");
		}

		if (_connectionFactory.Provider == DatabaseProvider.SqlServer)
		{
			await ExecuteNonQueryAsync(connection, null, "DBCC CHECKDB WITH NO_INFOMSGS;", cancellationToken);
			return new DatabaseIntegrityResult(true, "SQL Server integrity check completed successfully.");
		}

		await using var command = connection.CreateCommand();
		command.CommandTimeout = 0;
		command.CommandText = $"CHECK TABLE {string.Join(", ", Tables.Select(table => table.Name))};";
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			var messageType = reader.GetString(2);
			var message = reader.GetString(3);
			if (string.Equals(messageType, "error", StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(message, "OK", StringComparison.OrdinalIgnoreCase))
			{
				return new DatabaseIntegrityResult(false, $"MySQL/MariaDB integrity problem: {message}");
			}
		}

		return new DatabaseIntegrityResult(true, "MySQL/MariaDB integrity check completed successfully.");
	}

	public async Task<IReadOnlyList<DatabaseBackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default)
	{
		var directory = GetAbsoluteBackupDirectory();
		if (!Directory.Exists(directory)) return [];
		var result = new List<DatabaseBackupInfo>();
		foreach (var filePath in Directory.EnumerateFiles(directory, $"*{BackupExtension}"))
		{
			cancellationToken.ThrowIfCancellationRequested();
			result.Add(await ValidateBackupCoreAsync(filePath, requireCurrentProvider: false, cancellationToken));
		}

		return result.OrderByDescending(item => item.CreatedUtc).ToList();
	}

	public string GetAutomaticBackupPath() =>
		Path.Combine(
			GetAbsoluteBackupDirectory(),
			$"Depot-Auto-{DateTime.Now:yyyyMMdd-HHmmss}{BackupExtension}");

	private async Task<DatabaseBackupInfo> ValidateBackupCoreAsync(
		string filePath,
		bool requireCurrentProvider,
		CancellationToken cancellationToken)
	{
		try
		{
			var fullPath = Path.GetFullPath(filePath);
			if (!File.Exists(fullPath)) return InvalidBackup(fullPath, "The backup file does not exist.");
			using var archive = ZipFile.OpenRead(fullPath);
			var manifestEntry = archive.GetEntry("manifest.json");
			if (manifestEntry is null) return InvalidBackup(fullPath, "The backup manifest is missing.");
			await using var manifestStream = manifestEntry.Open();
			var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken);
			if (manifest is null || manifest.FormatVersion != 1)
				return InvalidBackup(fullPath, "The backup format is unsupported or damaged.");
			if (manifest.SchemaVersion != DatabaseVersion.CurrentVersion)
				return InvalidBackup(fullPath, $"Schema version {manifest.SchemaVersion} is not compatible with version {DatabaseVersion.CurrentVersion}.", manifest);
			if (requireCurrentProvider && manifest.Provider != _connectionFactory.Provider)
				return InvalidBackup(fullPath, $"The backup belongs to {GetProviderDisplayName(manifest.Provider)}, not the active provider.", manifest);

			foreach (var table in Tables)
			{
				var entry = archive.GetEntry($"tables/{table.Name}.json");
				if (entry is null || !manifest.RowCounts.TryGetValue(table.Name, out var expectedCount))
					return InvalidBackup(fullPath, $"Backup data for {table.Name} is missing.", manifest);
				await using var entryStream = entry.Open();
				using var document = await JsonDocument.ParseAsync(entryStream, cancellationToken: cancellationToken);
				if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != expectedCount)
					return InvalidBackup(fullPath, $"Backup data for {table.Name} is incomplete.", manifest);
				foreach (var row in document.RootElement.EnumerateArray())
				{
					if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != table.Columns.Length)
						return InvalidBackup(fullPath, $"A record in {table.Name} is damaged.", manifest);
				}
			}

			var info = new FileInfo(fullPath);
			return new DatabaseBackupInfo(fullPath, manifest.CreatedUtc, info.Length, manifest.Provider, manifest.SchemaVersion, true, "Backup is valid.");
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
		{
			return InvalidBackup(filePath, $"The backup is unreadable or damaged: {exception.Message}");
		}
	}

	private static async Task<long> WriteTableAsync(
		ZipArchive archive,
		DbConnection connection,
		DbTransaction transaction,
		TableDefinition table,
		CancellationToken cancellationToken)
	{
		var entry = archive.CreateEntry($"tables/{table.Name}.json", CompressionLevel.Optimal);
		await using var entryStream = entry.Open();
		await using var writer = new Utf8JsonWriter(entryStream);
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandTimeout = 0;
		command.CommandText = $"SELECT {string.Join(", ", table.Columns)} FROM {table.Name} ORDER BY Id;";
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		long count = 0;
		writer.WriteStartArray();
		while (await reader.ReadAsync(cancellationToken))
		{
			writer.WriteStartArray();
			for (var index = 0; index < reader.FieldCount; index++)
			{
				WriteValue(writer, reader.IsDBNull(index) ? null : reader.GetValue(index));
			}
			writer.WriteEndArray();
			count++;
			if (count % 500 == 0) await writer.FlushAsync(cancellationToken);
		}
		writer.WriteEndArray();
		await writer.FlushAsync(cancellationToken);
		return count;
	}

	private async Task RestoreTableAsync(
		ZipArchive archive,
		DbConnection connection,
		DbTransaction transaction,
		TableDefinition table,
		CancellationToken cancellationToken)
	{
		var entry = archive.GetEntry($"tables/{table.Name}.json")
			?? throw new InvalidDataException($"Backup data for {table.Name} is missing.");
		await using var entryStream = entry.Open();
		using var document = await JsonDocument.ParseAsync(entryStream, cancellationToken: cancellationToken);
		if (_connectionFactory.Provider == DatabaseProvider.SqlServer)
			await ExecuteNonQueryAsync(connection, transaction, $"SET IDENTITY_INSERT {table.Name} ON;", cancellationToken);

		try
		{
			foreach (var row in document.RootElement.EnumerateArray())
			{
				cancellationToken.ThrowIfCancellationRequested();
				await using var command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandTimeout = 0;
				command.CommandText = $"INSERT INTO {table.Name} ({string.Join(", ", table.Columns)}) VALUES ({string.Join(", ", table.Columns.Select((_, index) => $"$Value{index}"))});";
				var index = 0;
				foreach (var value in row.EnumerateArray())
				{
					var parameter = command.CreateParameter();
					parameter.ParameterName = _connectionFactory.Provider == DatabaseProvider.Local
						? $"$Value{index}"
						: $"@Value{index}";
					parameter.Value = ReadValue(value);
					command.Parameters.Add(parameter);
					index++;
				}
				await command.ExecuteNonQueryAsync(cancellationToken);
			}
		}
		finally
		{
			if (_connectionFactory.Provider == DatabaseProvider.SqlServer)
				await ExecuteNonQueryAsync(connection, transaction, $"SET IDENTITY_INSERT {table.Name} OFF;", CancellationToken.None);
		}
	}

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
		return Convert.ToInt64(await ExecuteScalarAsync(connection, sql, cancellationToken), CultureInfo.InvariantCulture);
	}

	private string GetAbsoluteBackupDirectory() =>
		Path.GetFullPath(_settingsService.CurrentSettings.BackupDirectory);

	private static string NormalizeBackupPath(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A backup path is required.", nameof(filePath));
		var fullPath = Path.GetFullPath(filePath.Trim());
		return string.Equals(Path.GetExtension(fullPath), BackupExtension, StringComparison.OrdinalIgnoreCase)
			? fullPath
			: fullPath + BackupExtension;
	}

	private static string GetProviderDisplayName(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => "SQLite",
		DatabaseProvider.SqlServer => "Microsoft SQL Server",
		DatabaseProvider.MySql => "MySQL / MariaDB",
		_ => provider.ToString()
	};

	private static string GetConnectionDisplayName(DatabaseConnectionSettings settings) => settings.Provider switch
	{
		DatabaseProvider.Local => Path.GetFullPath(settings.LocalDatabasePath),
		DatabaseProvider.SqlServer => $"{settings.SqlServerHost}:{settings.SqlServerPort} / {settings.SqlServerDatabase}",
		DatabaseProvider.MySql => $"{settings.MySqlHost}:{settings.MySqlPort} / {settings.MySqlDatabase}",
		_ => "Unknown database"
	};

	private static void WriteValue(Utf8JsonWriter writer, object? value)
	{
		switch (value)
		{
			case null or DBNull: writer.WriteNullValue(); break;
			case bool boolean: writer.WriteBooleanValue(boolean); break;
			case byte number: writer.WriteNumberValue(number); break;
			case short number: writer.WriteNumberValue(number); break;
			case int number: writer.WriteNumberValue(number); break;
			case long number: writer.WriteNumberValue(number); break;
			case float number: writer.WriteNumberValue(number); break;
			case double number: writer.WriteNumberValue(number); break;
			case decimal number: writer.WriteNumberValue(number); break;
			case DateTime dateTime: writer.WriteStringValue(dateTime.ToUniversalTime()); break;
			case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
			default: writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
		}
	}

	private static object ReadValue(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.Null => DBNull.Value,
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
		JsonValueKind.Number => value.GetDecimal(),
		JsonValueKind.String => value.GetString() ?? string.Empty,
		_ => throw new InvalidDataException("The backup contains an unsupported value.")
	};

	private static async Task<object?> ExecuteScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken) =>
		await ExecuteScalarAsync(connection, null, sql, cancellationToken);

	private static async Task<object?> ExecuteScalarAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandTimeout = 0;
		command.CommandText = sql;
		return await command.ExecuteScalarAsync(cancellationToken);
	}

	private static async Task ExecuteNonQueryAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandTimeout = 0;
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static DatabaseBackupInfo InvalidBackup(string filePath, string message, BackupManifest? manifest = null)
	{
		var info = File.Exists(filePath) ? new FileInfo(filePath) : null;
		return new DatabaseBackupInfo(
			filePath,
			manifest?.CreatedUtc ?? info?.LastWriteTimeUtc ?? DateTime.MinValue,
			info?.Length ?? 0,
			manifest?.Provider ?? DatabaseProvider.Local,
			manifest?.SchemaVersion ?? 0,
			false,
			message);
	}

	private static void DeleteTemporaryFile(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); }
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	private sealed record TableDefinition(string Name, string[] Columns);

	private sealed class BackupManifest
	{
		public int FormatVersion { get; set; } = 1;
		public DateTime CreatedUtc { get; set; }
		public DatabaseProvider Provider { get; set; }
		public int SchemaVersion { get; set; }
		public Dictionary<string, long> RowCounts { get; set; } = new(StringComparer.Ordinal);
	}
}
