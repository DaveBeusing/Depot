// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ItemRepository : DatabaseRepository
{
	private const string SelectColumns =
		"Id, PartNumber, Description, Manufacturer, Category, IsActive, Version";

	public ItemRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<long> CreateAsync(Item item, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"""
			INSERT INTO Items (PartNumber, Description, Manufacturer, Category, IsActive)
			VALUES ($PartNumber, $Description, $Manufacturer, $Category, $IsActive);
			""",
			cancellationToken,
			Parameter("$PartNumber", item.PartNumber),
			Parameter("$Description", item.Description),
			Parameter("$Manufacturer", item.Manufacturer),
			Parameter("$Category", item.Category),
			Parameter("$IsActive", item.IsActive));

	public async Task<bool> UpdateAsync(Item item, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"""
			UPDATE Items
			SET Description = $Description, Manufacturer = $Manufacturer, Category = $Category,
			    IsActive = $IsActive, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			cancellationToken,
			Parameter("$Id", item.Id),
			Parameter("$Description", item.Description),
			Parameter("$Manufacturer", item.Manufacturer),
			Parameter("$Category", item.Category),
			Parameter("$IsActive", item.IsActive),
			Parameter("$Version", item.Version)) == 1;

	public async Task<bool> DeactivateAsync(long id, long version, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Items SET IsActive = 0, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	public Task<PageResult<Item>> SearchPageAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = hasSearch
			? "IsActive = 1 AND (PartNumber LIKE $Search OR Description LIKE $Search OR Manufacturer LIKE $Search OR Category LIKE $Search)"
			: "IsActive = 1";
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QueryPageAsync(
			$"SELECT {SelectColumns} FROM Items WHERE {filter} ORDER BY PartNumber",
			$"SELECT COUNT(*) FROM Items WHERE {filter};",
			ReadItem,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<Item?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Items WHERE Id = $Id;",
			ReadItem,
			cancellationToken,
			Parameter("$Id", id));

	public Task<Item?> GetByPartNumberAsync(string partNumber, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Items WHERE PartNumber = $PartNumber;",
			ReadItem,
			cancellationToken,
			Parameter("$PartNumber", partNumber));

	public long Create(Item item) =>
		Database.Insert(
			"""
			INSERT INTO Items (PartNumber, Description, Manufacturer, Category, IsActive)
			VALUES ($PartNumber, $Description, $Manufacturer, $Category, $IsActive);
			""",
			Parameter("$PartNumber", item.PartNumber),
			Parameter("$Description", item.Description),
			Parameter("$Manufacturer", item.Manufacturer),
			Parameter("$Category", item.Category),
			Parameter("$IsActive", item.IsActive));

	public bool Update(Item item) =>
		Database.Execute(
			"""
			UPDATE Items
			SET Description = $Description,
			    Manufacturer = $Manufacturer,
			    Category = $Category,
			    IsActive = $IsActive,
			    Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", item.Id),
			Parameter("$Description", item.Description),
			Parameter("$Manufacturer", item.Manufacturer),
			Parameter("$Category", item.Category),
			Parameter("$IsActive", item.IsActive),
			Parameter("$Version", item.Version)) == 1;

	public bool Deactivate(long id, long version) =>
		Database.Execute(
			"""
			UPDATE Items
			SET IsActive = 0, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	public IReadOnlyList<Item> GetAll() => SearchActive(null);

	public IReadOnlyList<Item> SearchActive(string? searchText)
	{
		if (string.IsNullOrWhiteSpace(searchText))
		{
			return Database.Query(
				$"SELECT {SelectColumns} FROM Items WHERE IsActive = 1 ORDER BY PartNumber;",
				ReadItem);
		}

		return Database.Query(
			$"""
			SELECT {SelectColumns}
			FROM Items
			WHERE IsActive = 1
			  AND (PartNumber LIKE $Search
			       OR Description LIKE $Search
			       OR Manufacturer LIKE $Search
			       OR Category LIKE $Search)
			ORDER BY PartNumber;
			""",
			ReadItem,
			Parameter("$Search", $"%{searchText.Trim()}%"));
	}

	public Item? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Items WHERE Id = $Id;",
			ReadItem,
			Parameter("$Id", id));

	public Item? GetByPartNumber(string partNumber) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Items WHERE PartNumber = $PartNumber;",
			ReadItem,
			Parameter("$PartNumber", partNumber));

	private static Item ReadItem(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			PartNumber = reader.GetString(1),
			Description = reader.GetString(2),
			Manufacturer = reader.IsDBNull(3) ? null : reader.GetString(3),
			Category = reader.IsDBNull(4) ? null : reader.GetString(4),
			IsActive = reader.GetBoolean(5),
			Version = reader.GetInt64(6)
		};
}
