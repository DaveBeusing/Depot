// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SecurityEventRepository : DatabaseRepository
{
	private const string SelectColumns = "Id, TimestampUtc, EventType, Severity, UserId, AccountIdentifier, SessionId, MachineName, Summary, Details, ReviewedUtc, ReviewedByUserId, Version";
	private const string InsertSql = "INSERT INTO SecurityEvents (TimestampUtc, EventType, Severity, UserId, AccountIdentifier, SessionId, MachineName, Summary, Details, ReviewedUtc, ReviewedByUserId, Version) VALUES ($TimestampUtc, $EventType, $Severity, $UserId, $AccountIdentifier, $SessionId, $MachineName, $Summary, $Details, NULL, NULL, 1);";

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
			ReadListItem, 0, count, cancellationToken, parameters);
	}

	public Task<SecurityCenterMetrics?> GetMetricsAsync(DateTime sinceUtc, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"SELECT COUNT(*), COALESCE(SUM(CASE WHEN EventType IN (3,4,5) THEN 1 ELSE 0 END),0), COALESCE(SUM(CASE WHEN ReviewedUtc IS NULL AND Severity >= 3 THEN 1 ELSE 0 END),0), COALESCE(SUM(CASE WHEN EventType = 4 THEN 1 ELSE 0 END),0) FROM SecurityEvents WHERE TimestampUtc >= $SinceUtc;",
			reader => new SecurityCenterMetrics(
				Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture)),
			cancellationToken,
			Parameter("$SinceUtc", Format(sinceUtc)));

	public async Task<bool> MarkReviewedAsync(long id, long expectedVersion, long reviewedByUserId, DateTime reviewedUtc, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE SecurityEvents SET ReviewedUtc = $ReviewedUtc, ReviewedByUserId = $ReviewedByUserId, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND ReviewedUtc IS NULL;",
			cancellationToken,
			Parameter("$ReviewedUtc", Format(reviewedUtc)), Parameter("$ReviewedByUserId", reviewedByUserId),
			Parameter("$Id", id), Parameter("$Version", expectedVersion)) == 1;

	private static DatabaseParameter[] Parameters(SecurityEvent securityEvent) =>
	[
		Parameter("$TimestampUtc", Format(securityEvent.TimestampUtc)),
		Parameter("$EventType", (int)securityEvent.EventType), Parameter("$Severity", (int)securityEvent.Severity),
		Parameter("$UserId", securityEvent.UserId), Parameter("$AccountIdentifier", securityEvent.AccountIdentifier),
		Parameter("$SessionId", securityEvent.SessionId?.ToString("D", CultureInfo.InvariantCulture)), Parameter("$MachineName", securityEvent.MachineName),
		Parameter("$Summary", securityEvent.Summary), Parameter("$Details", securityEvent.Details)
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
		Id = reader.GetInt64(0), TimestampUtc = ReadUtc(reader, 1), EventType = (SecurityEventType)reader.GetInt32(2),
		Severity = (SecurityEventSeverity)reader.GetInt32(3), UserId = NullableInt64(reader, 4), AccountIdentifier = NullableString(reader, 5),
		SessionId = reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)), MachineName = NullableString(reader, 7),
		Summary = reader.GetString(8), Details = NullableString(reader, 9), ReviewedUtc = ReadNullableUtc(reader, 10),
		ReviewedByUserId = NullableInt64(reader, 11), Version = reader.GetInt64(12)
	};

	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ReadUtc(DbDataReader reader, int ordinal) => DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static DateTime? ReadNullableUtc(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);
	private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
	private static long? NullableInt64(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
