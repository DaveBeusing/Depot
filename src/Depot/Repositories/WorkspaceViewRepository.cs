// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class WorkspaceViewRepository : DatabaseRepository
{
	private const string SelectColumns = "v.Id, v.UserId, v.WorkspaceId, v.ViewId, v.Name, v.DefinitionJson, CASE WHEN d.ViewId = v.ViewId THEN 1 ELSE 0 END, v.UpdatedUtc, v.Version";

	public WorkspaceViewRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<IReadOnlyList<UserWorkspaceViewRecord>> ListAsync(long userId, string workspaceId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {SelectColumns} FROM UserWorkspaceViews v LEFT JOIN UserWorkspaceDefaultViews d ON d.UserId = v.UserId AND d.WorkspaceId = v.WorkspaceId WHERE v.UserId = $UserId AND v.WorkspaceId = $WorkspaceId ORDER BY v.Name, v.Id;",
			Read,
			cancellationToken,
			Parameter("$UserId", userId),
			Parameter("$WorkspaceId", workspaceId));

	public Task<UserWorkspaceViewRecord?> GetAsync(long userId, string workspaceId, Guid viewId, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM UserWorkspaceViews v LEFT JOIN UserWorkspaceDefaultViews d ON d.UserId = v.UserId AND d.WorkspaceId = v.WorkspaceId WHERE v.UserId = $UserId AND v.WorkspaceId = $WorkspaceId AND v.ViewId = $ViewId;",
			Read,
			cancellationToken,
			Parameter("$UserId", userId),
			Parameter("$WorkspaceId", workspaceId),
			Parameter("$ViewId", viewId.ToString("D", CultureInfo.InvariantCulture)));

	public Task<long> InsertAsync(DatabaseTransactionContext transaction, long userId, string workspaceId, Guid viewId, string name, string definitionJson, DateTime updatedUtc, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO UserWorkspaceViews (UserId, WorkspaceId, ViewId, Name, DefinitionJson, UpdatedUtc, Version) VALUES ($UserId, $WorkspaceId, $ViewId, $Name, $DefinitionJson, $UpdatedUtc, 1);",
			cancellationToken,
			Parameter("$UserId", userId), Parameter("$WorkspaceId", workspaceId),
			Parameter("$ViewId", viewId.ToString("D", CultureInfo.InvariantCulture)), Parameter("$Name", name),
			Parameter("$DefinitionJson", definitionJson), Parameter("$UpdatedUtc", Format(updatedUtc)));

	public Task<int> UpdateAsync(DatabaseTransactionContext transaction, long userId, string workspaceId, Guid viewId, string name, string definitionJson, long expectedVersion, DateTime updatedUtc, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE UserWorkspaceViews SET Name = $Name, DefinitionJson = $DefinitionJson, UpdatedUtc = $UpdatedUtc, Version = Version + 1 WHERE UserId = $UserId AND WorkspaceId = $WorkspaceId AND ViewId = $ViewId AND Version = $ExpectedVersion;",
			cancellationToken,
			Parameter("$Name", name), Parameter("$DefinitionJson", definitionJson), Parameter("$UpdatedUtc", Format(updatedUtc)),
			Parameter("$UserId", userId), Parameter("$WorkspaceId", workspaceId),
			Parameter("$ViewId", viewId.ToString("D", CultureInfo.InvariantCulture)), Parameter("$ExpectedVersion", expectedVersion));

	public Task<int> DeleteAsync(DatabaseTransactionContext transaction, long userId, string workspaceId, Guid viewId, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"DELETE FROM UserWorkspaceViews WHERE UserId = $UserId AND WorkspaceId = $WorkspaceId AND ViewId = $ViewId AND Version = $ExpectedVersion;",
			cancellationToken,
			Parameter("$UserId", userId), Parameter("$WorkspaceId", workspaceId),
			Parameter("$ViewId", viewId.ToString("D", CultureInfo.InvariantCulture)), Parameter("$ExpectedVersion", expectedVersion));

	public async Task SetDefaultAsync(DatabaseTransactionContext transaction, long userId, string workspaceId, Guid viewId, DateTime updatedUtc, CancellationToken cancellationToken)
	{
		var parameters = new[]
		{
			Parameter("$UserId", userId), Parameter("$WorkspaceId", workspaceId),
			Parameter("$ViewId", viewId.ToString("D", CultureInfo.InvariantCulture)), Parameter("$UpdatedUtc", Format(updatedUtc))
		};
		var updated = await transaction.Session.ExecuteAsync(
			"UPDATE UserWorkspaceDefaultViews SET ViewId = $ViewId, UpdatedUtc = $UpdatedUtc, Version = Version + 1 WHERE UserId = $UserId AND WorkspaceId = $WorkspaceId;",
			cancellationToken,
			parameters);
		if (updated == 0)
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO UserWorkspaceDefaultViews (UserId, WorkspaceId, ViewId, UpdatedUtc, Version) VALUES ($UserId, $WorkspaceId, $ViewId, $UpdatedUtc, 1);",
				cancellationToken,
				parameters);
		}
	}

	public Task<int> ClearDefaultAsync(DatabaseTransactionContext transaction, long userId, string workspaceId, Guid? viewId, CancellationToken cancellationToken)
	{
		var sql = viewId is null
			? "DELETE FROM UserWorkspaceDefaultViews WHERE UserId = $UserId AND WorkspaceId = $WorkspaceId;"
			: "DELETE FROM UserWorkspaceDefaultViews WHERE UserId = $UserId AND WorkspaceId = $WorkspaceId AND ViewId = $ViewId;";
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$UserId", userId), Parameter("$WorkspaceId", workspaceId)
		};
		if (viewId is not null) parameters.Add(Parameter("$ViewId", viewId.Value.ToString("D", CultureInfo.InvariantCulture)));
		return transaction.Session.ExecuteAsync(sql, cancellationToken, parameters.ToArray());
	}

	private static UserWorkspaceViewRecord Read(DbDataReader reader) => new(
		reader.GetInt64(0),
		reader.GetInt64(1),
		reader.GetString(2),
		Guid.Parse(reader.GetString(3)),
		reader.GetString(4),
		reader.GetString(5),
		Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture) != 0,
		DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
		reader.GetInt64(8));

	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
