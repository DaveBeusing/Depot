// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class UserSessionRepository : DatabaseRepository
{
	private const string SessionColumns =
		"Id, SessionId, UserId, StartedUtc, LastSeenUtc, LastActivityUtc, EndedUtc, EndReason, ClientInstanceId, MachineName, AppVersion, Version";

	public UserSessionRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<long> CreateAsync(UserSession session, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"""
			INSERT INTO UserSessions
			(SessionId, UserId, StartedUtc, LastSeenUtc, LastActivityUtc, EndedUtc, EndReason, ClientInstanceId, MachineName, AppVersion, Version)
			VALUES
			($SessionId, $UserId, $StartedUtc, $LastSeenUtc, $LastActivityUtc, NULL, NULL, $ClientInstanceId, $MachineName, $AppVersion, 1);
			""",
			cancellationToken,
			Parameter("$SessionId", Format(session.SessionId)),
			Parameter("$UserId", session.UserId),
			Parameter("$StartedUtc", Format(session.StartedUtc)),
			Parameter("$LastSeenUtc", Format(session.LastSeenUtc)),
			Parameter("$LastActivityUtc", Format(session.LastActivityUtc)),
			Parameter("$ClientInstanceId", Format(session.ClientInstanceId)),
			Parameter("$MachineName", session.MachineName),
			Parameter("$AppVersion", session.AppVersion));

	public Task<bool> UpdateHeartbeatAsync(Guid sessionId, DateTime lastSeenUtc, CancellationToken cancellationToken) =>
		UpdateHeartbeatAsync(sessionId, lastSeenUtc, null, cancellationToken);

	public async Task<bool> UpdateHeartbeatAsync(Guid sessionId, DateTime lastSeenUtc, DateTime? lastActivityUtc, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE UserSessions SET LastSeenUtc = $LastSeenUtc, LastActivityUtc = COALESCE($LastActivityUtc, LastActivityUtc), Version = Version + 1 WHERE SessionId = $SessionId AND EndedUtc IS NULL;",
			cancellationToken,
			Parameter("$LastSeenUtc", Format(lastSeenUtc)),
			Parameter("$LastActivityUtc", Format(lastActivityUtc)),
			Parameter("$SessionId", Format(sessionId))) == 1;

	public async Task<bool> EndAsync(Guid sessionId, DateTime endedUtc, UserSessionEndReason reason, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE UserSessions SET EndedUtc = $EndedUtc, EndReason = $EndReason, Version = Version + 1 WHERE SessionId = $SessionId AND EndedUtc IS NULL;",
			cancellationToken,
			Parameter("$EndedUtc", Format(endedUtc)),
			Parameter("$EndReason", (int)reason),
			Parameter("$SessionId", Format(sessionId))) == 1;

	public Task<int> EndActiveSessionsForUserAsync(long userId, DateTime endedUtc, UserSessionEndReason reason, CancellationToken cancellationToken) =>
		Database.ExecuteAsync(
			"UPDATE UserSessions SET EndedUtc = $EndedUtc, EndReason = $EndReason, Version = Version + 1 WHERE UserId = $UserId AND EndedUtc IS NULL;",
			cancellationToken,
			Parameter("$EndedUtc", Format(endedUtc)),
			Parameter("$EndReason", (int)reason),
			Parameter("$UserId", userId));

	public static async Task<bool> EndAsync(DatabaseTransactionContext transaction, Guid sessionId, DateTime endedUtc, UserSessionEndReason reason, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE UserSessions SET EndedUtc = $EndedUtc, EndReason = $EndReason, Version = Version + 1 WHERE SessionId = $SessionId AND EndedUtc IS NULL;",
			cancellationToken,
			Parameter("$EndedUtc", Format(endedUtc)),
			Parameter("$EndReason", (int)reason),
			Parameter("$SessionId", Format(sessionId))) == 1;

	public static Task<int> EndActiveSessionsForUserAsync(DatabaseTransactionContext transaction, long userId, DateTime endedUtc, UserSessionEndReason reason, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE UserSessions SET EndedUtc = $EndedUtc, EndReason = $EndReason, Version = Version + 1 WHERE UserId = $UserId AND EndedUtc IS NULL;",
			cancellationToken,
			Parameter("$EndedUtc", Format(endedUtc)),
			Parameter("$EndReason", (int)reason),
			Parameter("$UserId", userId));

	public Task<UserSession?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SessionColumns} FROM UserSessions WHERE SessionId = $SessionId;",
			ReadSession,
			cancellationToken,
			Parameter("$SessionId", Format(sessionId)));

	public Task<IReadOnlyList<UserSession>> GetOpenSessionsForUserAsync(long userId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {SessionColumns} FROM UserSessions WHERE UserId = $UserId AND EndedUtc IS NULL ORDER BY StartedUtc DESC, Id DESC;",
			ReadSession,
			cancellationToken,
			Parameter("$UserId", userId));

	public Task<IReadOnlyList<ActiveUserSession>> GetActiveSessionsAsync(DateTime presenceCutoffUtc, string? searchText, CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = hasSearch ? " AND (u.DisplayName LIKE $Search OR u.Email LIKE $Search OR s.MachineName LIKE $Search)" : string.Empty;
		var parameters = new List<DatabaseParameter> { Parameter("$PresenceCutoff", Format(presenceCutoffUtc)) };
		if (hasSearch) parameters.Add(Parameter("$Search", $"%{search}%"));
		return Database.QueryAsync(
			$"""
			SELECT s.Id, s.SessionId, s.UserId, u.Email, u.DisplayName, s.StartedUtc, s.LastSeenUtc, s.LastActivityUtc,
			       s.ClientInstanceId, s.MachineName, s.AppVersion, s.Version
			FROM UserSessions s
			INNER JOIN Users u ON u.Id = s.UserId
			WHERE s.EndedUtc IS NULL AND s.LastSeenUtc >= $PresenceCutoff{filter}
			ORDER BY s.LastSeenUtc DESC, s.Id DESC;
			""",
			ReadActiveSession,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<IReadOnlyList<EndedUserSession>> GetRecentEndedSessionsAsync(int count, CancellationToken cancellationToken)
	{
		if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
		return Database.QuerySliceAsync(
			"""
			SELECT s.Id, s.SessionId, s.UserId, u.Email, u.DisplayName, s.StartedUtc, s.LastSeenUtc, s.EndedUtc, s.EndReason,
			       s.ClientInstanceId, s.MachineName, s.AppVersion, s.Version
			FROM UserSessions s
			INNER JOIN Users u ON u.Id = s.UserId
			WHERE s.EndedUtc IS NOT NULL
			ORDER BY s.EndedUtc DESC, s.Id DESC
			""",
			ReadEndedSession,
			0,
			count,
			cancellationToken);
	}

	public async Task<long> CountActiveSessionsAsync(DateTime presenceCutoffUtc, CancellationToken cancellationToken) =>
		Convert.ToInt64(await Database.ExecuteScalarAsync(
			"SELECT COUNT(*) FROM UserSessions WHERE EndedUtc IS NULL AND LastSeenUtc >= $PresenceCutoff;",
			cancellationToken,
			Parameter("$PresenceCutoff", Format(presenceCutoffUtc))), CultureInfo.InvariantCulture);

	public async Task<long> CountDistinctOnlineUsersAsync(DateTime presenceCutoffUtc, CancellationToken cancellationToken) =>
		Convert.ToInt64(await Database.ExecuteScalarAsync(
			"SELECT COUNT(DISTINCT UserId) FROM UserSessions WHERE EndedUtc IS NULL AND LastSeenUtc >= $PresenceCutoff;",
			cancellationToken,
			Parameter("$PresenceCutoff", Format(presenceCutoffUtc))), CultureInfo.InvariantCulture);

	public Task<UserSessionPresenceMetrics?> GetPresenceMetricsAsync(DateTime presenceCutoffUtc, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"SELECT COUNT(DISTINCT UserId), COUNT(*) FROM UserSessions WHERE EndedUtc IS NULL AND LastSeenUtc >= $PresenceCutoff;",
			reader => new UserSessionPresenceMetrics(
				Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)),
			cancellationToken,
			Parameter("$PresenceCutoff", Format(presenceCutoffUtc)));

	public async Task<UserSessionPolicy> GetPolicyAsync(CancellationToken cancellationToken)
	{
		var policy = await Database.QuerySingleOrDefaultAsync(
			"SELECT Id, IdleTimeoutMinutes, MaximumSessionAgeHours, UpdatedUtc, Version FROM UserSessionPolicy WHERE Id = 1;",
			ReadPolicy,
			cancellationToken);
		return policy ?? new UserSessionPolicy
		{
			UpdatedUtc = DateTime.UnixEpoch,
			Version = 1
		};
	}

	public async Task<bool> UpdatePolicyAsync(UserSessionPolicy policy, long expectedVersion, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE UserSessionPolicy SET IdleTimeoutMinutes = $IdleTimeoutMinutes, MaximumSessionAgeHours = $MaximumSessionAgeHours, UpdatedUtc = $UpdatedUtc, Version = Version + 1 WHERE Id = 1 AND Version = $ExpectedVersion;",
			cancellationToken,
			Parameter("$IdleTimeoutMinutes", policy.IdleTimeoutMinutes),
			Parameter("$MaximumSessionAgeHours", policy.MaximumSessionAgeHours),
			Parameter("$UpdatedUtc", Format(policy.UpdatedUtc)),
			Parameter("$ExpectedVersion", expectedVersion)) == 1;

	public async Task<bool> ExpireSessionIfPolicyExceededAsync(Guid sessionId, DateTime nowUtc, UserSessionPolicy policy, CancellationToken cancellationToken)
	{
		var idleCutoffUtc = nowUtc - policy.IdleTimeout;
		var maximumAgeCutoffUtc = nowUtc - policy.MaximumSessionAge;
		return await Database.ExecuteAsync(
			"""
			UPDATE UserSessions
			SET EndedUtc = $EndedUtc, EndReason = $EndReason, Version = Version + 1
			WHERE SessionId = $SessionId
			  AND EndedUtc IS NULL
			  AND (StartedUtc <= $MaximumAgeCutoff OR COALESCE(LastActivityUtc, LastSeenUtc, StartedUtc) <= $IdleCutoff);
			""",
			cancellationToken,
			Parameter("$EndedUtc", Format(nowUtc)),
			Parameter("$EndReason", (int)UserSessionEndReason.Expired),
			Parameter("$SessionId", Format(sessionId)),
			Parameter("$MaximumAgeCutoff", Format(maximumAgeCutoffUtc)),
			Parameter("$IdleCutoff", Format(idleCutoffUtc))) == 1;
	}

	public Task<int> ExpireSessionsByPolicyAsync(DateTime nowUtc, UserSessionPolicy policy, CancellationToken cancellationToken)
	{
		var idleCutoffUtc = nowUtc - policy.IdleTimeout;
		var maximumAgeCutoffUtc = nowUtc - policy.MaximumSessionAge;
		return Database.ExecuteAsync(
			"""
			UPDATE UserSessions
			SET EndedUtc = $EndedUtc, EndReason = $EndReason, Version = Version + 1
			WHERE EndedUtc IS NULL
			  AND (StartedUtc <= $MaximumAgeCutoff OR COALESCE(LastActivityUtc, LastSeenUtc, StartedUtc) <= $IdleCutoff);
			""",
			cancellationToken,
			Parameter("$EndedUtc", Format(nowUtc)),
			Parameter("$EndReason", (int)UserSessionEndReason.Expired),
			Parameter("$MaximumAgeCutoff", Format(maximumAgeCutoffUtc)),
			Parameter("$IdleCutoff", Format(idleCutoffUtc)));
	}

	private static UserSession ReadSession(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), SessionId = Guid.Parse(reader.GetString(1)), UserId = reader.GetInt64(2),
		StartedUtc = ReadUtc(reader, 3), LastSeenUtc = ReadUtc(reader, 4), LastActivityUtc = ReadNullableUtc(reader, 5),
		EndedUtc = ReadNullableUtc(reader, 6), EndReason = reader.IsDBNull(7) ? null : (UserSessionEndReason)reader.GetInt32(7),
		ClientInstanceId = Guid.Parse(reader.GetString(8)), MachineName = reader.IsDBNull(9) ? null : reader.GetString(9),
		AppVersion = reader.IsDBNull(10) ? null : reader.GetString(10), Version = reader.GetInt64(11)
	};

	private static ActiveUserSession ReadActiveSession(DbDataReader reader) => new(
		reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
		ReadUtc(reader, 5), ReadUtc(reader, 6), ReadNullableUtc(reader, 7), Guid.Parse(reader.GetString(8)),
		reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetInt64(11));

	private static EndedUserSession ReadEndedSession(DbDataReader reader) => new(
		reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
		ReadUtc(reader, 5), ReadUtc(reader, 6), ReadUtc(reader, 7), (UserSessionEndReason)reader.GetInt32(8),
		Guid.Parse(reader.GetString(9)), reader.IsDBNull(10) ? null : reader.GetString(10),
		reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetInt64(12));

	private static UserSessionPolicy ReadPolicy(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		IdleTimeoutMinutes = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
		MaximumSessionAgeHours = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
		UpdatedUtc = ReadUtc(reader, 3),
		Version = reader.GetInt64(4)
	};

	private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static string? Format(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ReadUtc(DbDataReader reader, int ordinal) => DateTime.Parse(
		Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty,
		CultureInfo.InvariantCulture,
		DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static DateTime? ReadNullableUtc(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);
}
