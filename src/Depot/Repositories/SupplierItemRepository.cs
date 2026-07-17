// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SupplierItemRepository : DatabaseRepository
{
	private const string Columns = "si.Id, si.SupplierId, si.ItemId, i.PartNumber, i.Description, si.SupplierPartNumber, si.PurchasePrice, si.LeadTimeDays, si.MinimumOrderQuantity, si.IsPreferredSupplier, si.IsActive, si.Version";
	private const string From = "FROM SupplierItems si INNER JOIN Items i ON i.Id = si.ItemId";

	public SupplierItemRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<SupplierItem>> SearchBySupplierAsync(long supplierId, string? searchText, CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var filter = string.IsNullOrWhiteSpace(search)
			? string.Empty
			: "AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR si.SupplierPartNumber LIKE $Search)";
		var parameters = string.IsNullOrWhiteSpace(search)
			? new[] { Parameter("$SupplierId", supplierId) }
			: new[] { Parameter("$SupplierId", supplierId), Parameter("$Search", $"%{search}%") };
		return Database.QueryAsync($"SELECT {Columns} {From} WHERE si.SupplierId = $SupplierId {filter} ORDER BY si.IsActive DESC, si.IsPreferredSupplier DESC, i.PartNumber;", Read, cancellationToken, parameters);
	}

	public Task<SupplierItem?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE si.Id = $Id;", Read, cancellationToken, Parameter("$Id", id));

	public Task<SupplierItem?> GetByContextAsync(long supplierId, long itemId, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE si.SupplierId = $SupplierId AND si.ItemId = $ItemId;", Read, cancellationToken, Parameter("$SupplierId", supplierId), Parameter("$ItemId", itemId));

	public Task<long> SaveAsync(SupplierItem value, CancellationToken cancellationToken) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			long savedId;
			if (value.Id == 0)
			{
				savedId = await session.InsertAsync(
					"INSERT INTO SupplierItems (SupplierId, ItemId, SupplierPartNumber, PurchasePrice, LeadTimeDays, MinimumOrderQuantity, IsPreferredSupplier, IsActive) VALUES ($SupplierId, $ItemId, $SupplierPartNumber, $PurchasePrice, $LeadTimeDays, $MinimumOrderQuantity, $IsPreferredSupplier, $IsActive);",
					token, Parameters(value));
			}
			else
			{
				var affected = await session.ExecuteAsync(
					"UPDATE SupplierItems SET ItemId = $ItemId, SupplierPartNumber = $SupplierPartNumber, PurchasePrice = $PurchasePrice, LeadTimeDays = $LeadTimeDays, MinimumOrderQuantity = $MinimumOrderQuantity, IsPreferredSupplier = $IsPreferredSupplier, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
					token, [.. Parameters(value), Parameter("$Id", value.Id), Parameter("$Version", value.Version)]);
				if (affected != 1) return 0;
				savedId = value.Id;
			}
			if (value.IsPreferredSupplier)
			{
				await session.ExecuteAsync(
					"UPDATE SupplierItems SET IsPreferredSupplier = 0, Version = Version + 1 WHERE ItemId = $ItemId AND Id <> $Id AND IsPreferredSupplier = 1;",
					token, Parameter("$ItemId", value.ItemId), Parameter("$Id", savedId));
			}
			return savedId;
		}, cancellationToken);

	public async Task<bool> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE SupplierItems SET IsActive = $IsActive, IsPreferredSupplier = CASE WHEN $IsActive = 0 THEN 0 ELSE IsPreferredSupplier END, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken, Parameter("$Id", id), Parameter("$Version", version), Parameter("$IsActive", isActive)) == 1;

	public async Task<bool> HasActiveForSupplierAsync(long supplierId, CancellationToken cancellationToken) =>
		Convert.ToInt64(await Database.ExecuteScalarAsync("SELECT COUNT(*) FROM SupplierItems WHERE SupplierId = $SupplierId AND IsActive = 1;", cancellationToken, Parameter("$SupplierId", supplierId))) > 0;

	public async Task<bool> HasActiveForItemAsync(long itemId, CancellationToken cancellationToken) =>
		Convert.ToInt64(await Database.ExecuteScalarAsync("SELECT COUNT(*) FROM SupplierItems WHERE ItemId = $ItemId AND IsActive = 1;", cancellationToken, Parameter("$ItemId", itemId))) > 0;

	public bool HasActiveForItem(long itemId) =>
		Convert.ToInt64(Database.QuerySingleOrDefault(
			"SELECT COUNT(*) FROM SupplierItems WHERE ItemId = $ItemId AND IsActive = 1;",
			reader => new ScalarCount { Value = reader.GetInt64(0) },
			Parameter("$ItemId", itemId))?.Value) > 0;

	private static SupplierItem Read(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), SupplierId = reader.GetInt64(1), ItemId = reader.GetInt64(2), ItemPartNumber = reader.GetString(3),
		ItemDescription = reader.GetString(4), SupplierPartNumber = reader.GetString(5), PurchasePrice = reader.GetDecimal(6),
		LeadTimeDays = reader.GetInt32(7), MinimumOrderQuantity = reader.GetDecimal(8), IsPreferredSupplier = reader.GetBoolean(9),
		IsActive = reader.GetBoolean(10), Version = reader.GetInt64(11)
	};

	private static DatabaseParameter[] Parameters(SupplierItem value) =>
	[
		Parameter("$SupplierId", value.SupplierId), Parameter("$ItemId", value.ItemId), Parameter("$SupplierPartNumber", value.SupplierPartNumber),
		Parameter("$PurchasePrice", value.PurchasePrice), Parameter("$LeadTimeDays", value.LeadTimeDays),
		Parameter("$MinimumOrderQuantity", value.MinimumOrderQuantity), Parameter("$IsPreferredSupplier", value.IsPreferredSupplier), Parameter("$IsActive", value.IsActive)
	];

	private sealed class ScalarCount
	{
		public long Value { get; init; }
	}
}
