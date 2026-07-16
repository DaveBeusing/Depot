// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class InventoryRepository : DatabaseRepository
{
	private const string SelectColumns = "Id, ItemId, PurposeId, LocationId, IsActive, Version";

	public InventoryRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public IReadOnlyList<Inventory> GetAll() =>
		Database.Query(
			$"SELECT {SelectColumns} FROM Inventories WHERE IsActive = 1 ORDER BY ItemId;",
			ReadInventory);

	public IReadOnlyList<Inventory> GetByItem(long itemId) =>
		Database.Query(
			$"""
			SELECT {SelectColumns}
			FROM Inventories
			WHERE ItemId = $ItemId AND IsActive = 1
			ORDER BY PurposeId, LocationId;
			""",
			ReadInventory,
			Parameter("$ItemId", itemId));

	public Inventory? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Inventories WHERE Id = $Id;",
			ReadInventory,
			Parameter("$Id", id));

	public Inventory? GetByItemPurposeLocation(long itemId, long purposeId, long locationId) =>
		Database.QuerySingleOrDefault(
			$"""
			SELECT {SelectColumns}
			FROM Inventories
			WHERE ItemId = $ItemId
			  AND PurposeId = $PurposeId
			  AND LocationId = $LocationId;
			""",
			ReadInventory,
			Parameter("$ItemId", itemId),
			Parameter("$PurposeId", purposeId),
			Parameter("$LocationId", locationId));

	public long Create(Inventory inventory) =>
		Database.Insert(
			"""
			INSERT INTO Inventories (ItemId, PurposeId, LocationId, IsActive)
			VALUES ($ItemId, $PurposeId, $LocationId, $IsActive);
			""",
			Parameter("$ItemId", inventory.ItemId),
			Parameter("$PurposeId", inventory.PurposeId),
			Parameter("$LocationId", inventory.LocationId),
			Parameter("$IsActive", inventory.IsActive));

	public bool Deactivate(long id, long version) =>
		Database.Execute(
			"""
			UPDATE Inventories
			SET IsActive = 0, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	private static Inventory ReadInventory(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			ItemId = reader.GetInt64(1),
			PurposeId = reader.GetInt64(2),
			LocationId = reader.GetInt64(3),
			IsActive = reader.GetBoolean(4),
			Version = reader.GetInt64(5)
		};
}
