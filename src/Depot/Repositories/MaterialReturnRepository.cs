// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class MaterialReturnRepository : DatabaseRepository
{
	private const string ReturnColumns = "mr.Id, mr.ReturnNumber, mr.ReturnDate, mr.Status, mr.RecipientOrSource, mr.OriginalMaterialIssueId, original.IssueNumber, mr.Reference, mr.Notes, mr.CreatedByUserId, mr.PostedByUserId, mr.PostedAtUtc, mr.Version";
	private const string ReturnFrom = "FROM MaterialReturns mr LEFT JOIN MaterialIssues original ON original.Id = mr.OriginalMaterialIssueId";
	private const string LineColumns = "mrl.Id, mrl.MaterialReturnId, mrl.LineNumber, mrl.InventoryId, mrl.Quantity, mrl.ReasonCodeId, mrl.Notes, mrl.Version, i.PartNumber, i.Description, w.Name, sl.Name, p.Name, rc.Name";

	public MaterialReturnRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<MaterialReturnOverviewItem>> SearchAsync(string? searchText, MaterialReturnStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string>(); var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText)) { filters.Add("(mr.ReturnNumber LIKE $Search OR mr.RecipientOrSource LIKE $Search OR mr.Reference LIKE $Search OR mr.Notes LIKE $Search OR original.IssueNumber LIKE $Search)"); parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%")); }
		if (status is not null) { filters.Add("mr.Status = $Status"); parameters.Add(Parameter("$Status", (int)status.Value)); }
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		var from = $"{ReturnFrom} INNER JOIN Users createdBy ON createdBy.Id = mr.CreatedByUserId";
		return Database.QueryPageAsync($"SELECT mr.Id, mr.ReturnNumber, mr.ReturnDate, mr.Status, mr.RecipientOrSource, original.IssueNumber, mr.Reference, mr.Notes, createdBy.DisplayName, (SELECT COUNT(*) FROM MaterialReturnLines mrl WHERE mrl.MaterialReturnId = mr.Id), mr.PostedAtUtc, mr.Version {from} {where} ORDER BY mr.ReturnDate DESC, mr.Id DESC", $"SELECT COUNT(*) {from} {where}", ReadOverview, pageNumber, pageSize, cancellationToken, parameters.ToArray());
	}

	public Task<MaterialReturnOverviewItem?> GetOverviewByIdAsync(long id, CancellationToken cancellationToken) => Database.QuerySingleOrDefaultAsync($"SELECT mr.Id, mr.ReturnNumber, mr.ReturnDate, mr.Status, mr.RecipientOrSource, original.IssueNumber, mr.Reference, mr.Notes, createdBy.DisplayName, (SELECT COUNT(*) FROM MaterialReturnLines mrl WHERE mrl.MaterialReturnId = mr.Id), mr.PostedAtUtc, mr.Version {ReturnFrom} INNER JOIN Users createdBy ON createdBy.Id = mr.CreatedByUserId WHERE mr.Id = $Id;", ReadOverview, cancellationToken, Parameter("$Id", id));

	public async Task<MaterialReturn?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var value = await Database.QuerySingleOrDefaultAsync($"SELECT {ReturnColumns} {ReturnFrom} WHERE mr.Id = $Id;", ReadReturn, cancellationToken, Parameter("$Id", id));
		if (value is not null) value.Lines = await ListLinesAsync(id, cancellationToken);
		return value;
	}

	public async Task<MaterialReturn?> GetByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		if (await transaction.Session.ExecuteScalarAsync(Database.MaterialReturnLockSql, cancellationToken, Parameter("$MaterialReturnId", id)) is null) return null;
		var value = await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {ReturnColumns} {ReturnFrom} WHERE mr.Id = $Id;", ReadReturn, cancellationToken, Parameter("$Id", id));
		if (value is not null) value.Lines = await ListLinesAsync(transaction.Session, id, cancellationToken);
		return value;
	}

	public Task<IReadOnlyList<MaterialReturnLine>> ListLinesAsync(long id, CancellationToken cancellationToken) => Database.QueryAsync(LineSelect, ReadLine, cancellationToken, Parameter("$MaterialReturnId", id));
	public Task<long> CreateAsync(DatabaseTransactionContext transaction, MaterialReturn value, CancellationToken cancellationToken) => transaction.Session.InsertAsync("INSERT INTO MaterialReturns (ReturnNumber, ReturnDate, Status, RecipientOrSource, OriginalMaterialIssueId, Reference, Notes, CreatedByUserId) VALUES ($ReturnNumber, $ReturnDate, $Status, $RecipientOrSource, $OriginalMaterialIssueId, $Reference, $Notes, $CreatedByUserId);", cancellationToken, ReturnParameters(value));
	public Task<int> UpdateReturnNumberAsync(DatabaseTransactionContext transaction, long id, string number, CancellationToken cancellationToken) => transaction.Session.ExecuteAsync("UPDATE MaterialReturns SET ReturnNumber = $ReturnNumber WHERE Id = $Id;", cancellationToken, Parameter("$ReturnNumber", number), Parameter("$Id", id));
	public async Task<bool> UpdateDraftAsync(DatabaseTransactionContext transaction, MaterialReturn value, CancellationToken cancellationToken) => await transaction.Session.ExecuteAsync("UPDATE MaterialReturns SET ReturnDate = $ReturnDate, RecipientOrSource = $RecipientOrSource, OriginalMaterialIssueId = $OriginalMaterialIssueId, Reference = $Reference, Notes = $Notes, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Draft;", cancellationToken, Parameter("$ReturnDate", Date(value.ReturnDate)), Parameter("$RecipientOrSource", value.RecipientOrSource), Parameter("$OriginalMaterialIssueId", value.OriginalMaterialIssueId), Parameter("$Reference", value.Reference), Parameter("$Notes", value.Notes), Parameter("$Id", value.Id), Parameter("$Version", value.Version), Parameter("$Draft", (int)MaterialReturnStatus.Draft)) == 1;

	public Task<int> DeleteLinesAsync(DatabaseTransactionContext transaction, long id, IReadOnlyList<long> lineIds, CancellationToken cancellationToken)
	{
		if (lineIds.Count == 0) return Task.FromResult(0);
		var parameters = lineIds.Select((lineId, index) => Parameter($"$LineId{index}", lineId)).ToArray();
		return transaction.Session.ExecuteAsync($"DELETE FROM MaterialReturnLines WHERE MaterialReturnId = $MaterialReturnId AND Id IN ({string.Join(", ", parameters.Select(parameter => parameter.Name))});", cancellationToken, [.. parameters, Parameter("$MaterialReturnId", id)]);
	}

	public Task<long> CreateLineAsync(DatabaseTransactionContext transaction, MaterialReturnLine line, CancellationToken cancellationToken) => transaction.Session.InsertAsync("INSERT INTO MaterialReturnLines (MaterialReturnId, LineNumber, InventoryId, Quantity, ReasonCodeId, Notes) VALUES ($MaterialReturnId, $LineNumber, $InventoryId, $Quantity, $ReasonCodeId, $Notes);", cancellationToken, LineParameters(line));
	public async Task<bool> UpdateLineAsync(DatabaseTransactionContext transaction, MaterialReturnLine line, CancellationToken cancellationToken) => await transaction.Session.ExecuteAsync("UPDATE MaterialReturnLines SET LineNumber = $LineNumber, InventoryId = $InventoryId, Quantity = $Quantity, ReasonCodeId = $ReasonCodeId, Notes = $Notes, Version = Version + 1 WHERE Id = $Id AND MaterialReturnId = $MaterialReturnId AND Version = $Version;", cancellationToken, [.. LineParameters(line), Parameter("$Id", line.Id), Parameter("$Version", line.Version)]) == 1;
	public async Task<bool> SetPostedAsync(DatabaseTransactionContext transaction, long id, long version, long userId, DateTime postedAtUtc, CancellationToken cancellationToken) => await transaction.Session.ExecuteAsync("UPDATE MaterialReturns SET Status = $Posted, PostedByUserId = $UserId, PostedAtUtc = $PostedAtUtc, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Draft;", cancellationToken, Parameter("$Posted", (int)MaterialReturnStatus.Posted), Parameter("$UserId", userId), Parameter("$PostedAtUtc", DateTimeValue(postedAtUtc)), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Draft", (int)MaterialReturnStatus.Draft)) == 1;
	public async Task<bool> CancelDraftAsync(DatabaseTransactionContext transaction, long id, long version, CancellationToken cancellationToken) => await transaction.Session.ExecuteAsync("UPDATE MaterialReturns SET Status = $Cancelled, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Draft;", cancellationToken, Parameter("$Cancelled", (int)MaterialReturnStatus.Cancelled), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Draft", (int)MaterialReturnStatus.Draft)) == 1;

	private string LineSelect => $"SELECT {LineColumns}, COALESCE((SELECT SUM({Database.CastToInt64("sm.Quantity")}) FROM StockMovements sm WHERE sm.InventoryId = mrl.InventoryId), 0) FROM MaterialReturnLines mrl INNER JOIN Inventories inv ON inv.Id = mrl.InventoryId INNER JOIN Items i ON i.Id = inv.ItemId INNER JOIN Purposes p ON p.Id = inv.PurposeId INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId INNER JOIN Warehouses w ON w.Id = sl.WarehouseId INNER JOIN ReasonCodes rc ON rc.Id = mrl.ReasonCodeId WHERE mrl.MaterialReturnId = $MaterialReturnId ORDER BY mrl.LineNumber;";
	private Task<IReadOnlyList<MaterialReturnLine>> ListLinesAsync(DatabaseSession session, long id, CancellationToken cancellationToken) => session.QueryAsync(LineSelect, ReadLine, cancellationToken, Parameter("$MaterialReturnId", id));
	private static DatabaseParameter[] ReturnParameters(MaterialReturn value) => [Parameter("$ReturnNumber", value.ReturnNumber), Parameter("$ReturnDate", Date(value.ReturnDate)), Parameter("$Status", (int)value.Status), Parameter("$RecipientOrSource", value.RecipientOrSource), Parameter("$OriginalMaterialIssueId", value.OriginalMaterialIssueId), Parameter("$Reference", value.Reference), Parameter("$Notes", value.Notes), Parameter("$CreatedByUserId", value.CreatedByUserId)];
	private static DatabaseParameter[] LineParameters(MaterialReturnLine line) => [Parameter("$MaterialReturnId", line.MaterialReturnId), Parameter("$LineNumber", line.LineNumber), Parameter("$InventoryId", line.InventoryId), Parameter("$Quantity", line.Quantity), Parameter("$ReasonCodeId", line.ReasonCodeId), Parameter("$Notes", line.Notes)];
	private static MaterialReturn ReadReturn(DbDataReader reader) => new() { Id = reader.GetInt64(0), ReturnNumber = reader.GetString(1), ReturnDate = ParseDate(reader.GetString(2)), Status = (MaterialReturnStatus)reader.GetInt32(3), RecipientOrSource = reader.GetString(4), OriginalMaterialIssueId = reader.IsDBNull(5) ? null : reader.GetInt64(5), OriginalMaterialIssueNumber = reader.IsDBNull(6) ? null : reader.GetString(6), Reference = reader.IsDBNull(7) ? null : reader.GetString(7), Notes = reader.IsDBNull(8) ? null : reader.GetString(8), CreatedByUserId = reader.GetInt64(9), PostedByUserId = reader.IsDBNull(10) ? null : reader.GetInt64(10), PostedAtUtc = reader.IsDBNull(11) ? null : ParseDateTime(reader.GetString(11)), Version = reader.GetInt64(12) };
	private static MaterialReturnLine ReadLine(DbDataReader reader) => new() { Id = reader.GetInt64(0), MaterialReturnId = reader.GetInt64(1), LineNumber = reader.GetInt32(2), InventoryId = reader.GetInt64(3), Quantity = reader.GetInt32(4), ReasonCodeId = reader.GetInt64(5), Notes = reader.IsDBNull(6) ? null : reader.GetString(6), Version = reader.GetInt64(7), PartNumber = reader.GetString(8), ItemDescription = reader.GetString(9), WarehouseName = reader.GetString(10), StorageLocationName = reader.GetString(11), PurposeName = reader.GetString(12), ReasonCodeName = reader.GetString(13), CurrentStock = Convert.ToInt64(reader.GetValue(14), CultureInfo.InvariantCulture) };
	private static MaterialReturnOverviewItem ReadOverview(DbDataReader reader) => new() { Id = reader.GetInt64(0), ReturnNumber = reader.GetString(1), ReturnDate = ParseDate(reader.GetString(2)), Status = (MaterialReturnStatus)reader.GetInt32(3), RecipientOrSource = reader.GetString(4), OriginalMaterialIssueNumber = reader.IsDBNull(5) ? null : reader.GetString(5), Reference = reader.IsDBNull(6) ? null : reader.GetString(6), Notes = reader.IsDBNull(7) ? null : reader.GetString(7), CreatedByUserName = reader.GetString(8), LineCount = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture), PostedAtUtc = reader.IsDBNull(10) ? null : ParseDateTime(reader.GetString(10)), Version = reader.GetInt64(11) };
	private static string Date(DateTime value) => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private static DateTime ParseDate(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
	private static string DateTimeValue(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ParseDateTime(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
