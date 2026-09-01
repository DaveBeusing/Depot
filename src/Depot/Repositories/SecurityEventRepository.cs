// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SecurityEventRepository : DatabaseRepository
{
	private const string SelectColumns = "Id, TimestampUtc, EventType, Severity, UserId, AccountIdentifier, SessionId, ClientInstanceId, MachineName, Summary, Details, ReviewedUtc, ReviewedByUserId, Version";
	private const string InsertSql = "INSERT INTO SecurityEvents (TimestampUtc, EventType, Severity, UserId, AccountIdentifier, SessionId, ClientInstanceId, MachineName, Summary, Details, ReviewedUtc, ReviewedByUserId, Version) VALUES ($TimestampUtc, $EventType, $Severity, $UserId, $AccountIdentifier, $SessionId, $ClientInstanceId, $MachineName, $Summary, $Details, NULL, NULL, 1);";

	public SecurityEventRepository(DatabaseAccess database) : base(database) { }

	public Task<long> CreateAsync(SecurityEvent securityEvent, CancellationToken cancellationToken) =>
		Database.InsertAsync(InsertSql, cancellationToken, Parameters(securityEvent));

	public static Task<long> CreateAsync(DatabaseTransactionContext transaction, SecurityEvent securityEvent, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(InsertSql, cancellationToken, Parameters(securityEvent));

	public Task<IReadOnlyList<SecurityEventListItem>> GetRecentAsync(SecurityEventFilter filter, int count, CancellationToken cancellationToken)
	{
		if (count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
		var (where, parameters) = BuildFilter(filter);
		return Database.QuerySliceAsync(
			$"SELECT {SelectColumns} FROM SecurityEvents {where} ORDER BY TimestampUtc DESC, Id DESC",
			ReadListItem,
			0,
			count,
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<SecurityEventListItem>> GetRelatedAsync(SecurityEventListItem anchor, int count, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(anchor);
		if (count is < 1 or > 250) throw new ArgumentOutOfRangeException(nameof(count));

		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter> { Parameter("$Id", anchor.Id) };
		if (anchor.UserId is { } userId)
		{
			predicates.Add("UserId = $UserId");
			parameters.Add(Parameter("$UserId", userId));
		}
		if (!string.IsNullOrWhiteSpace(anchor.AccountIdentifier))
		{
			predicates.Add("AccountIdentifier = $AccountIdentifier");
			parameters.Add(Parameter("$AccountIdentifier", anchor.AccountIdentifier));
		}
		if (anchor.SessionId is { } sessionId)
		{
			predicates.Add("SessionId = $SessionId");
			parameters.Add(Parameter("$SessionId", sessionId.ToString("D", CultureInfo.InvariantCulture)));
		}
		if (anchor.ClientInstanceId is { } clientInstanceId)
		{
			predicates.Add("ClientInstanceId = $ClientInstanceId");
			parameters.Add(Parameter("$ClientInstanceId", clientInstanceId.ToString("D", CultureInfo.InvariantCulture)));
		}
		if (predicates.Count == 0) return Task.FromResult<IReadOnlyList<SecurityEventListItem>>([]);

		return Database.QuerySliceAsync(
			$"SELECT {SelectColumns} FROM SecurityEvents WHERE Id <> $Id AND ({string.Join(" OR ", predicates)}) ORDER BY TimestampUtc DESC, Id DESC",
			ReadListItem,
			0,
			count,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<SecurityCenterMetrics?> GetMetricsAsync(DateTime sinceUtc, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"""
			SELECT
				COALESCE(SUM(CASE WHEN TimestampUtc >= $SinceUtc THEN 1 ELSE 0 END),0),
				COALESCE(SUM(CASE WHEN TimestampUtc >= $SinceUtc AND EventType IN (3,4,5) THEN 1 ELSE 0 END),0),
				COALESCE(SUM(CASE WHEN ReviewedUtc IS NULL AND Severity >= 3 THEN 1 ELSE 0 END),0),
				COALESCE(SUM(CASE WHEN TimestampUtc >= $SinceUtc AND EventType = 4 THEN 1 ELSE 0 END),0),
				COALESCE(SUM(CASE WHEN ReviewedUtc IS NOT NULL AND ReviewedUtc >= $SinceUtc THEN 1 ELSE 0 END),0),
				COALESCE(SUM(CASE WHEN ReviewedUtc IS NULL THEN 1 ELSE 0 END),0)
			FROM SecurityEvents;
			""",
			reader => new SecurityCenterMetrics(
				Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture)),
			cancellationToken,
			Parameter("$SinceUtc", Format(sinceUtc)));

	public async Task<bool> MarkReviewedAsync(long id, long expectedVersion, long reviewedByUserId, DateTime reviewedUtc, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE SecurityEvents SET ReviewedUtc = $ReviewedUtc, ReviewedByUserId = $ReviewedByUserId, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND ReviewedUtc IS NULL;",
			cancellationToken,
			Parameter("$ReviewedUtc", Format(reviewedUtc)),
			Parameter("$ReviewedByUserId", reviewedByUserId),
			Parameter("$Id", id),
			Parameter("$Version", expectedVersion)) == 1;

	public Task<IReadOnlyList<long>> GetIdsBeforeAsync(DateTime cutoffUtc, int count, CancellationToken cancellationToken)
	{
		if (count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
		return Database.QuerySliceAsync(
			"SELECT Id FROM SecurityEvents WHERE TimestampUtc < $CutoffUtc ORDER BY TimestampUtc, Id",
			reader => reader.GetInt64(0),
			0,
			count,
			cancellationToken,
			Parameter("$CutoffUtc", Format(cutoffUtc)));
	}

	public static async Task<int> DeleteByIdsBeforeAsync(DatabaseTransactionContext transaction, IReadOnlyCollection<long> ids, DateTime cutoffUtc, CancellationToken cancellationToken)
	{
		var deleted = 0;
		foreach (var id in ids)
		{
			deleted += await transaction.Session.ExecuteAsync(
				"DELETE FROM SecurityEvents WHERE Id = $Id AND TimestampUtc < $CutoffUtc;",
				cancellationToken,
				Parameter("$Id", id),
				Parameter("$CutoffUtc", Format(cutoffUtc)));
		}
		return deleted;
	}

	private static DatabaseParameter[] Parameters(SecurityEvent securityEvent) =>
	[
		Parameter("$TimestampUtc", Format(securityEvent.TimestampUtc)),
		Parameter("$EventType", (int)securityEvent.EventType),
		Parameter("$Severity", (int)securityEvent.Severity),
		Parameter("$UserId", securityEvent.UserId),
		Parameter("$AccountIdentifier", securityEvent.AccountIdentifier),
		Parameter("$SessionId", securityEvent.SessionId?.ToString("D", CultureInfo.InvariantCulture)),
		Parameter("$ClientInstanceId", securityEvent.ClientInstanceId?.ToString("D", CultureInfo.InvariantCulture)),
		Parameter("$MachineName", securityEvent.MachineName),
		Parameter("$Summary", securityEvent.Summary),
		Parameter("$Details", securityEvent.Details)
	];

	private static (string Where, DatabaseParameter[] Parameters) BuildFilter(SecurityEventFilter filter)
	{
		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(filter.SearchText))
		{
			predicates.Add("(AccountIdentifier LIKE $Search OR MachineName LIKE $Search OR Summary LIKE $Search OR Details LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{filter.SearchText.Trim()}%"));
		}
		if (filter.MinimumSeverity is { } severity)
		{
			predicates.Add("Severity >= $Severity");
			parameters.Add(Parameter("$Severity", (int)severity));
		}
		if (filter.Reviewed is true) predicates.Add("ReviewedUtc IS NOT NULL");
		if (filter.Reviewed is false) predicates.Add("ReviewedUtc IS NULL");
		return (predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}", parameters.ToArray());
	}

	private static SecurityEventListItem ReadListItem(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		TimestampUtc = ReadUtc(reader, 1),
		EventType = (SecurityEventType)reader.GetInt32(2),
		Severity = (SecurityEventSeverity)reader.GetInt32(3),
		UserId = NullableInt64(reader, 4),
		AccountIdentifier = NullableString(reader, 5),
		SessionId = NullableGuid(reader, 6),
		ClientInstanceId = NullableGuid(reader, 7),
		MachineName = NullableString(reader, 8),
		Summary = reader.GetString(9),
		Details = NullableString(reader, 10),
		ReviewedUtc = ReadNullableUtc(reader, 11),
		ReviewedByUserId = NullableInt64(reader, 12),
		Version = reader.GetInt64(13)
	};

	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ReadUtc(DbDataReader reader, int ordinal) => DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static DateTime? ReadNullableUtc(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);
	private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
	private static long? NullableInt64(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
	private static Guid? NullableGuid(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
}
