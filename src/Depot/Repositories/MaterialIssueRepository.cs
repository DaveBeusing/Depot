// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class MaterialIssueRepository : DatabaseRepository
{
	private const string IssueColumns = "Id, IssueNumber, IssueDate, Status, Recipient, Reference, Notes, CreatedByUserId, PostedByUserId, PostedAtUtc, ReversedByUserId, ReversedAtUtc, ReversalReason, Version";
	private const string LineColumns = "mil.Id, mil.MaterialIssueId, mil.LineNumber, mil.InventoryId, mil.Quantity, mil.ReasonCodeId, mil.Notes, mil.Version, i.PartNumber, i.Description, w.Name, sl.Name, p.Name, rc.Name";

	public MaterialIssueRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<MaterialIssueOverviewItem>> SearchAsync(string? searchText, MaterialIssueStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(mi.IssueNumber LIKE $Search OR mi.Recipient LIKE $Search OR mi.Reference LIKE $Search OR mi.Notes LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (status is not null)
		{
			filters.Add("mi.Status = $Status");
			parameters.Add(Parameter("$Status", (int)status.Value));
		}
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		const string from = "FROM MaterialIssues mi INNER JOIN Users createdBy ON createdBy.Id = mi.CreatedByUserId";
		return Database.QueryPageAsync(
			$"SELECT mi.Id, mi.IssueNumber, mi.IssueDate, mi.Status, mi.Recipient, mi.Reference, mi.Notes, createdBy.DisplayName, (SELECT COUNT(*) FROM MaterialIssueLines mil WHERE mil.MaterialIssueId = mi.Id), mi.PostedAtUtc, mi.ReversedAtUtc, mi.ReversalReason, mi.Version {from} {where} ORDER BY mi.IssueDate DESC, mi.Id DESC",
			$"SELECT COUNT(*) {from} {where}", ReadOverview, pageNumber, pageSize, cancellationToken, parameters.ToArray());
	}

	public Task<MaterialIssueOverviewItem?> GetOverviewByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"SELECT mi.Id, mi.IssueNumber, mi.IssueDate, mi.Status, mi.Recipient, mi.Reference, mi.Notes, createdBy.DisplayName, (SELECT COUNT(*) FROM MaterialIssueLines mil WHERE mil.MaterialIssueId = mi.Id), mi.PostedAtUtc, mi.ReversedAtUtc, mi.ReversalReason, mi.Version FROM MaterialIssues mi INNER JOIN Users createdBy ON createdBy.Id = mi.CreatedByUserId WHERE mi.Id = $Id;",
			ReadOverview, cancellationToken, Parameter("$Id", id));

	public async Task<MaterialIssue?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var issue = await Database.QuerySingleOrDefaultAsync($"SELECT {IssueColumns} FROM MaterialIssues WHERE Id = $Id;", ReadIssue, cancellationToken, Parameter("$Id", id));
		if (issue is not null) issue.Lines = await ListLinesAsync(id, cancellationToken);
		return issue;
	}

	public async Task<MaterialIssue?> GetByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		if (await transaction.Session.ExecuteScalarAsync(Database.MaterialIssueLockSql, cancellationToken, Parameter("$MaterialIssueId", id)) is null) return null;
		var issue = await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {IssueColumns} FROM MaterialIssues WHERE Id = $Id;", ReadIssue, cancellationToken, Parameter("$Id", id));
		if (issue is not null) issue.Lines = await ListLinesAsync(transaction.Session, id, cancellationToken);
		return issue;
	}

	public Task<IReadOnlyList<MaterialIssueLine>> ListLinesAsync(long materialIssueId, CancellationToken cancellationToken) =>
		Database.QueryAsync(LineSelect, ReadLine, cancellationToken, Parameter("$MaterialIssueId", materialIssueId));

	public Task<long> CreateAsync(DatabaseTransactionContext transaction, MaterialIssue issue, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO MaterialIssues (IssueNumber, IssueDate, Status, Recipient, Reference, Notes, CreatedByUserId) VALUES ($IssueNumber, $IssueDate, $Status, $Recipient, $Reference, $Notes, $CreatedByUserId);",
			cancellationToken, IssueParameters(issue));

	public Task<int> UpdateIssueNumberAsync(DatabaseTransactionContext transaction, long id, string issueNumber, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE MaterialIssues SET IssueNumber = $IssueNumber WHERE Id = $Id;", cancellationToken, Parameter("$IssueNumber", issueNumber), Parameter("$Id", id));

	public async Task<bool> UpdateDraftAsync(DatabaseTransactionContext transaction, MaterialIssue issue, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE MaterialIssues SET IssueDate = $IssueDate, Recipient = $Recipient, Reference = $Reference, Notes = $Notes, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Draft;",
			cancellationToken, Parameter("$IssueDate", Date(issue.IssueDate)), Parameter("$Recipient", issue.Recipient), Parameter("$Reference", issue.Reference), Parameter("$Notes", issue.Notes), Parameter("$Id", issue.Id), Parameter("$Version", issue.Version), Parameter("$Draft", (int)MaterialIssueStatus.Draft)) == 1;

	public Task<int> DeleteLinesAsync(DatabaseTransactionContext transaction, long issueId, IReadOnlyList<long> lineIds, CancellationToken cancellationToken)
	{
		if (lineIds.Count == 0) return Task.FromResult(0);
		var parameters = lineIds.Select((id, index) => Parameter($"$LineId{index}", id)).ToArray();
		return transaction.Session.ExecuteAsync($"DELETE FROM MaterialIssueLines WHERE MaterialIssueId = $MaterialIssueId AND Id IN ({string.Join(", ", parameters.Select(parameter => parameter.Name))});", cancellationToken, [.. parameters, Parameter("$MaterialIssueId", issueId)]);
	}

	public Task<long> CreateLineAsync(DatabaseTransactionContext transaction, MaterialIssueLine line, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO MaterialIssueLines (MaterialIssueId, LineNumber, InventoryId, Quantity, ReasonCodeId, Notes) VALUES ($MaterialIssueId, $LineNumber, $InventoryId, $Quantity, $ReasonCodeId, $Notes);", cancellationToken, LineParameters(line));

	public async Task<bool> UpdateLineAsync(DatabaseTransactionContext transaction, MaterialIssueLine line, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync("UPDATE MaterialIssueLines SET LineNumber = $LineNumber, InventoryId = $InventoryId, Quantity = $Quantity, ReasonCodeId = $ReasonCodeId, Notes = $Notes, Version = Version + 1 WHERE Id = $Id AND MaterialIssueId = $MaterialIssueId AND Version = $Version;", cancellationToken, [.. LineParameters(line), Parameter("$Id", line.Id), Parameter("$Version", line.Version)]) == 1;

	public async Task<bool> SetPostedAsync(DatabaseTransactionContext transaction, long id, long version, long postedByUserId, DateTime postedAtUtc, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync("UPDATE MaterialIssues SET Status = $Posted, PostedByUserId = $PostedByUserId, PostedAtUtc = $PostedAtUtc, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Draft;", cancellationToken, Parameter("$Posted", (int)MaterialIssueStatus.Posted), Parameter("$PostedByUserId", postedByUserId), Parameter("$PostedAtUtc", DateTimeValue(postedAtUtc)), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Draft", (int)MaterialIssueStatus.Draft)) == 1;

	public async Task<bool> CancelDraftAsync(DatabaseTransactionContext transaction, long id, long version, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync("UPDATE MaterialIssues SET Status = $Cancelled, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Draft;", cancellationToken, Parameter("$Cancelled", (int)MaterialIssueStatus.Cancelled), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Draft", (int)MaterialIssueStatus.Draft)) == 1;

	public async Task<bool> MarkReversedAsync(DatabaseTransactionContext transaction, long id, long version, long userId, DateTime reversedAtUtc, string reason, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync("UPDATE MaterialIssues SET Status = $Reversed, ReversedByUserId = $UserId, ReversedAtUtc = $ReversedAtUtc, ReversalReason = $Reason, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Posted;", cancellationToken, Parameter("$Reversed", (int)MaterialIssueStatus.Reversed), Parameter("$UserId", userId), Parameter("$ReversedAtUtc", DateTimeValue(reversedAtUtc)), Parameter("$Reason", reason), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Posted", (int)MaterialIssueStatus.Posted)) == 1;

	private string LineSelect => $"SELECT {LineColumns}, COALESCE((SELECT SUM({Database.CastToInt64("sm.Quantity")}) FROM StockMovements sm WHERE sm.InventoryId = mil.InventoryId), 0) FROM MaterialIssueLines mil INNER JOIN Inventories inv ON inv.Id = mil.InventoryId INNER JOIN Items i ON i.Id = inv.ItemId INNER JOIN Purposes p ON p.Id = inv.PurposeId INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId INNER JOIN Warehouses w ON w.Id = sl.WarehouseId INNER JOIN ReasonCodes rc ON rc.Id = mil.ReasonCodeId WHERE mil.MaterialIssueId = $MaterialIssueId ORDER BY mil.LineNumber;";

	private Task<IReadOnlyList<MaterialIssueLine>> ListLinesAsync(DatabaseSession session, long issueId, CancellationToken cancellationToken) => session.QueryAsync(LineSelect, ReadLine, cancellationToken, Parameter("$MaterialIssueId", issueId));
	private static DatabaseParameter[] IssueParameters(MaterialIssue issue) => [Parameter("$IssueNumber", issue.IssueNumber), Parameter("$IssueDate", Date(issue.IssueDate)), Parameter("$Status", (int)issue.Status), Parameter("$Recipient", issue.Recipient), Parameter("$Reference", issue.Reference), Parameter("$Notes", issue.Notes), Parameter("$CreatedByUserId", issue.CreatedByUserId)];
	private static DatabaseParameter[] LineParameters(MaterialIssueLine line) => [Parameter("$MaterialIssueId", line.MaterialIssueId), Parameter("$LineNumber", line.LineNumber), Parameter("$InventoryId", line.InventoryId), Parameter("$Quantity", line.Quantity), Parameter("$ReasonCodeId", line.ReasonCodeId), Parameter("$Notes", line.Notes)];
	private static MaterialIssue ReadIssue(DbDataReader reader) => new() { Id = reader.GetInt64(0), IssueNumber = reader.GetString(1), IssueDate = ParseDate(reader.GetString(2)), Status = (MaterialIssueStatus)reader.GetInt32(3), Recipient = reader.GetString(4), Reference = reader.IsDBNull(5) ? null : reader.GetString(5), Notes = reader.IsDBNull(6) ? null : reader.GetString(6), CreatedByUserId = reader.GetInt64(7), PostedByUserId = reader.IsDBNull(8) ? null : reader.GetInt64(8), PostedAtUtc = reader.IsDBNull(9) ? null : ParseDateTime(reader.GetString(9)), ReversedByUserId = reader.IsDBNull(10) ? null : reader.GetInt64(10), ReversedAtUtc = reader.IsDBNull(11) ? null : ParseDateTime(reader.GetString(11)), ReversalReason = reader.IsDBNull(12) ? null : reader.GetString(12), Version = reader.GetInt64(13) };
	private static MaterialIssueLine ReadLine(DbDataReader reader) => new() { Id = reader.GetInt64(0), MaterialIssueId = reader.GetInt64(1), LineNumber = reader.GetInt32(2), InventoryId = reader.GetInt64(3), Quantity = reader.GetInt32(4), ReasonCodeId = reader.GetInt64(5), Notes = reader.IsDBNull(6) ? null : reader.GetString(6), Version = reader.GetInt64(7), PartNumber = reader.GetString(8), ItemDescription = reader.GetString(9), WarehouseName = reader.GetString(10), StorageLocationName = reader.GetString(11), PurposeName = reader.GetString(12), ReasonCodeName = reader.GetString(13), CurrentStock = Convert.ToInt64(reader.GetValue(14), CultureInfo.InvariantCulture) };
	private static MaterialIssueOverviewItem ReadOverview(DbDataReader reader) => new() { Id = reader.GetInt64(0), IssueNumber = reader.GetString(1), IssueDate = ParseDate(reader.GetString(2)), Status = (MaterialIssueStatus)reader.GetInt32(3), Recipient = reader.GetString(4), Reference = reader.IsDBNull(5) ? null : reader.GetString(5), Notes = reader.IsDBNull(6) ? null : reader.GetString(6), CreatedByUserName = reader.GetString(7), LineCount = Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture), PostedAtUtc = reader.IsDBNull(9) ? null : ParseDateTime(reader.GetString(9)), ReversedAtUtc = reader.IsDBNull(10) ? null : ParseDateTime(reader.GetString(10)), ReversalReason = reader.IsDBNull(11) ? null : reader.GetString(11), Version = reader.GetInt64(12) };
	private static string Date(DateTime value) => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private static DateTime ParseDate(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
	private static string DateTimeValue(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ParseDateTime(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
