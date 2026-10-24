// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

using Depot.Models;

namespace Depot.Data;

/// <summary>Read-only metadata inspection shared by Depot and Depot Manager. Never initializes a database.</summary>
public static class DatabaseCompatibilityReader
{
	public static async Task<DatabaseSchemaCompatibility> ReadAsync(DbConnection connection, DatabaseProvider provider, CancellationToken cancellationToken)
	{
		var core = await QueryAsync(connection, "SELECT Version FROM DatabaseInfo;", cancellationToken);
		if (core.Count != 1) throw new InvalidDataException("Exactly one database core version is required.");
		var tables = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		var definitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var indexes = new Dictionary<string, DatabaseSchemaIndex[]>(StringComparer.OrdinalIgnoreCase);
		if (provider == DatabaseProvider.Local)
		{
			foreach (var row in await QueryAsync(connection, "SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;", cancellationToken))
			{
				var name = Text(row[0]);
				ValidateIdentifier(name);
				var columns = await QueryAsync(connection, $"PRAGMA table_xinfo([{name}]);", cancellationToken);
				tables.Add(name, columns.Select(column => Text(column[1])).ToArray());
				definitions.Add(name, Text(row[1]) + "|" + JsonSerializer.Serialize(columns.Select(column => column.Select(Text).ToArray())));
				var tableIndexes = new List<DatabaseSchemaIndex>();
				foreach (var index in await QueryAsync(connection, $"PRAGMA index_list([{name}]);", cancellationToken))
				{
					var indexName = Text(index[1]);
					ValidateIdentifier(indexName);
					var indexColumns = await QueryAsync(connection, $"PRAGMA index_xinfo([{indexName}]);", cancellationToken);
					var definition = await QueryAsync(connection, "SELECT sql FROM sqlite_master WHERE type='index' AND name=@Name;", cancellationToken, indexName);
					var sql = definition.Count == 0 ? string.Empty : Text(definition[0][0]);
					var where = sql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
					var filter = where < 0 ? null : sql[(where + 7)..].Trim().TrimEnd(';');
					tableIndexes.Add(new(indexName, indexColumns.Where(column => Integer(column[5]) == 1)
						.Select(column => Text(column[2]) is { Length: > 0 } columnName ? columnName + (Integer(column[3]) == 1 ? " DESC" : string.Empty) : "<expression>").ToArray(),
						Integer(index[2]) == 1, filter, sql + "|" + JsonSerializer.Serialize(indexColumns.Select(column => column.Select(Text).ToArray()))));
				}
				if (tableIndexes.Count > 0) indexes.Add(name, tableIndexes.ToArray());
			}
		}
		else if (provider is DatabaseProvider.SqlServer or DatabaseProvider.MySql)
		{
			var columnSql = provider == DatabaseProvider.SqlServer
				? "SELECT t.name,c.name,ty.name,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_computed,COALESCE(dc.definition,''),COALESCE(cc.definition,'') FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id WHERE t.is_ms_shipped=0 AND s.name='dbo' ORDER BY t.name,c.column_id;"
				: "SELECT c.TABLE_NAME,c.COLUMN_NAME,c.COLUMN_TYPE,c.IS_NULLABLE,CASE WHEN c.COLUMN_DEFAULT IS NULL THEN 1 ELSE 0 END,COALESCE(c.COLUMN_DEFAULT,''),c.EXTRA,COALESCE(c.GENERATION_EXPRESSION,''),t.ENGINE FROM information_schema.COLUMNS c JOIN information_schema.TABLES t ON t.TABLE_SCHEMA=c.TABLE_SCHEMA AND t.TABLE_NAME=c.TABLE_NAME WHERE c.TABLE_SCHEMA=DATABASE() AND t.TABLE_TYPE='BASE TABLE' ORDER BY c.TABLE_NAME,c.ORDINAL_POSITION;";
			foreach (var group in (await QueryAsync(connection, columnSql, cancellationToken)).GroupBy(row => Text(row[0]), StringComparer.OrdinalIgnoreCase))
			{
				tables.Add(group.Key, group.Select(row => Text(row[1])).ToArray());
				definitions.Add(group.Key, JsonSerializer.Serialize(group.Select(row => row.Select(Text).ToArray())));
			}
			var indexSql = provider == DatabaseProvider.SqlServer ? """
				SELECT t.name,i.name,i.is_unique,COALESCE(i.filter_definition,''),c.name,ic.key_ordinal,ic.is_included_column,ic.is_descending_key,i.type_desc,i.is_disabled
				FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id
				JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
				WHERE t.is_ms_shipped=0 AND s.name='dbo' AND i.name IS NOT NULL ORDER BY t.name,i.name,ic.key_ordinal,ic.index_column_id;
				""" : """
				SELECT TABLE_NAME,INDEX_NAME,1-NON_UNIQUE,'',COLUMN_NAME,SEQ_IN_INDEX,0,COLLATION,INDEX_TYPE,COALESCE(SUB_PART,0)
				FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME,INDEX_NAME,SEQ_IN_INDEX;
				""";
			foreach (var table in (await QueryAsync(connection, indexSql, cancellationToken)).GroupBy(row => Text(row[0]), StringComparer.OrdinalIgnoreCase))
				indexes.Add(table.Key, table.GroupBy(row => Text(row[1]), StringComparer.OrdinalIgnoreCase).Select(index =>
				{
					var rows = index.ToArray();
					return new DatabaseSchemaIndex(index.Key, rows.Where(row => Integer(row[6]) == 0).Select(row => Text(row[4]) +
						((provider == DatabaseProvider.SqlServer ? Integer(row[7]) == 1 : Text(row[7]) == "D") ? " DESC" : string.Empty) +
						(provider == DatabaseProvider.MySql && Integer(row[9]) > 0 ? $" PREFIX({Integer(row[9])})" : string.Empty)).ToArray(),
						Integer(rows[0][2]) != 0, Text(rows[0][3]), JsonSerializer.Serialize(rows.Select(row => row.Select(Text).ToArray())),
						provider != DatabaseProvider.SqlServer || Integer(rows[0][9]) == 0);
				}).ToArray());
		}
		else throw new NotSupportedException($"Schema inspection does not support provider '{provider}'.");

		var otherDefinitions = new Dictionary<string, string>(StringComparer.Ordinal);
		var mysqlCheckEnforcement = false;
		if (provider == DatabaseProvider.MySql)
		{
			var capability = await QueryAsync(connection, "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='information_schema' AND TABLE_NAME='TABLE_CONSTRAINTS' AND COLUMN_NAME='ENFORCED';", cancellationToken);
			mysqlCheckEnforcement = Integer(capability[0][0]) > 0;
		}
		var definitionQueries = provider switch
		{
			DatabaseProvider.Local => new[]
			{
				"SELECT type,name,sql FROM sqlite_master WHERE type IN ('trigger','view') ORDER BY type,name;"
			},
			DatabaseProvider.SqlServer => new[]
			{
				"SELECT 'foreign-key',s.name+'.'+t.name+'.'+fk.name,fk.delete_referential_action_desc,fk.update_referential_action_desc,fk.is_disabled,fk.is_not_trusted,cc.name,ps.name,pt.name,pc.name,fkc.constraint_column_id FROM sys.foreign_keys fk JOIN sys.tables t ON t.object_id=fk.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id JOIN sys.columns cc ON cc.object_id=fkc.parent_object_id AND cc.column_id=fkc.parent_column_id JOIN sys.tables pt ON pt.object_id=fkc.referenced_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id JOIN sys.columns pc ON pc.object_id=fkc.referenced_object_id AND pc.column_id=fkc.referenced_column_id ORDER BY s.name,t.name,fk.name,fkc.constraint_column_id;",
				"SELECT 'check',OBJECT_SCHEMA_NAME(parent_object_id)+'.'+OBJECT_NAME(parent_object_id)+'.'+name,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY parent_object_id,name;",
				"SELECT 'trigger',OBJECT_SCHEMA_NAME(object_id)+'.'+name,OBJECT_DEFINITION(object_id),is_disabled FROM sys.triggers WHERE is_ms_shipped=0 ORDER BY name;",
				"SELECT 'view',SCHEMA_NAME(schema_id)+'.'+name,OBJECT_DEFINITION(object_id) FROM sys.views WHERE is_ms_shipped=0 ORDER BY schema_id,name;"
			},
			DatabaseProvider.MySql => new[]
			{
				"SELECT 'foreign-key',CONCAT(k.TABLE_NAME,'.',k.CONSTRAINT_NAME),k.COLUMN_NAME,k.REFERENCED_TABLE_SCHEMA,k.REFERENCED_TABLE_NAME,k.REFERENCED_COLUMN_NAME,r.UPDATE_RULE,r.DELETE_RULE,k.ORDINAL_POSITION FROM information_schema.KEY_COLUMN_USAGE k JOIN information_schema.REFERENTIAL_CONSTRAINTS r ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA AND r.TABLE_NAME=k.TABLE_NAME AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME WHERE k.TABLE_SCHEMA=DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL ORDER BY k.TABLE_NAME,k.CONSTRAINT_NAME,k.ORDINAL_POSITION;",
				mysqlCheckEnforcement
                    ? "SELECT 'check',CONCAT(t.TABLE_NAME,'.',c.CONSTRAINT_NAME),c.CHECK_CLAUSE,t.ENFORCED FROM information_schema.CHECK_CONSTRAINTS c JOIN information_schema.TABLE_CONSTRAINTS t ON t.CONSTRAINT_SCHEMA=c.CONSTRAINT_SCHEMA AND t.CONSTRAINT_NAME=c.CONSTRAINT_NAME AND t.CONSTRAINT_TYPE='CHECK' WHERE c.CONSTRAINT_SCHEMA=DATABASE() ORDER BY t.TABLE_NAME,c.CONSTRAINT_NAME;"
                    : "SELECT 'check',CONCAT(t.TABLE_NAME,'.',c.CONSTRAINT_NAME),c.CHECK_CLAUSE FROM information_schema.CHECK_CONSTRAINTS c JOIN information_schema.TABLE_CONSTRAINTS t ON t.CONSTRAINT_SCHEMA=c.CONSTRAINT_SCHEMA AND t.TABLE_NAME=c.TABLE_NAME AND t.CONSTRAINT_NAME=c.CONSTRAINT_NAME AND t.CONSTRAINT_TYPE='CHECK' WHERE c.CONSTRAINT_SCHEMA=DATABASE() ORDER BY t.TABLE_NAME,c.CONSTRAINT_NAME;",
				"SELECT 'trigger',TRIGGER_NAME,ACTION_STATEMENT,EVENT_OBJECT_TABLE,ACTION_TIMING,EVENT_MANIPULATION FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() ORDER BY TRIGGER_NAME;",
				"SELECT 'view',TABLE_NAME,VIEW_DEFINITION,CHECK_OPTION,IS_UPDATABLE FROM information_schema.VIEWS WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME;"
			},
			_ => throw new NotSupportedException()
		};
		foreach (var query in definitionQueries)
			foreach (var group in (await QueryAsync(connection, query, cancellationToken)).GroupBy(row => Text(row[0]) + ":" + Text(row[1]), StringComparer.Ordinal))
			{
				if (group.Any(row => string.IsNullOrWhiteSpace(Text(row[2])))) throw new InvalidDataException("Database object definitions cannot be inspected with the current permissions.");
				otherDefinitions.Add(group.Key, JsonSerializer.Serialize(group.Select(row => row.Select(Text).ToArray())));
			}
		var features = new Dictionary<string, int>(StringComparer.Ordinal);
		if (tables.ContainsKey("DepotFeatureVersions"))
			foreach (var row in await QueryAsync(connection, "SELECT Name,Version FROM DepotFeatureVersions ORDER BY Name;", cancellationToken))
				if (!features.TryAdd(Text(row[0]), Integer(row[1]))) throw new InvalidDataException("Duplicate feature schema version.");
		var result = new DatabaseSchemaCompatibility
		{
			CoreVersion = Integer(core[0][0]), Provider = provider, FeatureVersions = features, Tables = tables, TableDefinitions = definitions, OtherDefinitions = otherDefinitions, Indexes = indexes
		};
		result.Validate();
		return result;
	}

	private static async Task<List<object[]>> QueryAsync(DbConnection connection, string sql, CancellationToken cancellationToken, string? name = null)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		if (name is not null)
		{
			var parameter = command.CreateParameter(); parameter.ParameterName = "@Name"; parameter.Value = name; command.Parameters.Add(parameter);
		}
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		var rows = new List<object[]>();
		while (await reader.ReadAsync(cancellationToken))
		{
			var row = new object[reader.FieldCount]; reader.GetValues(row); rows.Add(row);
		}
		return rows;
	}

	private static void ValidateIdentifier(string name)
	{
		if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
			throw new InvalidDataException("The database contains an unsupported schema identifier.");
	}
	private static string Text(object value) => value is DBNull ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
	private static int Integer(object value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);
}
