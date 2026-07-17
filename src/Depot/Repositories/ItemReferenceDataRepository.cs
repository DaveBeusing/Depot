// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public abstract class ItemReferenceDataRepository<T> : DatabaseRepository
	where T : ItemReferenceData, new()
{
	private const string Columns = "Id, Name, Description, IsActive, Version";
	private readonly string _tableName;
	private readonly string? _itemForeignKey;
	private readonly string? _additionalReferenceQuery;

	protected ItemReferenceDataRepository(
		DatabaseAccess database,
		string tableName,
		string? itemForeignKey,
		string? additionalReferenceQuery = null)
		: base(database)
	{
		_tableName = tableName;
		_itemForeignKey = itemForeignKey;
		_additionalReferenceQuery = additionalReferenceQuery;
	}

	public Task<IReadOnlyList<T>> SearchAsync(string? searchText, CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var filter = string.IsNullOrWhiteSpace(search)
			? string.Empty
			: "WHERE Name LIKE $Search OR Description LIKE $Search";
		var parameters = string.IsNullOrWhiteSpace(search)
			? []
			: new[] { Parameter("$Search", $"%{search}%") };
		return Database.QueryAsync(
			$"SELECT {Columns} FROM {_tableName} {filter} ORDER BY IsActive DESC, Name;",
			Read,
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<T>> ListActiveAsync(CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {Columns} FROM {_tableName} WHERE IsActive = 1 ORDER BY Name;",
			Read,
			cancellationToken);

	public Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM {_tableName} WHERE Id = $Id;",
			Read,
			cancellationToken,
			Parameter("$Id", id));

	public Task<T?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM {_tableName} WHERE Name = $Name;",
			Read,
			cancellationToken,
			Parameter("$Name", name));

	public Task<long> CreateAsync(T value, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			$"INSERT INTO {_tableName} (Name, Description, IsActive) VALUES ($Name, $Description, $IsActive);",
			cancellationToken,
			Parameter("$Name", value.Name),
			Parameter("$Description", value.Description),
			Parameter("$IsActive", value.IsActive));

	public async Task<bool> UpdateAsync(T value, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			$"UPDATE {_tableName} SET Name = $Name, Description = $Description, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", value.Id),
			Parameter("$Name", value.Name),
			Parameter("$Description", value.Description),
			Parameter("$Version", value.Version)) == 1;

	public async Task<bool> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			$"UPDATE {_tableName} SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$IsActive", isActive)) == 1;

	public async Task<bool> IsReferencedAsync(long id, CancellationToken cancellationToken)
	{
		if (_itemForeignKey is not null)
		{
			var result = await Database.ExecuteScalarAsync(
				$"SELECT COUNT(*) FROM Items WHERE {_itemForeignKey} = $Id;",
				cancellationToken,
				Parameter("$Id", id));
			if (Convert.ToInt64(result) > 0) return true;
		}
		if (_additionalReferenceQuery is null) return false;
		var additional = await Database.ExecuteScalarAsync(_additionalReferenceQuery, cancellationToken, Parameter("$Id", id));
		return Convert.ToInt64(additional) > 0;
	}

	private static T Read(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		Name = reader.GetString(1),
		Description = reader.IsDBNull(2) ? null : reader.GetString(2),
		IsActive = reader.GetBoolean(3),
		Version = reader.GetInt64(4)
	};
}

public sealed class ManufacturerRepository(DatabaseAccess database)
	: ItemReferenceDataRepository<Manufacturer>(database, "Manufacturers", "ManufacturerId");

public sealed class CategoryRepository(DatabaseAccess database)
	: ItemReferenceDataRepository<Category>(database, "Categories", "CategoryId");

public sealed class UnitOfMeasureRepository(DatabaseAccess database)
	: ItemReferenceDataRepository<UnitOfMeasure>(database, "UnitsOfMeasure", "UnitOfMeasureId");

public sealed class PackagingRepository(DatabaseAccess database)
	: ItemReferenceDataRepository<Packaging>(database, "Packagings", "PackagingId");

public sealed class SupplierCategoryRepository(DatabaseAccess database)
	: ItemReferenceDataRepository<SupplierCategory>(database, "SupplierCategories", null, "SELECT COUNT(*) FROM Suppliers WHERE SupplierCategoryId = $Id;");
