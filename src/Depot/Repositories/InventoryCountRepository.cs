// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class InventoryCountRepository : DatabaseRepository
{
	private const string CountColumns =
		"Id, CountNumber, WarehouseId, Status, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, CreatedByUserId, PostedByUserId, Notes, ReversedAtUtc, ReversedByUserId, ReversalReason, Version";
	private const string LineColumns =
		"Id, InventoryCountId, InventoryId, ExpectedQuantity, CountedQuantity, CountedByUserId, CountedAtUtc, Version";

	public InventoryCountRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<PageResult<InventoryCountOverviewItem>> SearchAsync(
		string? searchText,
		InventoryCountStatus? status,
		long? warehouseId,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(ic.CountNumber LIKE $Search OR w.Name LIKE $Search OR ic.Notes LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (status is not null)
		{
			filters.Add("ic.Status = $Status");
			parameters.Add(Parameter("$Status", (int)status.Value));
		}
		if (warehouseId is not null)
		{
			filters.Add("ic.WarehouseId = $WarehouseId");
			parameters.Add(Parameter("$WarehouseId", warehouseId.Value));
		}

		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		const string from = "FROM InventoryCounts ic INNER JOIN Warehouses w ON w.Id = ic.WarehouseId INNER JOIN Users createdBy ON createdBy.Id = ic.CreatedByUserId";
		return Database.QueryPageAsync(
			$"SELECT ic.Id, ic.CountNumber, ic.WarehouseId, w.Name, ic.Status, ic.CreatedAtUtc, ic.StartedAtUtc, createdBy.DisplayName, (SELECT COUNT(*) FROM InventoryCountLines totalLines WHERE totalLines.InventoryCountId = ic.Id), (SELECT COUNT(*) FROM InventoryCountLines countedLines WHERE countedLines.InventoryCountId = ic.Id AND countedLines.CountedQuantity IS NOT NULL), (SELECT COUNT(*) FROM InventoryCountLines differenceLines WHERE differenceLines.InventoryCountId = ic.Id AND differenceLines.CountedQuantity IS NOT NULL AND differenceLines.CountedQuantity <> differenceLines.ExpectedQuantity), ic.Notes, ic.ReversedAtUtc, ic.ReversalReason, ic.Version {from} {where} ORDER BY ic.CreatedAtUtc DESC, ic.Id DESC",
			$"SELECT COUNT(*) {from} {where};",
			ReadOverview,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<InventoryCountOverviewItem?> GetOverviewByIdAsync(
		long id,
		CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"SELECT ic.Id, ic.CountNumber, ic.WarehouseId, w.Name, ic.Status, ic.CreatedAtUtc, ic.StartedAtUtc, createdBy.DisplayName, (SELECT COUNT(*) FROM InventoryCountLines totalLines WHERE totalLines.InventoryCountId = ic.Id), (SELECT COUNT(*) FROM InventoryCountLines countedLines WHERE countedLines.InventoryCountId = ic.Id AND countedLines.CountedQuantity IS NOT NULL), (SELECT COUNT(*) FROM InventoryCountLines differenceLines WHERE differenceLines.InventoryCountId = ic.Id AND differenceLines.CountedQuantity IS NOT NULL AND differenceLines.CountedQuantity <> differenceLines.ExpectedQuantity), ic.Notes, ic.ReversedAtUtc, ic.ReversalReason, ic.Version FROM InventoryCounts ic INNER JOIN Warehouses w ON w.Id = ic.WarehouseId INNER JOIN Users createdBy ON createdBy.Id = ic.CreatedByUserId WHERE ic.Id = $Id;",
			ReadOverview,
			cancellationToken,
			Parameter("$Id", id));

	public Task<PageResult<InventoryCountLineDetails>> SearchLineDetailsAsync(
		long inventoryCountId,
		string? searchText,
		bool uncountedOnly,
		bool differencesOnly,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var filters = new List<string> { "icl.InventoryCountId = $InventoryCountId" };
		var parameters = new List<DatabaseParameter> { Parameter("$InventoryCountId", inventoryCountId) };
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(i.PartNumber LIKE $Search OR i.Description LIKE $Search OR sl.Name LIKE $Search OR p.Name LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (uncountedOnly) filters.Add("icl.CountedQuantity IS NULL");
		if (differencesOnly) filters.Add("icl.CountedQuantity IS NOT NULL AND icl.CountedQuantity <> icl.ExpectedQuantity");
		var where = $"WHERE {string.Join(" AND ", filters)}";
		const string from = "FROM InventoryCountLines icl INNER JOIN Inventories inv ON inv.Id = icl.InventoryId INNER JOIN Items i ON i.Id = inv.ItemId INNER JOIN Purposes p ON p.Id = inv.PurposeId INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId LEFT JOIN Users countedBy ON countedBy.Id = icl.CountedByUserId";
		return Database.QueryPageAsync(
			$"SELECT icl.Id, icl.InventoryCountId, icl.InventoryId, i.PartNumber, i.Description, sl.Name, p.Name, icl.ExpectedQuantity, icl.CountedQuantity, icl.CountedByUserId, countedBy.DisplayName, icl.CountedAtUtc, icl.Version {from} {where} ORDER BY sl.Name, i.PartNumber, p.Name, icl.Id",
			$"SELECT COUNT(*) {from} {where};",
			ReadLineDetails,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<InventoryCountLineDetails?> GetLineDetailsByIdAsync(
		long lineId,
		CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"SELECT icl.Id, icl.InventoryCountId, icl.InventoryId, i.PartNumber, i.Description, sl.Name, p.Name, icl.ExpectedQuantity, icl.CountedQuantity, icl.CountedByUserId, countedBy.DisplayName, icl.CountedAtUtc, icl.Version FROM InventoryCountLines icl INNER JOIN Inventories inv ON inv.Id = icl.InventoryId INNER JOIN Items i ON i.Id = inv.ItemId INNER JOIN Purposes p ON p.Id = inv.PurposeId INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId LEFT JOIN Users countedBy ON countedBy.Id = icl.CountedByUserId WHERE icl.Id = $Id;",
			ReadLineDetails,
			cancellationToken,
			Parameter("$Id", lineId));

	public async Task<InventoryCount?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var count = await Database.QuerySingleOrDefaultAsync(
			$"SELECT {CountColumns} FROM InventoryCounts WHERE Id = $Id;",
			ReadCount,
			cancellationToken,
			Parameter("$Id", id));
		if (count is null) return null;
		count.Lines = await ListLinesAsync(id, cancellationToken);
		return count;
	}

	public Task<InventoryCount?> GetHeaderByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {CountColumns} FROM InventoryCounts WHERE Id = $Id;",
			ReadCount,
			cancellationToken,
			Parameter("$Id", id));

	public async Task<InventoryCount?> GetHeaderByIdForUpdateAsync(
		DatabaseTransactionContext transaction,
		long id,
		CancellationToken cancellationToken)
	{
		if (await transaction.Session.ExecuteScalarAsync(
			Database.InventoryCountLockSql,
			cancellationToken,
			Parameter("$InventoryCountId", id)) is null)
		{
			return null;
		}

		return await transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {CountColumns} FROM InventoryCounts WHERE Id = $Id;",
			ReadCount,
			cancellationToken,
			Parameter("$Id", id));
	}

	public Task<InventoryCountLine?> GetLineByIdAsync(
		DatabaseTransactionContext transaction,
		long inventoryCountId,
		long lineId,
		CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {LineColumns} FROM InventoryCountLines WHERE Id = $Id AND InventoryCountId = $InventoryCountId;",
			ReadLine,
			cancellationToken,
			Parameter("$Id", lineId),
			Parameter("$InventoryCountId", inventoryCountId));

	public async Task<bool> HasUncountedLinesAsync(
		DatabaseTransactionContext transaction,
		long inventoryCountId,
		CancellationToken cancellationToken) =>
		Convert.ToInt64(
			await transaction.Session.ExecuteScalarAsync(
				"SELECT COUNT(*) FROM InventoryCountLines WHERE InventoryCountId = $InventoryCountId AND CountedQuantity IS NULL;",
				cancellationToken,
				Parameter("$InventoryCountId", inventoryCountId)),
			CultureInfo.InvariantCulture) > 0;

	public Task<IReadOnlyList<InventoryCountLine>> ListLinesAsync(
		long inventoryCountId,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {LineColumns} FROM InventoryCountLines WHERE InventoryCountId = $InventoryCountId ORDER BY InventoryId;",
			ReadLine,
			cancellationToken,
			Parameter("$InventoryCountId", inventoryCountId));

	public Task<IReadOnlyList<InventoryCountLine>> ListLinesAsync(
		DatabaseTransactionContext transaction,
		long inventoryCountId,
		CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			$"SELECT {LineColumns} FROM InventoryCountLines WHERE InventoryCountId = $InventoryCountId ORDER BY InventoryId;",
			ReadLine,
			cancellationToken,
			Parameter("$InventoryCountId", inventoryCountId));

	public Task<long> CreateAsync(
		DatabaseTransactionContext transaction,
		InventoryCount count,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO InventoryCounts (CountNumber, WarehouseId, Status, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, CreatedByUserId, PostedByUserId, Notes) VALUES ($CountNumber, $WarehouseId, $Status, $CreatedAtUtc, $StartedAtUtc, $CompletedAtUtc, $CreatedByUserId, $PostedByUserId, $Notes);",
			cancellationToken,
			CountParameters(count));

	public Task<int> UpdateCountNumberAsync(
		DatabaseTransactionContext transaction,
		long id,
		string countNumber,
		CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE InventoryCounts SET CountNumber = $CountNumber WHERE Id = $Id;",
			cancellationToken,
			Parameter("$CountNumber", countNumber),
			Parameter("$Id", id));

	public async Task<bool> UpdateDraftAsync(
		DatabaseTransactionContext transaction,
		InventoryCount count,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE InventoryCounts SET WarehouseId = $WarehouseId, Notes = $Notes, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $DraftStatus;",
			cancellationToken,
			Parameter("$WarehouseId", count.WarehouseId),
			Parameter("$Notes", count.Notes),
			Parameter("$Id", count.Id),
			Parameter("$Version", count.Version),
			Parameter("$DraftStatus", (int)InventoryCountStatus.Draft)) == 1;

	public Task<long> CreateLineAsync(
		DatabaseTransactionContext transaction,
		InventoryCountLine line,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO InventoryCountLines (InventoryCountId, InventoryId, ExpectedQuantity, CountedQuantity, CountedByUserId, CountedAtUtc) VALUES ($InventoryCountId, $InventoryId, $ExpectedQuantity, $CountedQuantity, $CountedByUserId, $CountedAtUtc);",
			cancellationToken,
			LineParameters(line));

	public async Task<bool> StartAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		DateTime startedAtUtc,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE InventoryCounts SET Status = $CountingStatus, StartedAtUtc = $StartedAtUtc, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $DraftStatus;",
			cancellationToken,
			Parameter("$CountingStatus", (int)InventoryCountStatus.Counting),
			Parameter("$StartedAtUtc", DateTimeValue(startedAtUtc)),
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$DraftStatus", (int)InventoryCountStatus.Draft)) == 1;

	public async Task<bool> SetStatusAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		InventoryCountStatus expectedStatus,
		InventoryCountStatus status,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE InventoryCounts SET Status = $Status, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $ExpectedStatus;",
			cancellationToken,
			Parameter("$Status", (int)status),
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$ExpectedStatus", (int)expectedStatus)) == 1;

	public async Task<bool> PostAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		long postedByUserId,
		DateTime completedAtUtc,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE InventoryCounts SET Status = $PostedStatus, PostedByUserId = $PostedByUserId, CompletedAtUtc = $CompletedAtUtc, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $ReviewStatus;",
			cancellationToken,
			Parameter("$PostedStatus", (int)InventoryCountStatus.Posted),
			Parameter("$PostedByUserId", postedByUserId),
			Parameter("$CompletedAtUtc", DateTimeValue(completedAtUtc)),
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$ReviewStatus", (int)InventoryCountStatus.Review)) == 1;

	public async Task<bool> MarkReversedAsync(DatabaseTransactionContext transaction, long id, long version, DateTime reversedAtUtc, long reversedByUserId, string reversalReason, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE InventoryCounts SET ReversedAtUtc = $ReversedAtUtc, ReversedByUserId = $ReversedByUserId, ReversalReason = $ReversalReason, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $PostedStatus AND ReversedAtUtc IS NULL;",
			cancellationToken,
			Parameter("$ReversedAtUtc", DateTimeValue(reversedAtUtc)),
			Parameter("$ReversedByUserId", reversedByUserId),
			Parameter("$ReversalReason", reversalReason),
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$PostedStatus", (int)InventoryCountStatus.Posted)) == 1;

	public async Task<bool> UpdateCountedQuantityAsync(
		DatabaseTransactionContext transaction,
		InventoryCountLine line,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE InventoryCountLines SET CountedQuantity = $CountedQuantity, CountedByUserId = $CountedByUserId, CountedAtUtc = $CountedAtUtc, Version = Version + 1 WHERE Id = $Id AND InventoryCountId = $InventoryCountId AND Version = $Version;",
			cancellationToken,
			Parameter("$CountedQuantity", line.CountedQuantity),
			Parameter("$CountedByUserId", line.CountedByUserId),
			Parameter("$CountedAtUtc", line.CountedAtUtc is null ? null : DateTimeValue(line.CountedAtUtc.Value)),
			Parameter("$Id", line.Id),
			Parameter("$InventoryCountId", line.InventoryCountId),
			Parameter("$Version", line.Version)) == 1;

	private static DatabaseParameter[] CountParameters(InventoryCount count) =>
	[
		Parameter("$CountNumber", count.CountNumber),
		Parameter("$WarehouseId", count.WarehouseId),
		Parameter("$Status", (int)count.Status),
		Parameter("$CreatedAtUtc", DateTimeValue(count.CreatedAtUtc)),
		Parameter("$StartedAtUtc", count.StartedAtUtc is null ? null : DateTimeValue(count.StartedAtUtc.Value)),
		Parameter("$CompletedAtUtc", count.CompletedAtUtc is null ? null : DateTimeValue(count.CompletedAtUtc.Value)),
		Parameter("$CreatedByUserId", count.CreatedByUserId),
		Parameter("$PostedByUserId", count.PostedByUserId),
		Parameter("$Notes", count.Notes)
	];

	private static DatabaseParameter[] LineParameters(InventoryCountLine line) =>
	[
		Parameter("$InventoryCountId", line.InventoryCountId),
		Parameter("$InventoryId", line.InventoryId),
		Parameter("$ExpectedQuantity", line.ExpectedQuantity),
		Parameter("$CountedQuantity", line.CountedQuantity),
		Parameter("$CountedByUserId", line.CountedByUserId),
		Parameter("$CountedAtUtc", line.CountedAtUtc is null ? null : DateTimeValue(line.CountedAtUtc.Value))
	];

	private static InventoryCount ReadCount(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		CountNumber = reader.GetString(1),
		WarehouseId = reader.GetInt64(2),
		Status = (InventoryCountStatus)reader.GetInt32(3),
		CreatedAtUtc = ParseDateTime(reader.GetString(4)),
		StartedAtUtc = reader.IsDBNull(5) ? null : ParseDateTime(reader.GetString(5)),
		CompletedAtUtc = reader.IsDBNull(6) ? null : ParseDateTime(reader.GetString(6)),
		CreatedByUserId = reader.GetInt64(7),
		PostedByUserId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
		Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
		ReversedAtUtc = reader.IsDBNull(10) ? null : ParseDateTime(reader.GetString(10)),
		ReversedByUserId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
		ReversalReason = reader.IsDBNull(12) ? null : reader.GetString(12),
		Version = reader.GetInt64(13)
	};

	private static InventoryCountLine ReadLine(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		InventoryCountId = reader.GetInt64(1),
		InventoryId = reader.GetInt64(2),
		ExpectedQuantity = Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
		CountedQuantity = reader.IsDBNull(4) ? null : Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
		CountedByUserId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
		CountedAtUtc = reader.IsDBNull(6) ? null : ParseDateTime(reader.GetString(6)),
		Version = reader.GetInt64(7)
	};

	private static InventoryCountOverviewItem ReadOverview(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		CountNumber = reader.GetString(1),
		WarehouseId = reader.GetInt64(2),
		WarehouseName = reader.GetString(3),
		Status = (InventoryCountStatus)reader.GetInt32(4),
		CreatedAtUtc = ParseDateTime(reader.GetString(5)),
		StartedAtUtc = reader.IsDBNull(6) ? null : ParseDateTime(reader.GetString(6)),
		CreatedByUserName = reader.GetString(7),
		TotalLineCount = Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
		CountedLineCount = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
		DifferenceLineCount = Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture),
		Notes = reader.IsDBNull(11) ? null : reader.GetString(11),
		ReversedAtUtc = reader.IsDBNull(12) ? null : ParseDateTime(reader.GetString(12)),
		ReversalReason = reader.IsDBNull(13) ? null : reader.GetString(13),
		Version = reader.GetInt64(14)
	};

	private static InventoryCountLineDetails ReadLineDetails(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		InventoryCountId = reader.GetInt64(1),
		InventoryId = reader.GetInt64(2),
		PartNumber = reader.GetString(3),
		Description = reader.GetString(4),
		StorageLocationName = reader.GetString(5),
		PurposeName = reader.GetString(6),
		ExpectedQuantity = Convert.ToInt64(reader.GetValue(7), CultureInfo.InvariantCulture),
		CountedQuantity = reader.IsDBNull(8) ? null : Convert.ToInt64(reader.GetValue(8), CultureInfo.InvariantCulture),
		CountedByUserId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
		CountedByUserName = reader.IsDBNull(10) ? null : reader.GetString(10),
		CountedAtUtc = reader.IsDBNull(11) ? null : ParseDateTime(reader.GetString(11)),
		Version = reader.GetInt64(12)
	};

	private static string DateTimeValue(DateTime value) =>
		value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static DateTime ParseDateTime(string value) =>
		DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
