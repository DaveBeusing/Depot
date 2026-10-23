// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

namespace Depot.Services;

internal sealed record DatabaseBackupColumn(
	string Name, string StoreType, bool IsNullable, bool IsGenerated, bool IsIdentity, int PrimaryKeyOrder);

internal sealed record DatabaseBackupTable(string Name, DatabaseBackupColumn[] Columns)
{
	[JsonIgnore]
	public DatabaseBackupColumn[] StoredColumns => Columns.Where(column => !column.IsGenerated).ToArray();

	[JsonIgnore]
	public bool HasIdentity => Columns.Any(column => column.IsIdentity);
}

internal sealed record DatabaseBackupForeignKey(string Table, string ParentTable, string[] Columns, string[] ParentColumns);
internal sealed record DatabaseBackupTrigger(string Name, string Table, string Definition);

/// <summary>
/// The contract comes from the running database, never from SQL supplied by an archive.
/// The shipped provisioning code supplies an independent minimum table/column contract.
/// </summary>
internal sealed class DatabaseBackupSchema
{
	public const int ContractVersion = 1;
	private static readonly Lazy<DatabaseBackupTable[]> RequiredTables = new(BuildRequiredTables);
	private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

	public required DatabaseProvider Provider { get; init; }
	public required DatabaseBackupTable[] Tables { get; init; }
	public required DatabaseBackupForeignKey[] ForeignKeys { get; init; }
	public required DatabaseBackupTrigger[] Triggers { get; init; }
	public required Dictionary<string, int> FeatureVersions { get; init; }
	public required Dictionary<string, long> IdentityHighWaterMarks { get; init; }
	public required int CoreVersion { get; init; }

