// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class AuditRepository : DatabaseRepository
{
	private const string ListColumns =
		"Id, TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action";
	private const string InsertSql =
		"""
		INSERT INTO AuditEntries
		(TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson)
		VALUES
		($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);
		""";

	public AuditRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<PageResult<AuditLogListItem>> SearchPageAsync(
		AuditLogFilter filter,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var (whereClause, parameters) = BuildFilter(filter);
		return Database.QueryPageAsync(
			$"SELECT {ListColumns} FROM AuditEntries {whereClause} ORDER BY TimestampUtc DESC, Id DESC",
			$"SELECT COUNT(*) FROM AuditEntries {whereClause};",
			ReadListItem,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<AuditLogDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {ListColumns}, BeforeJson, AfterJson FROM AuditEntries WHERE Id = $Id;",
			ReadDetails,
			cancellationToken,
			Parameter("$Id", id));

	public async Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
	{
		var entityTypes = await Database.QueryAsync(
			"SELECT DISTINCT EntityType FROM AuditEntries ORDER BY EntityType;",
			reader => reader.GetString(0), cancellationToken);
		var actions = await Database.QueryAsync(
			"SELECT DISTINCT Action FROM AuditEntries ORDER BY Action;",
			reader => reader.GetString(0), cancellationToken);
		return new AuditLogFilterOptions(entityTypes, actions);
	}

	public Task<IReadOnlyList<AuditLogDetails>> GetExportSliceAsync(
		AuditLogFilter filter,
		int offset,
		int count,
		CancellationToken cancellationToken)
	{
		var (whereClause, parameters) = BuildFilter(filter);
		return Database.QuerySliceAsync(
			$"SELECT {ListColumns}, BeforeJson, AfterJson FROM AuditEntries {whereClause} ORDER BY TimestampUtc DESC, Id DESC",
			ReadDetails,
			offset,
			count,
			cancellationToken,
			parameters);
	}

	public Task<long> CreateAsync(AuditEntry entry, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			InsertSql,
			cancellationToken,
			Parameters(entry));

	internal static Task<int> CreateAsync(
		DatabaseSession session,
		AuditEntry entry,
		CancellationToken cancellationToken) =>
		session.ExecuteAsync(InsertSql, cancellationToken, Parameters(entry));

	public Task<int> CreateAsync(
		DatabaseTransactionContext transaction,
		AuditEntry entry,
		CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(InsertSql, cancellationToken, Parameters(entry));

	public long Create(AuditEntry entry)
	{
		return Database.Insert(
			InsertSql,
			Parameters(entry));
	}

	private static DatabaseParameter[] Parameters(AuditEntry entry) =>
	[
		Parameter("$TimestampUtc", entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
		Parameter("$UserId", entry.UserId),
		Parameter("$UserEmail", entry.UserEmail),
		Parameter("$EntityType", entry.EntityType),
		Parameter("$EntityId", entry.EntityId),
		Parameter("$Action", entry.Action),
		Parameter("$BeforeJson", entry.BeforeJson),
		Parameter("$AfterJson", entry.AfterJson)
	];

	private static (string WhereClause, DatabaseParameter[] Parameters) BuildFilter(AuditLogFilter filter)
	{
		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(filter.SearchText))
		{
			predicates.Add("(UserEmail LIKE $Search OR EntityType LIKE $Search OR Action LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{filter.SearchText.Trim()}%"));
		}
		if (filter.FromUtc is not null)
		{
			predicates.Add("TimestampUtc >= $FromUtc");
			parameters.Add(Parameter("$FromUtc", filter.FromUtc.Value.ToString("O", CultureInfo.InvariantCulture)));
		}
		if (filter.ToUtcExclusive is not null)
		{
			predicates.Add("TimestampUtc < $ToUtc");
			parameters.Add(Parameter("$ToUtc", filter.ToUtcExclusive.Value.ToString("O", CultureInfo.InvariantCulture)));
		}
		AddTextFilter(predicates, parameters, "UserEmail", "$UserEmail", filter.UserEmail, true);
		AddTextFilter(predicates, parameters, "EntityType", "$EntityType", filter.EntityType, false);
		AddTextFilter(predicates, parameters, "Action", "$Action", filter.Action, false);
		if (filter.EntityId is not null)
		{
			predicates.Add("EntityId = $EntityId");
			parameters.Add(Parameter("$EntityId", filter.EntityId.Value));
		}
		return (predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}", parameters.ToArray());
	}

	private static void AddTextFilter(
		ICollection<string> predicates,
		ICollection<DatabaseParameter> parameters,
		string column,
		string parameterName,
		string? value,
		bool contains)
	{
		if (string.IsNullOrWhiteSpace(value)) return;
		predicates.Add(contains ? $"{column} LIKE {parameterName}" : $"{column} = {parameterName}");
		parameters.Add(Parameter(parameterName, contains ? $"%{value.Trim()}%" : value.Trim()));
	}

	private static AuditLogListItem ReadListItem(DbDataReader reader) =>
		new(
			reader.GetInt64(0),
			ReadDateTime(reader, 1),
			reader.IsDBNull(2) ? null : reader.GetInt64(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.GetInt64(5),
			reader.GetString(6));

	private static AuditLogDetails ReadDetails(DbDataReader reader) =>
		new(
			reader.GetInt64(0),
			ReadDateTime(reader, 1),
			reader.IsDBNull(2) ? null : reader.GetInt64(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.GetInt64(5),
			reader.GetString(6),
			reader.IsDBNull(7) ? null : reader.GetString(7),
			reader.IsDBNull(8) ? null : reader.GetString(8));

	private static DateTime ReadDateTime(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		return value is DateTime dateTime
			? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
			: DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
				CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	}
}
