// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class StockTransferRepository : DatabaseRepository
{
	private const string TransferColumns =
		"Id, TransferNumber, SourceWarehouseId, DestinationWarehouseId, TransferDate, Status, CreatedByUserId, PostedByUserId, Notes, ReversedAtUtc, ReversedByUserId, ReversalReason, Version";
	private const string LineColumns =
		"Id, StockTransferId, LineNumber, SourceInventoryId, DestinationInventoryId, Quantity, Version";

	public StockTransferRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<PageResult<StockTransferOverviewItem>> SearchAsync(
		string? searchText,
		StockTransferStatus? status,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(st.TransferNumber LIKE $Search OR sw.Name LIKE $Search OR dw.Name LIKE $Search OR st.Notes LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (status is not null)
		{
			filters.Add("st.Status = $Status");
			parameters.Add(Parameter("$Status", (int)status.Value));
		}

		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		const string from = "FROM StockTransfers st INNER JOIN Warehouses sw ON sw.Id = st.SourceWarehouseId INNER JOIN Warehouses dw ON dw.Id = st.DestinationWarehouseId INNER JOIN Users createdBy ON createdBy.Id = st.CreatedByUserId";
		return Database.QueryPageAsync(
			$"SELECT st.Id, st.TransferNumber, st.SourceWarehouseId, sw.Name, st.DestinationWarehouseId, dw.Name, st.TransferDate, st.Status, createdBy.DisplayName, (SELECT COUNT(*) FROM StockTransferLines lineCount WHERE lineCount.StockTransferId = st.Id), st.Notes, st.ReversedAtUtc, st.ReversalReason, st.Version {from} {where} ORDER BY st.TransferDate DESC, st.Id DESC",
			$"SELECT COUNT(*) {from} {where};",
			ReadOverview,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<StockTransferOverviewItem?> GetOverviewByIdAsync(
		long id,
		CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"SELECT st.Id, st.TransferNumber, st.SourceWarehouseId, sw.Name, st.DestinationWarehouseId, dw.Name, st.TransferDate, st.Status, createdBy.DisplayName, (SELECT COUNT(*) FROM StockTransferLines lineCount WHERE lineCount.StockTransferId = st.Id), st.Notes, st.ReversedAtUtc, st.ReversalReason, st.Version FROM StockTransfers st INNER JOIN Warehouses sw ON sw.Id = st.SourceWarehouseId INNER JOIN Warehouses dw ON dw.Id = st.DestinationWarehouseId INNER JOIN Users createdBy ON createdBy.Id = st.CreatedByUserId WHERE st.Id = $Id;",
			ReadOverview,
			cancellationToken,
			Parameter("$Id", id));

	public async Task<StockTransfer?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var transfer = await Database.QuerySingleOrDefaultAsync(
			$"SELECT {TransferColumns} FROM StockTransfers WHERE Id = $Id;",
			ReadTransfer,
			cancellationToken,
			Parameter("$Id", id));
		if (transfer is null)
		{
			return null;
		}

		transfer.Lines = await ListLinesAsync(id, cancellationToken);
		return transfer;
	}

	public async Task<StockTransfer?> GetByIdAsync(
		DatabaseTransactionContext transaction,
		long id,
		CancellationToken cancellationToken)
	{
		if (await transaction.Session.ExecuteScalarAsync(
			Database.StockTransferLockSql,
			cancellationToken,
			Parameter("$StockTransferId", id)) is null)
		{
			return null;
		}

		var transfer = await transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {TransferColumns} FROM StockTransfers WHERE Id = $Id;",
			ReadTransfer,
			cancellationToken,
			Parameter("$Id", id));
		if (transfer is null)
		{
			return null;
		}

		transfer.Lines = await transaction.Session.QueryAsync(
			$"SELECT {LineColumns} FROM StockTransferLines WHERE StockTransferId = $StockTransferId ORDER BY LineNumber;",
			ReadLine,
			cancellationToken,
			Parameter("$StockTransferId", id));
		return transfer;
	}

	public Task<IReadOnlyList<StockTransferLine>> ListLinesAsync(
		long stockTransferId,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {LineColumns} FROM StockTransferLines WHERE StockTransferId = $StockTransferId ORDER BY LineNumber;",
			ReadLine,
			cancellationToken,
			Parameter("$StockTransferId", stockTransferId));

	public Task<long> CreateAsync(
		DatabaseTransactionContext transaction,
		StockTransfer transfer,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO StockTransfers (TransferNumber, SourceWarehouseId, DestinationWarehouseId, TransferDate, Status, CreatedByUserId, PostedByUserId, Notes) VALUES ($TransferNumber, $SourceWarehouseId, $DestinationWarehouseId, $TransferDate, $Status, $CreatedByUserId, $PostedByUserId, $Notes);",
			cancellationToken,
			TransferParameters(transfer));

	public Task<int> UpdateTransferNumberAsync(
		DatabaseTransactionContext transaction,
		long id,
		string transferNumber,
		CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE StockTransfers SET TransferNumber = $TransferNumber WHERE Id = $Id;",
			cancellationToken,
			Parameter("$TransferNumber", transferNumber),
			Parameter("$Id", id));

	public async Task<bool> UpdateDraftAsync(
		DatabaseTransactionContext transaction,
		StockTransfer transfer,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE StockTransfers SET SourceWarehouseId = $SourceWarehouseId, DestinationWarehouseId = $DestinationWarehouseId, TransferDate = $TransferDate, Notes = $Notes, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $DraftStatus;",
			cancellationToken,
			Parameter("$SourceWarehouseId", transfer.SourceWarehouseId),
			Parameter("$DestinationWarehouseId", transfer.DestinationWarehouseId),
			Parameter("$TransferDate", Date(transfer.TransferDate)),
			Parameter("$Notes", transfer.Notes),
			Parameter("$Id", transfer.Id),
			Parameter("$Version", transfer.Version),
			Parameter("$DraftStatus", (int)StockTransferStatus.Draft)) == 1;

	public Task<IReadOnlyList<long>> ListLineIdsAsync(
		DatabaseTransactionContext transaction,
		long stockTransferId,
		CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			"SELECT Id FROM StockTransferLines WHERE StockTransferId = $StockTransferId ORDER BY Id;",
			reader => reader.GetInt64(0),
			cancellationToken,
			Parameter("$StockTransferId", stockTransferId));

	public Task<int> DeleteLinesAsync(
		DatabaseTransactionContext transaction,
		long stockTransferId,
		IReadOnlyList<long> lineIds,
		CancellationToken cancellationToken)
	{
		if (lineIds.Count == 0)
		{
			return Task.FromResult(0);
		}

		var parameters = lineIds
			.Select((id, index) => Parameter($"$LineId{index}", id))
			.ToArray();
		var parameterList = string.Join(", ", parameters.Select(parameter => parameter.Name));
		return transaction.Session.ExecuteAsync(
			$"DELETE FROM StockTransferLines WHERE StockTransferId = $StockTransferId AND Id IN ({parameterList});",
			cancellationToken,
			[.. parameters, Parameter("$StockTransferId", stockTransferId)]);
	}

	public Task<long> CreateLineAsync(
		DatabaseTransactionContext transaction,
		StockTransferLine line,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO StockTransferLines (StockTransferId, LineNumber, SourceInventoryId, DestinationInventoryId, Quantity) VALUES ($StockTransferId, $LineNumber, $SourceInventoryId, $DestinationInventoryId, $Quantity);",
			cancellationToken,
			LineParameters(line));

	public async Task<bool> UpdateLineAsync(
		DatabaseTransactionContext transaction,
		StockTransferLine line,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE StockTransferLines SET LineNumber = $LineNumber, SourceInventoryId = $SourceInventoryId, DestinationInventoryId = $DestinationInventoryId, Quantity = $Quantity, Version = Version + 1 WHERE Id = $Id AND StockTransferId = $StockTransferId AND Version = $Version;",
			cancellationToken,
			[.. LineParameters(line), Parameter("$Id", line.Id), Parameter("$Version", line.Version)]) == 1;

	public async Task<bool> SetStatusAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		StockTransferStatus expectedStatus,
		StockTransferStatus status,
		long? postedByUserId,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE StockTransfers SET Status = $Status, PostedByUserId = $PostedByUserId, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $ExpectedStatus;",
			cancellationToken,
			Parameter("$Status", (int)status),
			Parameter("$PostedByUserId", postedByUserId),
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$ExpectedStatus", (int)expectedStatus)) == 1;

	public async Task<bool> MarkReversedAsync(DatabaseTransactionContext transaction, long id, long version, DateTime reversedAtUtc, long reversedByUserId, string reversalReason, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE StockTransfers SET ReversedAtUtc = $ReversedAtUtc, ReversedByUserId = $ReversedByUserId, ReversalReason = $ReversalReason, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $PostedStatus AND ReversedAtUtc IS NULL;",
			cancellationToken,
			Parameter("$ReversedAtUtc", DateTimeValue(reversedAtUtc)),
			Parameter("$ReversedByUserId", reversedByUserId),
			Parameter("$ReversalReason", reversalReason),
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$PostedStatus", (int)StockTransferStatus.Posted)) == 1;

	private static DatabaseParameter[] TransferParameters(StockTransfer transfer) =>
	[
		Parameter("$TransferNumber", transfer.TransferNumber),
		Parameter("$SourceWarehouseId", transfer.SourceWarehouseId),
		Parameter("$DestinationWarehouseId", transfer.DestinationWarehouseId),
		Parameter("$TransferDate", Date(transfer.TransferDate)),
		Parameter("$Status", (int)transfer.Status),
		Parameter("$CreatedByUserId", transfer.CreatedByUserId),
		Parameter("$PostedByUserId", transfer.PostedByUserId),
		Parameter("$Notes", transfer.Notes)
	];

	private static DatabaseParameter[] LineParameters(StockTransferLine line) =>
	[
		Parameter("$StockTransferId", line.StockTransferId),
		Parameter("$LineNumber", line.LineNumber),
		Parameter("$SourceInventoryId", line.SourceInventoryId),
		Parameter("$DestinationInventoryId", line.DestinationInventoryId),
		Parameter("$Quantity", line.Quantity)
	];

	private static StockTransfer ReadTransfer(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		TransferNumber = reader.GetString(1),
		SourceWarehouseId = reader.GetInt64(2),
		DestinationWarehouseId = reader.GetInt64(3),
		TransferDate = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
		Status = (StockTransferStatus)reader.GetInt32(5),
		CreatedByUserId = reader.GetInt64(6),
		PostedByUserId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
		Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
		ReversedAtUtc = reader.IsDBNull(9) ? null : ParseDateTime(reader.GetString(9)),
		ReversedByUserId = reader.IsDBNull(10) ? null : reader.GetInt64(10),
		ReversalReason = reader.IsDBNull(11) ? null : reader.GetString(11),
		Version = reader.GetInt64(12)
	};

	private static StockTransferLine ReadLine(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		StockTransferId = reader.GetInt64(1),
		LineNumber = reader.GetInt32(2),
		SourceInventoryId = reader.GetInt64(3),
		DestinationInventoryId = reader.GetInt64(4),
		Quantity = reader.GetInt32(5),
		Version = reader.GetInt64(6)
	};

	private static StockTransferOverviewItem ReadOverview(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		TransferNumber = reader.GetString(1),
		SourceWarehouseId = reader.GetInt64(2),
		SourceWarehouseName = reader.GetString(3),
		DestinationWarehouseId = reader.GetInt64(4),
		DestinationWarehouseName = reader.GetString(5),
		TransferDate = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
		Status = (StockTransferStatus)reader.GetInt32(7),
		CreatedByUserName = reader.GetString(8),
		LineCount = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
		Notes = reader.IsDBNull(10) ? null : reader.GetString(10),
		ReversedAtUtc = reader.IsDBNull(11) ? null : ParseDateTime(reader.GetString(11)),
		ReversalReason = reader.IsDBNull(12) ? null : reader.GetString(12),
		Version = reader.GetInt64(13)
	};

	private static string Date(DateTime value) =>
		value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private static string DateTimeValue(DateTime value) =>
		value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static DateTime ParseDateTime(string value) =>
		DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
