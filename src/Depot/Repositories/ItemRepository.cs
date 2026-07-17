// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ItemRepository : DatabaseRepository
{
	private const string SelectColumns =
		"i.Id, i.PartNumber, i.Description, m.Name, c.Name, u.Name, pk.Name, s.Name, i.IsActive, i.Version, i.ManufacturerId, i.CategoryId, i.UnitOfMeasureId, i.PackagingId, i.SupplierId";
	private const string SelectFrom =
		"FROM Items i LEFT JOIN Manufacturers m ON m.Id = i.ManufacturerId LEFT JOIN Categories c ON c.Id = i.CategoryId LEFT JOIN UnitsOfMeasure u ON u.Id = i.UnitOfMeasureId LEFT JOIN Packagings pk ON pk.Id = i.PackagingId LEFT JOIN Suppliers s ON s.Id = i.SupplierId";

	public ItemRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<long> CreateAsync(Item item, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"""
			INSERT INTO Items (PartNumber, Description, ManufacturerId, CategoryId, UnitOfMeasureId, PackagingId, SupplierId, IsActive)
			VALUES ($PartNumber, $Description, $ManufacturerId, $CategoryId, $UnitOfMeasureId, $PackagingId, $SupplierId, $IsActive);
			""",
			cancellationToken,
			Parameter("$PartNumber", item.PartNumber),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId), Parameter("$SupplierId", item.SupplierId),
			Parameter("$IsActive", item.IsActive));

	public async Task<bool> UpdateAsync(Item item, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"""
			UPDATE Items
			SET Description = $Description, ManufacturerId = $ManufacturerId, CategoryId = $CategoryId,
			    UnitOfMeasureId = $UnitOfMeasureId, PackagingId = $PackagingId, SupplierId = $SupplierId,
			    IsActive = $IsActive, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			cancellationToken,
			Parameter("$Id", item.Id),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId), Parameter("$SupplierId", item.SupplierId),
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
			? "i.IsActive = 1 AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR m.Name LIKE $Search OR c.Name LIKE $Search OR u.Name LIKE $Search OR pk.Name LIKE $Search OR s.Name LIKE $Search)"
			: "i.IsActive = 1";
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QueryPageAsync(
			$"SELECT {SelectColumns} {SelectFrom} WHERE {filter} ORDER BY i.PartNumber",
			$"SELECT COUNT(*) {SelectFrom} WHERE {filter};",
			ReadItem,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<Item?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} {SelectFrom} WHERE i.Id = $Id;",
			ReadItem,
			cancellationToken,
			Parameter("$Id", id));

	public Task<Item?> GetByPartNumberAsync(string partNumber, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} {SelectFrom} WHERE i.PartNumber = $PartNumber;",
			ReadItem,
			cancellationToken,
			Parameter("$PartNumber", partNumber));

	public long Create(Item item) =>
		Database.Insert(
			"""
			INSERT INTO Items (PartNumber, Description, ManufacturerId, CategoryId, UnitOfMeasureId, PackagingId, SupplierId, IsActive)
			VALUES ($PartNumber, $Description, $ManufacturerId, $CategoryId, $UnitOfMeasureId, $PackagingId, $SupplierId, $IsActive);
			""",
			Parameter("$PartNumber", item.PartNumber),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId), Parameter("$SupplierId", item.SupplierId),
			Parameter("$IsActive", item.IsActive));

	public bool Update(Item item) =>
		Database.Execute(
			"""
			UPDATE Items
			SET Description = $Description,
			    ManufacturerId = $ManufacturerId,
			    CategoryId = $CategoryId,
			    UnitOfMeasureId = $UnitOfMeasureId,
			    PackagingId = $PackagingId,
			    SupplierId = $SupplierId,
			    IsActive = $IsActive,
			    Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", item.Id),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId), Parameter("$SupplierId", item.SupplierId),
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
				$"SELECT {SelectColumns} {SelectFrom} WHERE i.IsActive = 1 ORDER BY i.PartNumber;",
				ReadItem);
		}

		return Database.Query(
			$"""
			SELECT {SelectColumns} {SelectFrom}
			WHERE i.IsActive = 1
			  AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR m.Name LIKE $Search OR c.Name LIKE $Search OR u.Name LIKE $Search OR pk.Name LIKE $Search OR s.Name LIKE $Search)
			ORDER BY i.PartNumber;
			""",
			ReadItem,
			Parameter("$Search", $"%{searchText.Trim()}%"));
	}

	public Item? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} {SelectFrom} WHERE i.Id = $Id;",
			ReadItem,
			Parameter("$Id", id));

	public Item? GetByPartNumber(string partNumber) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} {SelectFrom} WHERE i.PartNumber = $PartNumber;",
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
			UnitOfMeasure = reader.IsDBNull(5) ? null : reader.GetString(5),
			Packaging = reader.IsDBNull(6) ? null : reader.GetString(6),
			Supplier = reader.IsDBNull(7) ? null : reader.GetString(7),
			IsActive = reader.GetBoolean(8),
			Version = reader.GetInt64(9),
			ManufacturerId = reader.IsDBNull(10) ? null : reader.GetInt64(10),
			CategoryId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
			UnitOfMeasureId = reader.IsDBNull(12) ? null : reader.GetInt64(12),
			PackagingId = reader.IsDBNull(13) ? null : reader.GetInt64(13),
			SupplierId = reader.IsDBNull(14) ? null : reader.GetInt64(14)
		};
}
