// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

namespace Depot.Data;

internal static class DatabaseSchemaContract
{
	private static readonly Lazy<DatabaseSchemaCompatibility> Contract = new(Build);
	public static DatabaseSchemaCompatibility Current => Contract.Value;

	private static DatabaseSchemaCompatibility Build() => new()
	{
		CoreVersion = DatabaseVersion.CurrentVersion,
		FeatureVersions = DatabaseSchemaRegistry.CurrentFeatureVersions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
		// The same independently provisioned reference schema used by backup validation covers
		// every feature and unversioned structural extension without a second hand-written table list.
		Tables = DatabaseBackupSchema.RequiredTableDefinitions.ToDictionary(table => table.Name,
			table => table.Columns.Select(column => column.Name).ToArray(), StringComparer.OrdinalIgnoreCase),
		Indexes = new(StringComparer.OrdinalIgnoreCase)
		{
			["Items"] = [new("IX_Items_Gtin", ["Gtin"], true, "Gtin IS NOT NULL")],
			["ItemTrackingUnits"] = [new("UX_ItemTrackingUnits_Item_Mode_Code", ["ItemId", "TrackingMode", "Code"], true)],
			["StockMovementTracking"] = [new("IX_StockMovementTracking_TrackingUnit", ["TrackingUnitId"], false)]
		}
	};
}
