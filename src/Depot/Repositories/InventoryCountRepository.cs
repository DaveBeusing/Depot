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
		"Id, CountNumber, WarehouseId, Status, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, CreatedByUserId, PostedByUserId, Notes, Version";
	private const string LineColumns =
		"Id, InventoryCountId, InventoryId, ExpectedQuantity, CountedQuantity, CountedByUserId, CountedAtUtc, Version";

	public InventoryCountRepository(DatabaseAccess database)
		: base(database)
	{
	}

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

	public async Task<InventoryCount?> GetByIdForUpdateAsync(
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

		var count = await transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {CountColumns} FROM InventoryCounts WHERE Id = $Id;",
			ReadCount,
			cancellationToken,
			Parameter("$Id", id));
		if (count is null) return null;
		count.Lines = await transaction.Session.QueryAsync(
			$"SELECT {LineColumns} FROM InventoryCountLines WHERE InventoryCountId = $InventoryCountId ORDER BY InventoryId;",
			ReadLine,
			cancellationToken,
			Parameter("$InventoryCountId", id));
		return count;
	}

	public Task<IReadOnlyList<InventoryCountLine>> ListLinesAsync(
		long inventoryCountId,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
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
		Version = reader.GetInt64(10)
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

	private static string DateTimeValue(DateTime value) =>
		value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static DateTime ParseDateTime(string value) =>
		DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
