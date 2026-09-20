// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

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
			["EnterpriseIdentity"] = EnterpriseIdentitySchemaMigration.CurrentVersion
		};

	public static bool IsCurrent(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		try
		{
			using var connection = connectionFactory.CreateConnection();
			connection.Open();

			using (var coreCommand = connection.CreateCommand())
			{
				coreCommand.CommandText = "SELECT Version FROM DatabaseInfo;";
				var value = coreCommand.ExecuteScalar();
				if (value is null or DBNull ||
					Convert.ToInt32(value, CultureInfo.InvariantCulture) != DatabaseVersion.CurrentVersion)
					return false;
			}

			var found = new HashSet<string>(StringComparer.Ordinal);
			using var featureCommand = connection.CreateCommand();
			featureCommand.CommandText = "SELECT Name, Version FROM DepotFeatureVersions;";
			using var reader = featureCommand.ExecuteReader();
			while (reader.Read())
			{
				var name = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
				if (name is null || !ExpectedFeatureVersions.TryGetValue(name, out var expectedVersion)) continue;
				if (!found.Add(name)) return false;
				if (Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) != expectedVersion) return false;
			}

			return found.Count == ExpectedFeatureVersions.Count;
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
}
