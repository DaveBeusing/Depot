// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ItemRepository : DatabaseRepository
{
	private const string SelectColumns =
		"i.Id, i.PartNumber, i.Description, m.Name, c.Name, u.Name, pk.Name, i.IsActive, i.Version, i.ManufacturerId, i.CategoryId, i.UnitOfMeasureId, i.PackagingId";
	private const string MasterDataSelectColumns =
		SelectColumns + ", i.Gtin, i.ItemType, i.LifecycleStatus, i.CountryOfOrigin, i.CustomsTariffNumber, i.TrackingMode, i.NetWeight, i.Length, i.Width, i.Height, i.ReplacementItemId, i.Notes";
	private const string SelectFrom =
		"FROM Items i LEFT JOIN Manufacturers m ON m.Id = i.ManufacturerId LEFT JOIN Categories c ON c.Id = i.CategoryId LEFT JOIN UnitsOfMeasure u ON u.Id = i.UnitOfMeasureId LEFT JOIN Packagings pk ON pk.Id = i.PackagingId";

	public ItemRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<long> CreateAsync(Item item, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"""
			INSERT INTO Items (PartNumber, Description, ManufacturerId, CategoryId, UnitOfMeasureId, PackagingId, IsActive)
			VALUES ($PartNumber, $Description, $ManufacturerId, $CategoryId, $UnitOfMeasureId, $PackagingId, $IsActive);
			""",
			cancellationToken,
			Parameter("$PartNumber", item.PartNumber),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId),
			Parameter("$IsActive", item.IsActive));

	public Task<long> CreateMasterDataAsync(Item item, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"""
			INSERT INTO Items
			(PartNumber, Description, ManufacturerId, CategoryId, UnitOfMeasureId, PackagingId, Gtin, ItemType, LifecycleStatus, CountryOfOrigin, CustomsTariffNumber, TrackingMode, NetWeight, Length, Width, Height, ReplacementItemId, Notes, IsActive)
			VALUES
			($PartNumber, $Description, $ManufacturerId, $CategoryId, $UnitOfMeasureId, $PackagingId, $Gtin, $ItemType, $LifecycleStatus, $CountryOfOrigin, $CustomsTariffNumber, $TrackingMode, $NetWeight, $Length, $Width, $Height, $ReplacementItemId, $Notes, $IsActive);
			""",
			cancellationToken,
			MasterDataParameters(item));

	public async Task<bool> UpdateAsync(Item item, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"""
			UPDATE Items
			SET Description = $Description, ManufacturerId = $ManufacturerId, CategoryId = $CategoryId,
			    UnitOfMeasureId = $UnitOfMeasureId, PackagingId = $PackagingId,
			    IsActive = $IsActive, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			cancellationToken,
			Parameter("$Id", item.Id),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId),
			Parameter("$IsActive", item.IsActive),
			Parameter("$Version", item.Version)) == 1;

	public async Task<bool> UpdateMasterDataAsync(Item item, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"""
			UPDATE Items
			SET Description = $Description,
			    ManufacturerId = $ManufacturerId,
			    CategoryId = $CategoryId,
			    UnitOfMeasureId = $UnitOfMeasureId,
			    PackagingId = $PackagingId,
			    Gtin = $Gtin,
			    ItemType = $ItemType,
			    LifecycleStatus = $LifecycleStatus,
			    CountryOfOrigin = $CountryOfOrigin,
			    CustomsTariffNumber = $CustomsTariffNumber,
			    TrackingMode = $TrackingMode,
			    NetWeight = $NetWeight,
			    Length = $Length,
			    Width = $Width,
			    Height = $Height,
			    ReplacementItemId = $ReplacementItemId,
			    Notes = $Notes,
			    IsActive = $IsActive,
			    Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			cancellationToken,
			MasterDataParameters(item, includeIdentity: true)) == 1;

	public async Task<bool> DeactivateAsync(long id, long version, CancellationToken cancellationToken) =>
		await SetActiveAsync(id, version, false, cancellationToken);

	public async Task<bool> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Items SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$IsActive", isActive)) == 1;

	public Task<PageResult<Item>> SearchPageAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken) =>
		SearchPageAsync(searchText, true, pageNumber, pageSize, cancellationToken);

	public Task<PageResult<Item>> SearchPageAsync(
		string? searchText,
		bool? isActive,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken) =>
		SearchPageCoreAsync(searchText, isActive, pageNumber, pageSize, false, cancellationToken);

	public Task<PageResult<Item>> SearchMasterDataPageAsync(
		string? searchText,
		bool? isActive,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken) =>
		SearchPageCoreAsync(searchText, isActive, pageNumber, pageSize, true, cancellationToken);

	private Task<PageResult<Item>> SearchPageCoreAsync(
		string? searchText,
		bool? isActive,
		int pageNumber,
		int pageSize,
		bool includeMasterData,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (isActive is not null)
		{
			predicates.Add("i.IsActive = $IsActive");
			parameters.Add(Parameter("$IsActive", isActive.Value));
		}
		if (hasSearch)
		{
			var masterSearch = includeMasterData
				? " OR i.Gtin LIKE $Search OR i.CountryOfOrigin LIKE $Search OR i.CustomsTariffNumber LIKE $Search OR i.Notes LIKE $Search"
				: string.Empty;
			predicates.Add($"(i.PartNumber LIKE $Search OR i.Description LIKE $Search OR m.Name LIKE $Search OR c.Name LIKE $Search OR u.Name LIKE $Search OR pk.Name LIKE $Search{masterSearch} OR EXISTS (SELECT 1 FROM SupplierItems si INNER JOIN Suppliers s ON s.Id = si.SupplierId WHERE si.ItemId = i.Id AND si.IsActive = 1 AND (s.Name LIKE $Search OR si.SupplierPartNumber LIKE $Search)))");
			parameters.Add(Parameter("$Search", $"%{search}%"));
		}
		var filter = predicates.Count == 0 ? "1 = 1" : string.Join(" AND ", predicates);
		var columns = includeMasterData ? MasterDataSelectColumns : SelectColumns;
		Func<DbDataReader, Item> reader = includeMasterData ? ReadMasterDataItem : ReadItem;
		return Database.QueryPageAsync(
			$"SELECT {columns} {SelectFrom} WHERE {filter} ORDER BY i.IsActive DESC, i.PartNumber, i.Id",
			$"SELECT COUNT(*) {SelectFrom} WHERE {filter};",
			reader,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<Item?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} {SelectFrom} WHERE i.Id = $Id;",
			ReadItem,
			cancellationToken,
			Parameter("$Id", id));

	public Task<Item?> GetMasterDataByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {MasterDataSelectColumns} {SelectFrom} WHERE i.Id = $Id;",
			ReadMasterDataItem,
			cancellationToken,
			Parameter("$Id", id));

	public Task<IReadOnlyList<Item>> GetByIdsAsync(
		IEnumerable<long> ids,
		CancellationToken cancellationToken)
	{
		var itemIds = ids.Distinct().OrderBy(id => id).ToArray();
		if (itemIds.Length == 0)
		{
			return Task.FromResult<IReadOnlyList<Item>>([]);
		}

		var parameters = itemIds
			.Select((id, index) => Parameter($"$ItemId{index}", id))
			.ToArray();
		var parameterList = string.Join(", ", parameters.Select(parameter => parameter.Name));
		return Database.QueryAsync(
			$"SELECT {SelectColumns} {SelectFrom} WHERE i.Id IN ({parameterList}) ORDER BY i.Id;",
			ReadItem,
			cancellationToken,
			parameters);
	}

	public Task<Item?> GetByPartNumberAsync(string partNumber, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} {SelectFrom} WHERE i.PartNumber = $PartNumber;",
			ReadItem,
			cancellationToken,
			Parameter("$PartNumber", partNumber));

	public long Create(Item item) =>
		Database.Insert(
			"""
			INSERT INTO Items (PartNumber, Description, ManufacturerId, CategoryId, UnitOfMeasureId, PackagingId, IsActive)
			VALUES ($PartNumber, $Description, $ManufacturerId, $CategoryId, $UnitOfMeasureId, $PackagingId, $IsActive);
			""",
			Parameter("$PartNumber", item.PartNumber),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId),
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
			    IsActive = $IsActive,
			    Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", item.Id),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId), Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId), Parameter("$PackagingId", item.PackagingId),
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
			  AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR m.Name LIKE $Search OR c.Name LIKE $Search OR u.Name LIKE $Search OR pk.Name LIKE $Search OR EXISTS (SELECT 1 FROM SupplierItems si INNER JOIN Suppliers s ON s.Id = si.SupplierId WHERE si.ItemId = i.Id AND si.IsActive = 1 AND (s.Name LIKE $Search OR si.SupplierPartNumber LIKE $Search)))
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

	private static DatabaseParameter[] MasterDataParameters(Item item, bool includeIdentity = false)
	{
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$PartNumber", item.PartNumber),
			Parameter("$Description", item.Description),
			Parameter("$ManufacturerId", item.ManufacturerId),
			Parameter("$CategoryId", item.CategoryId),
			Parameter("$UnitOfMeasureId", item.UnitOfMeasureId),
			Parameter("$PackagingId", item.PackagingId),
			Parameter("$Gtin", item.Gtin),
			Parameter("$ItemType", (int)item.ItemType),
			Parameter("$LifecycleStatus", (int)item.LifecycleStatus),
			Parameter("$CountryOfOrigin", item.CountryOfOrigin),
			Parameter("$CustomsTariffNumber", item.CustomsTariffNumber),
			Parameter("$TrackingMode", (int)item.TrackingMode),
			Parameter("$NetWeight", item.NetWeight),
			Parameter("$Length", item.Length),
			Parameter("$Width", item.Width),
			Parameter("$Height", item.Height),
			Parameter("$ReplacementItemId", item.ReplacementItemId),
			Parameter("$Notes", item.Notes),
			Parameter("$IsActive", item.IsActive)
		};
		if (includeIdentity)
		{
			parameters.Add(Parameter("$Id", item.Id));
			parameters.Add(Parameter("$Version", item.Version));
		}
		return parameters.ToArray();
	}

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
			IsActive = reader.GetBoolean(7),
			Version = reader.GetInt64(8),
			ManufacturerId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
			CategoryId = reader.IsDBNull(10) ? null : reader.GetInt64(10),
			UnitOfMeasureId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
			PackagingId = reader.IsDBNull(12) ? null : reader.GetInt64(12)
		};

	private static Item ReadMasterDataItem(DbDataReader reader)
	{
		var item = ReadItem(reader);
		item.Gtin = reader.IsDBNull(13) ? null : reader.GetString(13);
		item.ItemType = (ItemType)reader.GetInt32(14);
		item.LifecycleStatus = (ItemLifecycleStatus)reader.GetInt32(15);
		item.CountryOfOrigin = reader.IsDBNull(16) ? null : reader.GetString(16);
		item.CustomsTariffNumber = reader.IsDBNull(17) ? null : reader.GetString(17);
		item.TrackingMode = (ItemTrackingMode)reader.GetInt32(18);
		item.NetWeight = ReadNullableDecimal(reader, 19);
		item.Length = ReadNullableDecimal(reader, 20);
		item.Width = ReadNullableDecimal(reader, 21);
		item.Height = ReadNullableDecimal(reader, 22);
		item.ReplacementItemId = reader.IsDBNull(23) ? null : reader.GetInt64(23);
		item.Notes = reader.IsDBNull(24) ? null : reader.GetString(24);
		return item;
	}

	private static decimal? ReadNullableDecimal(DbDataReader reader, int ordinal) =>
		reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}
