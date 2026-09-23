// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Models;

namespace Depot.Data;

internal static class DatabaseSchemaStateInspector
{
	private static readonly IReadOnlyDictionary<string, int> ExpectedFeatureVersions =
		new Dictionary<string, int>(StringComparer.Ordinal)
		{
			["Sales"] = SalesSchemaMigration.CurrentVersion,
			["Finance"] = FinanceInventoryAccountingSchemaMigration.CurrentVersion,
			["UserSessions"] = UserSessionSchemaMigration.CurrentVersion,
			["SecurityEvents"] = SecurityEventSchemaMigration.CurrentVersion,
			["UserPreferences"] = UserPreferenceSchemaMigration.CurrentVersion,
			["DocumentTemplates"] = DocumentTemplateSchemaMigration.CurrentVersion,
			["ApprovalPolicies"] = ApprovalPolicySchemaMigration.CurrentVersion,
			["EnterpriseIdentity"] = EnterpriseIdentitySchemaMigration.CurrentVersion,
			["BusinessAttachments"] = BusinessAttachmentSchemaMigration.CurrentVersion
		};

	public static bool IsCurrent(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		try
		{
			using var connection = connectionFactory.CreateConnection();
			connection.Open();

			if (!HasCurrentCoreVersion(connection)) return false;
			if (!HasCurrentFeatureVersions(connection)) return false;
			if (!HasCurrentUnversionedSchema(connection, connectionFactory.Provider)) return false;
			if (!HasCurrentReferenceDefaults(connection)) return false;
			return HasCurrentRbacCatalog(connection);
		}
		catch (DbException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
		catch (FormatException)
		{
			return false;
		}
		catch (OverflowException)
		{
			return false;
		}
	}

	private static bool HasCurrentCoreVersion(DbConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Version FROM DatabaseInfo;";
		var value = command.ExecuteScalar();
		return value is not null and not DBNull &&
			Convert.ToInt32(value, CultureInfo.InvariantCulture) == DatabaseVersion.CurrentVersion;
	}

	private static bool HasCurrentFeatureVersions(DbConnection connection)
	{
		var found = new HashSet<string>(StringComparer.Ordinal);
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Name, Version FROM DepotFeatureVersions;";
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			var name = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
			if (name is null || !ExpectedFeatureVersions.TryGetValue(name, out var expectedVersion)) continue;
			if (!found.Add(name)) return false;
			if (Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) != expectedVersion) return false;
		}
		return found.Count == ExpectedFeatureVersions.Count;
	}

	private static bool HasCurrentUnversionedSchema(DbConnection connection, DatabaseProvider provider)
	{
		var itemColumns = ReadNames(connection, provider switch
		{
			DatabaseProvider.Local => "SELECT name FROM pragma_table_info('Items');",
			DatabaseProvider.SqlServer => "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'Items');",
			DatabaseProvider.MySql => "SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'Items';",
			_ => throw new NotSupportedException($"Schema inspection is not supported for provider '{provider}'.")
		});
		if (!ItemMasterDataSchema.RequiredColumns.All(itemColumns.Contains)) return false;
		if (!IndexExists(connection, provider, "Items", "IX_Items_Gtin")) return false;

		var tables = ReadNames(connection, provider switch
		{
			DatabaseProvider.Local => "SELECT name FROM sqlite_master WHERE type = 'table';",
			DatabaseProvider.SqlServer => "SELECT name FROM sys.tables;",
			DatabaseProvider.MySql => "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE();",
			_ => throw new NotSupportedException($"Schema inspection is not supported for provider '{provider}'.")
		});
		if (!ItemTraceabilitySchema.RequiredTables.All(tables.Contains)) return false;
		return ItemTraceabilitySchema.RequiredIndexes.All(required =>
			IndexExists(connection, provider, required.Table, required.Index));
	}

	private static bool HasCurrentReferenceDefaults(DbConnection connection)
	{
		var units = ReadNames(connection, "SELECT Name FROM UnitsOfMeasure;", StringComparer.OrdinalIgnoreCase);
		if (!ItemReferenceDataDefaults.RequiredUnitNames.All(units.Contains)) return false;
		var packagings = ReadNames(connection, "SELECT Name FROM Packagings;", StringComparer.OrdinalIgnoreCase);
		return ItemReferenceDataDefaults.RequiredPackagingNames.All(packagings.Contains);
	}

	private static bool HasCurrentRbacCatalog(DbConnection connection)
	{
		var permissions = new Dictionary<string, (string Name, string Module, string Action)>(StringComparer.Ordinal);
		using (var command = connection.CreateCommand())
		{
			command.CommandText = "SELECT Code, Name, Module, Action FROM Permissions;";
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var code = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
				if (string.IsNullOrWhiteSpace(code) || !permissions.TryAdd(
					code,
					(
						Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
						Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
						Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty)))
					return false;
			}
		}
		foreach (var expected in PermissionCatalog.Definitions)
		{
			if (!permissions.TryGetValue(expected.Code, out var actual) ||
				actual.Name != expected.Name ||
				actual.Module != expected.Module ||
				actual.Action != expected.Action)
				return false;
		}

		var roles = new Dictionary<string, (string Name, string Description, bool IsActive)>(StringComparer.Ordinal);
		using (var command = connection.CreateCommand())
		{
			command.CommandText = "SELECT Code, Name, Description, IsActive FROM Roles WHERE IsSystem = 1;";
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var code = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
				if (string.IsNullOrWhiteSpace(code) || !roles.TryAdd(
					code,
					(
						Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
						Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
						Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture) != 0)))
					return false;
			}
		}

		var rolePermissions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		using (var command = connection.CreateCommand())
		{
			command.CommandText =
				"SELECT r.Code, p.Code FROM Roles r JOIN RolePermissions rp ON rp.RoleId = r.Id JOIN Permissions p ON p.Id = rp.PermissionId WHERE r.IsSystem = 1;";
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var roleCode = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
				var permissionCode = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture);
				if (string.IsNullOrWhiteSpace(roleCode) || string.IsNullOrWhiteSpace(permissionCode)) return false;
				if (!rolePermissions.TryGetValue(roleCode, out var actual))
				rolePermissions[roleCode] = actual = new HashSet<string>(StringComparer.Ordinal);
				if (!actual.Add(permissionCode)) return false;
			}
		}

		foreach (var expected in SystemRoleCatalog.Definitions)
		{
			if (!roles.TryGetValue(expected.Code, out var actualRole) ||
				actualRole.Name != expected.Name ||
				actualRole.Description != expected.Description ||
				!actualRole.IsActive)
				return false;
			if (!rolePermissions.TryGetValue(expected.Code, out var actualPermissions)) return false;
			var expectedPermissions = expected.Permissions.Select(PermissionCatalog.Code).ToHashSet(StringComparer.Ordinal);
			if (!actualPermissions.SetEquals(expectedPermissions)) return false;
		}
		return true;
	}

	private static HashSet<string> ReadNames(
		DbConnection connection,
		string sql,
		IEqualityComparer<string>? comparer = null)
	{
		var values = new HashSet<string>(comparer ?? StringComparer.Ordinal);
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			var value = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
			if (string.IsNullOrWhiteSpace(value) || !values.Add(value)) continue;
		}
		return values;
	}

	private static bool IndexExists(DbConnection connection, DatabaseProvider provider, string table, string index)
	{
		using var command = connection.CreateCommand();
		command.CommandText = provider switch
		{
			DatabaseProvider.Local => "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = @Table AND name = @Index;",
			DatabaseProvider.SqlServer => "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@Table) AND name = @Index;",
			DatabaseProvider.MySql => "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = @Table AND index_name = @Index;",
			_ => throw new NotSupportedException($"Index inspection is not supported for provider '{provider}'.")
		};
		AddParameter(command, "@Table", table);
		AddParameter(command, "@Index", index);
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
	}

	private static void AddParameter(DbCommand command, string name, object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
	}
}