	public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Tables)));

	public string Quote(string identifier)
	{
		ValidateIdentifier(identifier);
		return Provider == DatabaseProvider.MySql ? $"`{identifier}`" : $"[{identifier}]";
	}

	public string TableName(string identifier) => Provider == DatabaseProvider.SqlServer
		? $"[dbo].{Quote(identifier)}" : Quote(identifier);

	public string SelectSql(DatabaseBackupTable table)
	{
		var columns = table.StoredColumns;
		var key = columns.Where(column => column.PrimaryKeyOrder > 0).OrderBy(column => column.PrimaryKeyOrder).ToArray();
		var order = key.Length > 0 ? key : columns;
		return $"SELECT {string.Join(",", columns.Select(column => Quote(column.Name)))} FROM {TableName(table.Name)} ORDER BY {string.Join(",", order.Select(column => Quote(column.Name)))};";
	}

	public static async Task<DatabaseBackupSchema> ReadAsync(
		DbConnection connection, DbTransaction? transaction, DatabaseProvider provider, CancellationToken cancellationToken,
		bool requireCurrent = true)
	{
		var tables = await ReadTablesAsync(connection, transaction, provider, cancellationToken);
		var foreignKeys = await ReadForeignKeysAsync(connection, transaction, provider, tables, cancellationToken);
		var triggers = await ReadTriggersAsync(connection, transaction, provider, cancellationToken);
		var coreRows = await QueryAsync(connection, transaction, "SELECT Version FROM DatabaseInfo;", cancellationToken);
		if (coreRows.Count != 1) throw new InvalidDataException("The database must have exactly one core schema version.");
		var coreVersion = Integer(coreRows[0][0]);
		var features = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var row in await QueryAsync(connection, transaction, "SELECT Name,Version FROM DepotFeatureVersions ORDER BY Name;", cancellationToken))
			if (!features.TryAdd(Text(row[0]), Integer(row[1]))) throw new InvalidDataException("Duplicate feature schema version.");
		var sequences = await ReadIdentityHighWaterMarksAsync(connection, transaction, provider, tables, cancellationToken);

		var result = new DatabaseBackupSchema
		{
			Provider = provider, Tables = tables, ForeignKeys = foreignKeys, Triggers = triggers,
			CoreVersion = coreVersion, FeatureVersions = features, IdentityHighWaterMarks = sequences
		};
		if (requireCurrent) result.RequireCurrentSchema();
		return result;
	}

	internal static async Task<Dictionary<string, long>> ReadIdentityHighWaterMarksAsync(
		DbConnection connection, DbTransaction? transaction, DatabaseProvider provider, DatabaseBackupTable[] tables, CancellationToken cancellationToken)
	{
		var sequences = tables.Where(table => table.HasIdentity).ToDictionary(table => table.Name, _ => 0L, StringComparer.Ordinal);
		var identitySql = provider switch
		{
			DatabaseProvider.Local => "SELECT name,seq FROM sqlite_sequence;",
			DatabaseProvider.SqlServer => "SELECT t.name,COALESCE(c.last_value,0) FROM sys.identity_columns c JOIN sys.tables t ON t.object_id=c.object_id;",
			DatabaseProvider.MySql => "SELECT TABLE_NAME,COALESCE(AUTO_INCREMENT,1)-1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND AUTO_INCREMENT IS NOT NULL;",
			_ => throw new NotSupportedException()
		};
		foreach (var row in await QueryAsync(connection, transaction, identitySql, cancellationToken))
		{
			var name = Text(row[0]);
			if (sequences.ContainsKey(name)) sequences[name] = Math.Max(0, Convert.ToInt64(row[1], CultureInfo.InvariantCulture));
		}

		return sequences;
	}

	private void RequireCurrentSchema()
	{
		if (CoreVersion != DatabaseVersion.CurrentVersion || !VersionsEqual(FeatureVersions, CurrentFeatureVersions))
			throw new InvalidDataException("A complete backup requires the current core and feature schema versions. Complete database provisioning first.");
		RequireCompleteTables(Tables, RequiredTables.Value);
	}

	internal static void RequireCompleteTables(IReadOnlyList<DatabaseBackupTable> tables, IReadOnlyList<DatabaseBackupTable> requiredTables)
	{
		if (tables.Select(table => table.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tables.Count)
			throw new InvalidDataException("The database contains ambiguous table names differing only in case.");
		var actual = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
		foreach (var required in requiredTables)
		{
			if (!actual.TryGetValue(required.Name, out var table))
				throw new InvalidDataException($"The current schema is incomplete: table {required.Name} is missing.");
			var columns = table.Columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var column in required.Columns)
				if (!columns.Contains(column.Name))
					throw new InvalidDataException($"The current schema is incomplete: column {required.Name}.{column.Name} is missing.");
		}
	}

	internal static IReadOnlyDictionary<string, int> CurrentFeatureVersions => DatabaseSchemaRegistry.CurrentFeatureVersions;

	internal static IReadOnlyList<DatabaseBackupTable> RequiredTableDefinitions => RequiredTables.Value;

	internal static bool VersionsEqual(IReadOnlyDictionary<string, int> actual, IReadOnlyDictionary<string, int> expected) =>
		actual.Count == expected.Count && expected.All(pair => actual.TryGetValue(pair.Key, out var version) && version == pair.Value);

	private static DatabaseBackupTable[] BuildRequiredTables()
	{
		// A fresh reference database automatically includes every shipped migration, including
		// unversioned item fields. This avoids another manually maintained partial table list.
		var directory = Path.Combine(Path.GetTempPath(), $"depot-backup-contract-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		var factory = new SqliteConnectionFactory(Path.Combine(directory, "contract.db"));
		try
		{
			DatabaseProvisioningService.Initialize(factory);
			using var connection = factory.CreateConnection();
			connection.Open();
			return ReadTablesAsync(connection, null, DatabaseProvider.Local, CancellationToken.None).GetAwaiter().GetResult();
		}
		finally
		{
			using var poolConnection = factory.CreateConnection();
			SqliteConnection.ClearPool(poolConnection);
			Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task<DatabaseBackupTable[]> ReadTablesAsync(
		DbConnection connection, DbTransaction? transaction, DatabaseProvider provider, CancellationToken cancellationToken)
	{
		var columns = new Dictionary<string, List<DatabaseBackupColumn>>(StringComparer.Ordinal);
		if (provider == DatabaseProvider.Local)
		{
			var tables = await QueryAsync(connection, transaction,
				"SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;", cancellationToken);
			foreach (var table in tables)
			{
				var name = Text(table[0]);
				ValidateIdentifier(name);
				var identity = Text(table[1]).Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase);
				var list = new List<DatabaseBackupColumn>();
				foreach (var row in await QueryAsync(connection, transaction, $"PRAGMA table_xinfo([{name}]);", cancellationToken))
				{
					var pk = Integer(row[5]);
					list.Add(new DatabaseBackupColumn(Text(row[1]), Text(row[2]).ToUpperInvariant(), Integer(row[3]) == 0 && pk == 0,
						Integer(row[6]) != 0, identity && pk > 0, pk));
				}
				columns.Add(name, list);
			}
		}
		else if (provider == DatabaseProvider.SqlServer)
		{
			if (Convert.ToInt64(await ScalarAsync(connection, transaction,
				"SELECT (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1)+(SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1);",
				cancellationToken), CultureInfo.InvariantCulture) != 0)
				throw new InvalidDataException("Logical backup and restore require enabled, trusted SQL Server foreign-key and check constraints.");
			const string sql = """
				SELECT s.name,t.name,c.name,ty.name,c.max_length,c.precision,c.scale,c.is_nullable,c.is_computed,c.is_identity,COALESCE(pk.key_ordinal,0)
				FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
				JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
				LEFT JOIN (SELECT ic.object_id,ic.column_id,ic.key_ordinal FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE i.is_primary_key=1) pk ON pk.object_id=c.object_id AND pk.column_id=c.column_id
				WHERE t.is_ms_shipped=0 ORDER BY t.name,c.name;
				""";
			foreach (var row in await QueryAsync(connection, transaction, sql, cancellationToken))
			{
				if (Text(row[0]) != "dbo") throw new InvalidDataException("Logical backups require application tables in the dbo schema.");
				var name = Text(row[1]);
				if (!columns.TryGetValue(name, out var list)) columns.Add(name, list = []);
				var type = Text(row[3]).ToLowerInvariant();
				list.Add(new DatabaseBackupColumn(Text(row[2]), $"{type}:{row[4]}:{row[5]}:{row[6]}", Boolean(row[7]),
					Boolean(row[8]) || type is "timestamp" or "rowversion", Boolean(row[9]), Integer(row[10])));
			}
		}
		else if (provider == DatabaseProvider.MySql)
		{
			const string sql = """
				SELECT c.TABLE_NAME,c.COLUMN_NAME,c.COLUMN_TYPE,c.IS_NULLABLE,c.EXTRA,c.GENERATION_EXPRESSION,COALESCE(k.ORDINAL_POSITION,0),t.ENGINE
				FROM information_schema.COLUMNS c JOIN information_schema.TABLES t ON t.TABLE_SCHEMA=c.TABLE_SCHEMA AND t.TABLE_NAME=c.TABLE_NAME
				LEFT JOIN information_schema.KEY_COLUMN_USAGE k ON k.TABLE_SCHEMA=c.TABLE_SCHEMA AND k.TABLE_NAME=c.TABLE_NAME AND k.COLUMN_NAME=c.COLUMN_NAME AND k.CONSTRAINT_NAME='PRIMARY'
				WHERE c.TABLE_SCHEMA=DATABASE() AND t.TABLE_TYPE='BASE TABLE' ORDER BY c.TABLE_NAME,c.COLUMN_NAME;
				""";
			foreach (var row in await QueryAsync(connection, transaction, sql, cancellationToken))
			{
				if (!string.Equals(Text(row[7]), "InnoDB", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("Logical backup and atomic restore require InnoDB tables.");
				var name = Text(row[0]);
				if (!columns.TryGetValue(name, out var list)) columns.Add(name, list = []);
				list.Add(new DatabaseBackupColumn(Text(row[1]), Text(row[2]).ToLowerInvariant(), Text(row[3]) == "YES",
					!string.IsNullOrWhiteSpace(Text(row[5])), Text(row[4]).Contains("auto_increment", StringComparison.OrdinalIgnoreCase), Integer(row[6])));
			}
		}
		else throw new NotSupportedException($"Logical backups do not support provider '{provider}'.");

		foreach (var pair in columns)
		{
			ValidateIdentifier(pair.Key);
			foreach (var column in pair.Value) ValidateIdentifier(column.Name);
			if (pair.Value.Count == 0 || pair.Value.All(column => column.IsGenerated))
				throw new InvalidDataException($"Table {pair.Key} has no stored columns.");
		}
		return columns.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => new DatabaseBackupTable(pair.Key, pair.Value.OrderBy(column => column.Name, StringComparer.Ordinal).ToArray())).ToArray();
	}

	private static async Task<DatabaseBackupForeignKey[]> ReadForeignKeysAsync(
		DbConnection connection, DbTransaction? transaction, DatabaseProvider provider, DatabaseBackupTable[] tables, CancellationToken cancellationToken)
	{
		var result = new List<DatabaseBackupForeignKey>();
		if (provider == DatabaseProvider.Local)
		{
			foreach (var table in tables)
			{
				var rows = await QueryAsync(connection, transaction, $"PRAGMA foreign_key_list([{table.Name}]);", cancellationToken);
				foreach (var group in rows.GroupBy(row => Integer(row[0])))
				{
					var ordered = group.OrderBy(row => Integer(row[1])).ToArray();
					var parent = Text(ordered[0][2]);
					var parentKeys = tables.SingleOrDefault(candidate => candidate.Name == parent)?.Columns
						.Where(column => column.PrimaryKeyOrder > 0).OrderBy(column => column.PrimaryKeyOrder).Select(column => column.Name).ToArray() ?? [];
					result.Add(new(table.Name, parent, ordered.Select(row => Text(row[3])).ToArray(),
						ordered.Select((row, index) => row[4] is DBNull ? parentKeys.ElementAtOrDefault(index) ?? string.Empty : Text(row[4])).ToArray()));
				}
			}
		}
		else
		{
			var sql = provider == DatabaseProvider.SqlServer ? """
				SELECT fk.object_id,child.name,parent.name,cc.name,pc.name,fkc.constraint_column_id
				FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
				JOIN sys.tables child ON child.object_id=fk.parent_object_id JOIN sys.tables parent ON parent.object_id=fk.referenced_object_id
				JOIN sys.columns cc ON cc.object_id=child.object_id AND cc.column_id=fkc.parent_column_id
				JOIN sys.columns pc ON pc.object_id=parent.object_id AND pc.column_id=fkc.referenced_column_id
				ORDER BY fk.object_id,fkc.constraint_column_id;
				""" : """
				SELECT CONSTRAINT_NAME,TABLE_NAME,REFERENCED_TABLE_NAME,COLUMN_NAME,REFERENCED_COLUMN_NAME,ORDINAL_POSITION
				FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL
				AND REFERENCED_TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME,CONSTRAINT_NAME,ORDINAL_POSITION;
				""";
			var rows = await QueryAsync(connection, transaction, sql, cancellationToken);
			foreach (var group in rows.GroupBy(row => (Table: Text(row[1]), Key: Text(row[0]))))
			{
				var ordered = group.OrderBy(row => Integer(row[5])).ToArray();
				result.Add(new(group.Key.Table, Text(ordered[0][2]), ordered.Select(row => Text(row[3])).ToArray(), ordered.Select(row => Text(row[4])).ToArray()));
			}
			if (provider == DatabaseProvider.MySql && Convert.ToInt64(await ScalarAsync(connection, transaction,
				"SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND REFERENCED_TABLE_SCHEMA IS NOT NULL AND REFERENCED_TABLE_SCHEMA<>DATABASE();", cancellationToken), CultureInfo.InvariantCulture) != 0)
				throw new InvalidDataException("Cross-database foreign keys are not supported by logical backup and restore.");
		}
		foreach (var key in result)
		{
			var child = tables.SingleOrDefault(table => table.Name == key.Table);
			var parent = tables.SingleOrDefault(table => table.Name == key.ParentTable);
			if (child is null || parent is null || !key.Columns.All(name => child.Columns.Any(column => column.Name == name)) ||
				!key.ParentColumns.All(name => parent.Columns.Any(column => column.Name == name)))
				throw new InvalidDataException($"Foreign key metadata for {key.Table} is incomplete.");
		}
		return result.ToArray();
	}

	private static async Task<DatabaseBackupTrigger[]> ReadTriggersAsync(
		DbConnection connection, DbTransaction? transaction, DatabaseProvider provider, CancellationToken cancellationToken)
	{
		var sql = provider switch
		{
			DatabaseProvider.Local => "SELECT name,tbl_name,sql FROM sqlite_master WHERE type='trigger' ORDER BY name;",
			DatabaseProvider.SqlServer => "SELECT tr.name,t.name,'' FROM sys.triggers tr JOIN sys.tables t ON t.object_id=tr.parent_id WHERE tr.is_disabled=0 ORDER BY tr.name;",
			DatabaseProvider.MySql => "SELECT TRIGGER_NAME,EVENT_OBJECT_TABLE,ACTION_STATEMENT FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE();",
			_ => throw new NotSupportedException()
		};
		var rows = await QueryAsync(connection, transaction, sql, cancellationToken);
		if (provider == DatabaseProvider.MySql && rows.Count != 0)
			throw new InvalidDataException("Logical restore cannot safely suspend custom MySQL/MariaDB triggers. Use a native database backup for this schema.");
		return rows.Select(row =>
		{
			ValidateIdentifier(Text(row[0]));
			ValidateIdentifier(Text(row[1]));
			return new DatabaseBackupTrigger(Text(row[0]), Text(row[1]), Text(row[2]));
		}).ToArray();
	}

	public async Task ValidateForeignKeysAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
	{
		if (Provider == DatabaseProvider.Local)
		{
			if ((await QueryAsync(connection, transaction, "PRAGMA foreign_key_check;", cancellationToken)).Count > 0)
				throw new InvalidDataException("Restored records violate foreign key constraints.");
			return;
		}
		foreach (var key in ForeignKeys)
		{
			var nonNull = string.Join(" AND ", key.Columns.Select(column => $"c.{Quote(column)} IS NOT NULL"));
			var match = string.Join(" AND ", key.Columns.Select((column, index) => $"c.{Quote(column)}=p.{Quote(key.ParentColumns[index])}"));
			var sql = $"SELECT COUNT(*) FROM {TableName(key.Table)} c WHERE {nonNull} AND NOT EXISTS (SELECT 1 FROM {TableName(key.ParentTable)} p WHERE {match});";
			if (Convert.ToInt64(await ScalarAsync(connection, transaction, sql, cancellationToken), CultureInfo.InvariantCulture) != 0)
				throw new InvalidDataException($"Restored records in {key.Table} violate a foreign key to {key.ParentTable}.");
		}
	}

	private static void ValidateIdentifier(string name)
	{
		if (!IdentifierPattern.IsMatch(name)) throw new InvalidDataException($"The database contains an unsupported SQL identifier: {name}.");
	}

	internal static async Task<List<object[]>> QueryAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = sql;
		command.CommandTimeout = 0;
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		var result = new List<object[]>();
		while (await reader.ReadAsync(cancellationToken))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			result.Add(row);
		}
		return result;
	}

	internal static async Task<object?> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = sql;
		command.CommandTimeout = 0;
		return await command.ExecuteScalarAsync(cancellationToken);
	}

	private static string Text(object value) => value is DBNull ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
	private static int Integer(object value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);
	private static bool Boolean(object value) => Convert.ToBoolean(value, CultureInfo.InvariantCulture);
}

