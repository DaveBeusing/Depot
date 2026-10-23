// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

internal sealed class DatabaseBackupManifest
{
	public int FormatVersion { get; set; }
	public int ContractVersion { get; set; }
	public DateTime CreatedUtc { get; set; }
	public DatabaseProvider Provider { get; set; }
	public int SchemaVersion { get; set; }
	public string SchemaFingerprint { get; set; } = string.Empty;
	public Dictionary<string, int> FeatureVersions { get; set; } = new(StringComparer.Ordinal);
	public Dictionary<string, long> IdentityHighWaterMarks { get; set; } = new(StringComparer.Ordinal);
	public List<DatabaseBackupTableData> Tables { get; set; } = [];
}

internal sealed record DatabaseBackupTableData(DatabaseBackupTable Schema, long RowCount, string Sha256, long? MaximumIdentityValue);

/// <summary>A validated archive remains open until restore finishes; the caller's path is never reopened.</summary>
internal sealed class DatabaseBackupArchive : IDisposable
{
	public const int FormatVersion = 2;
	internal static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};
	private readonly FileStream _stream;
	private readonly ZipArchive _archive;
	public DatabaseBackupManifest Manifest { get; }
	public long Length => _stream.Length;

	private DatabaseBackupArchive(FileStream stream, ZipArchive archive, DatabaseBackupManifest manifest)
	{
		_stream = stream;
		_archive = archive;
		Manifest = manifest;
	}

	public static async Task<DatabaseBackupArchive> OpenValidatedAsync(
		string path, DatabaseBackupSchema schema, CancellationToken cancellationToken)
	{
		var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
		ZipArchive? archive = null;
		try
		{
			archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
			var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
			foreach (var entry in archive.Entries)
				if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException($"Duplicate backup entry: {entry.FullName}.");
			if (!entries.TryGetValue("manifest.json", out var manifestEntry)) throw new InvalidDataException("The backup manifest is missing.");
			DatabaseBackupManifest manifest;
			await using (var source = manifestEntry.Open())
			{
				// Inspect the format first so old partial archives have an actionable rejection.
				using var document = await JsonDocument.ParseAsync(source, cancellationToken: cancellationToken);
				if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("FormatVersion", out var format) || format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var version) || version != FormatVersion)
					throw new InvalidDataException("This backup format is unsupported or incomplete. Legacy format 1 archives cannot be used for full database restore; create a new format 2 backup.");
				manifest = document.Deserialize<DatabaseBackupManifest>(JsonOptions) ?? throw new InvalidDataException("The backup manifest is damaged.");
			}
			ValidateContract(manifest, schema);
			if (entries.Count != schema.Tables.Length + 1) throw new InvalidDataException("The archive contains missing or unexpected table entries.");
			for (var index = 0; index < schema.Tables.Length; index++)
			{
				var table = schema.Tables[index];
				var expected = manifest.Tables[index];
				if (!entries.TryGetValue(EntryName(table), out var entry)) throw new InvalidDataException($"Backup data for {table.Name} is missing.");
				await using (var source = entry.Open())
				{
					var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
					if (!string.Equals(hash, expected.Sha256, StringComparison.Ordinal)) throw new InvalidDataException($"The checksum for {table.Name} does not match.");
				}
				await ValidateRowsAsync(entry, table, expected, schema, cancellationToken);
			}
			var result = new DatabaseBackupArchive(stream, archive, manifest);
			result.RequireCompatible(schema);
			return result;
		}
		catch (Exception exception)
		{
			archive?.Dispose();
			await stream.DisposeAsync();
			if (exception is InvalidCastException or InvalidOperationException or FormatException or OverflowException)
				throw new InvalidDataException("The backup contains an invalid manifest or typed record.", exception);
			throw;
		}
	}

	public void RequireCompatible(DatabaseBackupSchema schema)
	{
		ValidateContract(Manifest, schema);
		if (schema.Provider != DatabaseProvider.MySql) return;
		foreach (var table in Manifest.Tables.Where(table => table.Schema.HasIdentity))
		{
			var sourceHighWater = Manifest.IdentityHighWaterMarks[table.Schema.Name];
			var targetHighWater = schema.IdentityHighWaterMarks[table.Schema.Name];
			if (Math.Max(targetHighWater, table.MaximumIdentityValue ?? 0) < sourceHighWater)
				throw new InvalidDataException($"The target identity counter for {table.Schema.Name} is below the backup high-water mark ({sourceHighWater}). Prepare the target counter with native administration or use a native restore. Logical restore cannot issue MySQL ALTER TABLE without committing the data transaction.");
		}
	}

	private static void ValidateContract(DatabaseBackupManifest manifest, DatabaseBackupSchema schema)
	{
		if (manifest.ContractVersion != DatabaseBackupSchema.ContractVersion || manifest.Provider != schema.Provider ||
			manifest.SchemaVersion != schema.CoreVersion || manifest.SchemaFingerprint != schema.Fingerprint ||
			manifest.FeatureVersions is null || !DatabaseBackupSchema.VersionsEqual(manifest.FeatureVersions, schema.FeatureVersions))
			throw new InvalidDataException("The backup provider, complete schema contract, or feature versions do not match the current database.");
		if (manifest.CreatedUtc == default || manifest.CreatedUtc.Kind != DateTimeKind.Utc || manifest.Tables is null || manifest.Tables.Count != schema.Tables.Length)
			throw new InvalidDataException("The backup manifest is incomplete.");
		for (var index = 0; index < schema.Tables.Length; index++)
		{
			var expected = manifest.Tables[index];
			var actual = schema.Tables[index];
			if (expected is null || expected.Schema is null || expected.Schema.Name != actual.Name || expected.Schema.Columns is null ||
				!expected.Schema.Columns.SequenceEqual(actual.Columns) || expected.RowCount < 0 || expected.Sha256 is null || expected.Sha256.Length != 64)
				throw new InvalidDataException($"The complete table/column contract for {actual.Name} is missing or incompatible.");
		}
		if (manifest.IdentityHighWaterMarks is null || manifest.IdentityHighWaterMarks.Count != schema.Tables.Count(table => table.HasIdentity) ||
			manifest.IdentityHighWaterMarks.Any(pair => pair.Value < 0 || !schema.Tables.Any(table => table.Name == pair.Key && table.HasIdentity)))
			throw new InvalidDataException("The backup contains invalid identity sequence metadata.");
	}

	private static async Task ValidateRowsAsync(ZipArchiveEntry entry, DatabaseBackupTable table, DatabaseBackupTableData expected, DatabaseBackupSchema schema, CancellationToken cancellationToken)
	{
		await using var source = entry.Open();
		var columns = table.StoredColumns;
		var featureVersions = new Dictionary<string, int>(StringComparer.Ordinal);
		var identityIndex = Array.FindIndex(columns, column => column.IsIdentity);
		long? maximumIdentity = null;
		long count = 0;
		await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(source, cancellationToken: cancellationToken))
		{
			if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != columns.Length) throw new InvalidDataException($"A record in {table.Name} is incomplete.");
			for (var index = 0; index < columns.Length; index++) _ = ReadValue(row[index], columns[index], schema.Provider);
			if (identityIndex >= 0)
			{
				var value = ReadInteger(row[identityIndex], columns[identityIndex], schema.Provider);
				maximumIdentity = maximumIdentity is null ? value : Math.Max(value, maximumIdentity.Value);
			}
			if (string.Equals(table.Name, "DatabaseInfo", StringComparison.OrdinalIgnoreCase))
			{
				var index = Array.FindIndex(columns, column => string.Equals(column.Name, "Version", StringComparison.OrdinalIgnoreCase));
				if (ReadInteger(row[index], columns[index], schema.Provider) != schema.CoreVersion)
					throw new InvalidDataException("The archived core version does not match its manifest.");
			}
			if (string.Equals(table.Name, "DepotFeatureVersions", StringComparison.OrdinalIgnoreCase))
			{
				var nameIndex = Array.FindIndex(columns, column => string.Equals(column.Name, "Name", StringComparison.OrdinalIgnoreCase));
				var versionIndex = Array.FindIndex(columns, column => string.Equals(column.Name, "Version", StringComparison.OrdinalIgnoreCase));
				var name = ReadValue(row[nameIndex], columns[nameIndex], schema.Provider) as string ?? throw new InvalidDataException("The archived feature name is not text.");
				var version = checked((int)ReadInteger(row[versionIndex], columns[versionIndex], schema.Provider));
				if (!featureVersions.TryAdd(name, version)) throw new InvalidDataException("Duplicate archived feature version.");
			}
			if (++count > expected.RowCount) throw new InvalidDataException($"Backup row count for {table.Name} does not match.");
		}
		if (count != expected.RowCount || maximumIdentity != expected.MaximumIdentityValue || (string.Equals(table.Name, "DatabaseInfo", StringComparison.OrdinalIgnoreCase) && count != 1) ||
			(string.Equals(table.Name, "DepotFeatureVersions", StringComparison.OrdinalIgnoreCase) && !DatabaseBackupSchema.VersionsEqual(featureVersions, schema.FeatureVersions)))
			throw new InvalidDataException($"Backup data for {table.Name} is incomplete or inconsistent with the manifest.");
	}

	public static async Task<DatabaseBackupManifest> WriteAsync(
		Stream target, DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema,
		IProgress<DatabaseOperationProgress>? progress, CancellationToken cancellationToken)
	{
		var manifest = new DatabaseBackupManifest
		{
			FormatVersion = FormatVersion, ContractVersion = DatabaseBackupSchema.ContractVersion, CreatedUtc = DateTime.UtcNow,
			Provider = schema.Provider, SchemaVersion = schema.CoreVersion, SchemaFingerprint = schema.Fingerprint,
			FeatureVersions = schema.FeatureVersions, IdentityHighWaterMarks = schema.IdentityHighWaterMarks
		};
		using var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
		for (var index = 0; index < schema.Tables.Length; index++)
		{
			var table = schema.Tables[index];
			progress?.Report(new DatabaseOperationProgress(10 + index * 65 / schema.Tables.Length, $"Backing up {table.Name}..."));
			var entry = archive.CreateEntry(EntryName(table), CompressionLevel.Optimal);
			await using var destination = entry.Open();
			manifest.Tables.Add(await WriteTableAsync(destination, connection, transaction, schema, table, cancellationToken));
		}
		// Read allocator state after all tables have been read and locked by the
		// snapshot transaction, rather than retaining pre-snapshot counter values.
		var highWaterMarks = await DatabaseBackupSchema.ReadIdentityHighWaterMarksAsync(connection, transaction, schema.Provider, schema.Tables, cancellationToken);
		foreach (var pair in highWaterMarks)
		{
			var maximum = manifest.Tables.Single(table => table.Schema.Name == pair.Key).MaximumIdentityValue ?? 0;
			schema.IdentityHighWaterMarks[pair.Key] = Math.Max(pair.Value, maximum);
		}
		var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
		await using var manifestStream = manifestEntry.Open();
		await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
		return manifest;
	}

	internal static async Task<DatabaseBackupTableData> WriteTableAsync(
		Stream target, DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema, DatabaseBackupTable table, CancellationToken cancellationToken)
	{
		using var hash = SHA256.Create();
		await using var hashed = new CryptoStream(target, hash, CryptoStreamMode.Write, leaveOpen: true);
		await using var writer = new Utf8JsonWriter(hashed);
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandTimeout = 0;
		command.CommandText = schema.SelectSql(table);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		var identityIndex = Array.FindIndex(table.StoredColumns, column => column.IsIdentity);
		long? maximumIdentity = null;
		long count = 0;
		writer.WriteStartArray();
		while (await reader.ReadAsync(cancellationToken))
		{
			writer.WriteStartArray();
			for (var index = 0; index < reader.FieldCount; index++) WriteValue(writer, reader.GetValue(index));
			if (identityIndex >= 0)
			{
				var value = Convert.ToInt64(reader.GetValue(identityIndex), CultureInfo.InvariantCulture);
				maximumIdentity = maximumIdentity is null ? value : Math.Max(value, maximumIdentity.Value);
			}
			writer.WriteEndArray();
			if (++count % 100 == 0) await writer.FlushAsync(cancellationToken);
		}
		writer.WriteEndArray();
		await writer.FlushAsync(cancellationToken);
		await hashed.FlushFinalBlockAsync(cancellationToken);
		return new(table, count, Convert.ToHexString(hash.Hash!), maximumIdentity);
	}

	public async Task RestoreTableAsync(DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema, DatabaseBackupTable table, CancellationToken cancellationToken)
	{
		var identity = schema.Provider == DatabaseProvider.SqlServer && table.HasIdentity;
		if (identity) await ExecuteAsync(connection, transaction, $"SET IDENTITY_INSERT {schema.TableName(table.Name)} ON;", cancellationToken);
		try
		{
			var columns = table.StoredColumns;
			await using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandTimeout = 0;
			command.CommandText = $"INSERT INTO {schema.TableName(table.Name)} ({string.Join(",", columns.Select(column => schema.Quote(column.Name)))}) VALUES ({string.Join(",", columns.Select((_, index) => $"@Value{index}"))});";
			for (var index = 0; index < columns.Length; index++)
			{
				var parameter = command.CreateParameter();
				parameter.ParameterName = $"@Value{index}";
				ConfigureRestoreParameter(parameter, columns[index], schema.Provider);
				command.Parameters.Add(parameter);
			}
			await using var source = _archive.GetEntry(EntryName(table))!.Open();
			await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(source, cancellationToken: cancellationToken))
			{
				for (var index = 0; index < columns.Length; index++) command.Parameters[index].Value = ReadValue(row[index], columns[index], schema.Provider);
				await command.ExecuteNonQueryAsync(cancellationToken);
			}
		}
		finally
		{
			if (identity) await ExecuteAsync(connection, transaction, $"SET IDENTITY_INSERT {schema.TableName(table.Name)} OFF;", CancellationToken.None);
		}
	}

	internal static void ConfigureRestoreParameter(DbParameter parameter, DatabaseBackupColumn column, DatabaseProvider provider)
	{
		if (provider != DatabaseProvider.SqlServer) return;
		var storeType = column.StoreType.Split(':');
		DbType? type = storeType[0] switch
		{
			"date" => DbType.Date,
			"datetime2" => DbType.DateTime2,
			"datetimeoffset" => DbType.DateTimeOffset,
			"time" => DbType.Time,
			_ => null
		};
		if (type is null) return;
		// CLR DateTime otherwise infers SQL datetime and loses datetime2 precision.
		// Bind the live column contract before any values, including an initial null.
		parameter.DbType = type.Value;
		if (type == DbType.Date) return;
		if (storeType.Length != 4 || !byte.TryParse(storeType[3], NumberStyles.None, CultureInfo.InvariantCulture, out var scale) || scale > 7)
			throw new InvalidDataException($"The temporal column metadata for {column.Name} is invalid.");
		parameter.Scale = scale;
	}

	public async Task VerifyRestoredRowsAsync(DbConnection connection, DbTransaction transaction, DatabaseBackupSchema schema, CancellationToken cancellationToken)
	{
		for (var index = 0; index < schema.Tables.Length; index++)
		{
			var actual = await WriteTableAsync(Stream.Null, connection, transaction, schema, schema.Tables[index], cancellationToken);
			var expected = Manifest.Tables[index];
			if (actual.RowCount != expected.RowCount || actual.Sha256 != expected.Sha256)
				throw new InvalidDataException($"Restored values in {actual.Schema.Name} do not match the validated backup.");
		}
	}

	internal static void WriteValue(Utf8JsonWriter writer, object? value)
	{
		if (value is null or DBNull) { writer.WriteNullValue(); return; }
		var (kind, text) = value switch
		{
			byte[] bytes => ("binary", Convert.ToBase64String(bytes)),
			string textValue => ("string", textValue),
			bool boolean => ("boolean", boolean ? "true" : "false"),
			byte or sbyte or short or ushort or int or uint or long => ("integer", Convert.ToString(value, CultureInfo.InvariantCulture)!),
			ulong number => ("unsigned", number.ToString(CultureInfo.InvariantCulture)),
			decimal number => ("decimal", number.ToString("G29", CultureInfo.InvariantCulture)),
			double number when double.IsFinite(number) => ("double", number.ToString("R", CultureInfo.InvariantCulture)),
			float number when float.IsFinite(number) => ("single", number.ToString("R", CultureInfo.InvariantCulture)),
			DateTime dateTime => ("datetime", dateTime.ToString("O", CultureInfo.InvariantCulture)),
			DateTimeOffset offset => ("datetimeoffset", offset.ToString("O", CultureInfo.InvariantCulture)),
			TimeSpan span => ("timespan", span.ToString("c", CultureInfo.InvariantCulture)),
			Guid guid => ("guid", guid.ToString("D")),
			_ => throw new InvalidDataException($"Unsupported backup value type: {value.GetType().Name}.")
		};
		writer.WriteStartArray();
		writer.WriteStringValue(kind);
		writer.WriteStringValue(text);
		writer.WriteEndArray();
	}

	internal static object ReadValue(JsonElement cell, DatabaseBackupColumn column, DatabaseProvider provider)
	{
		if (cell.ValueKind == JsonValueKind.Null)
		{
			if (!column.IsNullable) throw new InvalidDataException($"A non-null column ({column.Name}) contains null.");
			return DBNull.Value;
		}
		if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() != 2 || cell[0].ValueKind != JsonValueKind.String || cell[1].ValueKind != JsonValueKind.String)
			throw new InvalidDataException($"The typed value for {column.Name} is damaged.");
		var kind = cell[0].GetString()!;
		var text = cell[1].GetString()!;
		var binaryColumn = column.StoreType.Contains("blob", StringComparison.OrdinalIgnoreCase) || column.StoreType.Contains("binary", StringComparison.OrdinalIgnoreCase);
		if ((kind == "binary") != binaryColumn) throw new InvalidDataException($"The binary value type for {column.Name} is incompatible.");
		try
		{
			return kind switch
			{
				"binary" => Convert.FromBase64String(text),
				"string" => text,
				"boolean" when text is "true" or "false" => text == "true",
				"integer" => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
				"unsigned" => ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
				"decimal" => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
				"double" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) => number,
				"single" when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number) => number,
				"datetime" => DateTime.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
				"datetimeoffset" => DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None),
				"timespan" => TimeSpan.ParseExact(text, "c", CultureInfo.InvariantCulture),
				"guid" => Guid.ParseExact(text, "D"),
				_ => throw new InvalidDataException($"Unsupported typed value for {column.Name}.")
			};
		}
		catch (Exception exception) when (exception is FormatException or OverflowException)
		{
			throw new InvalidDataException($"The typed value for {column.Name} is invalid.", exception);
		}
	}

	private static long ReadInteger(JsonElement cell, DatabaseBackupColumn column, DatabaseProvider provider) =>
		ReadValue(cell, column, provider) switch
		{
			long value => value,
			ulong value when value <= long.MaxValue => (long)value,
			_ => throw new InvalidDataException($"The archived integer column {column.Name} has an incompatible value type.")
		};

	internal static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = sql;
		command.CommandTimeout = 0;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static string EntryName(DatabaseBackupTable table) => $"tables/{table.Name}.json";

	public void Dispose()
	{
		_archive.Dispose();
		_stream.Dispose();
	}
}
