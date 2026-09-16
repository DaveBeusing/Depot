// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;

namespace Depot.Repositories;

public sealed class WorkspaceProductivityRepository : DatabaseRepository
{
	public WorkspaceProductivityRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<IReadOnlyList<string>> ListFavoritesAsync(long userId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"SELECT RouteId FROM UserWorkspaceFavorites WHERE UserId = $UserId ORDER BY PinnedUtc DESC, RouteId;",
			reader => reader.GetString(0),
			cancellationToken,
			Parameter("$UserId", userId));

	public Task<IReadOnlyList<string>> ListRecentsAsync(long userId, int maximum, CancellationToken cancellationToken) =>
		Database.QuerySliceAsync(
			"SELECT RouteId FROM UserWorkspaceRecents WHERE UserId = $UserId ORDER BY LastUsedUtc DESC, RouteId",
			reader => reader.GetString(0),
			0,
			maximum,
			cancellationToken,
			Parameter("$UserId", userId));

	public Task<UserWorkspacePreferenceRecord?> GetPreferenceAsync(long userId, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"SELECT UserId, DefaultLandingRouteId, UpdatedUtc, Version FROM UserWorkspacePreferences WHERE UserId = $UserId;",
			ReadPreference,
			cancellationToken,
			Parameter("$UserId", userId));

	public async Task PinAsync(DatabaseTransactionContext transaction, long userId, string routeId, DateTime utcNow, CancellationToken cancellationToken)
	{
		var updated = await transaction.Session.ExecuteAsync(
			"UPDATE UserWorkspaceFavorites SET PinnedUtc = $PinnedUtc WHERE UserId = $UserId AND RouteId = $RouteId;",
			cancellationToken,
			Parameter("$PinnedUtc", Format(utcNow)), Parameter("$UserId", userId), Parameter("$RouteId", routeId));
		if (updated == 0)
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO UserWorkspaceFavorites (UserId, RouteId, PinnedUtc) VALUES ($UserId, $RouteId, $PinnedUtc);",
				cancellationToken,
				Parameter("$UserId", userId), Parameter("$RouteId", routeId), Parameter("$PinnedUtc", Format(utcNow)));
		}
	}

	public Task<int> UnpinAsync(DatabaseTransactionContext transaction, long userId, string routeId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"DELETE FROM UserWorkspaceFavorites WHERE UserId = $UserId AND RouteId = $RouteId;",
			cancellationToken,
			Parameter("$UserId", userId), Parameter("$RouteId", routeId));

	public async Task RecordRecentAsync(DatabaseTransactionContext transaction, long userId, string routeId, DateTime utcNow, int maximum, CancellationToken cancellationToken)
	{
		var updated = await transaction.Session.ExecuteAsync(
			"UPDATE UserWorkspaceRecents SET LastUsedUtc = $LastUsedUtc, VisitCount = VisitCount + 1 WHERE UserId = $UserId AND RouteId = $RouteId;",
			cancellationToken,
			Parameter("$LastUsedUtc", Format(utcNow)), Parameter("$UserId", userId), Parameter("$RouteId", routeId));
		if (updated == 0)
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO UserWorkspaceRecents (UserId, RouteId, LastUsedUtc, VisitCount) VALUES ($UserId, $RouteId, $LastUsedUtc, 1);",
				cancellationToken,
				Parameter("$UserId", userId), Parameter("$RouteId", routeId), Parameter("$LastUsedUtc", Format(utcNow)));
		}

		var routes = await transaction.Session.QueryAsync(
			"SELECT RouteId FROM UserWorkspaceRecents WHERE UserId = $UserId ORDER BY LastUsedUtc DESC, RouteId;",
			reader => reader.GetString(0),
			cancellationToken,
			Parameter("$UserId", userId));
		foreach (var staleRoute in routes.Skip(maximum))
		{
			await transaction.Session.ExecuteAsync(
				"DELETE FROM UserWorkspaceRecents WHERE UserId = $UserId AND RouteId = $RouteId;",
				cancellationToken,
				Parameter("$UserId", userId), Parameter("$RouteId", staleRoute));
		}
	}

	public async Task SetDefaultLandingAsync(DatabaseTransactionContext transaction, long userId, string? routeId, DateTime utcNow, CancellationToken cancellationToken)
	{
		if (routeId is null)
		{
			await transaction.Session.ExecuteAsync(
				"DELETE FROM UserWorkspacePreferences WHERE UserId = $UserId;",
				cancellationToken,
				Parameter("$UserId", userId));
			return;
		}

		var updated = await transaction.Session.ExecuteAsync(
			"UPDATE UserWorkspacePreferences SET DefaultLandingRouteId = $RouteId, UpdatedUtc = $UpdatedUtc, Version = Version + 1 WHERE UserId = $UserId;",
			cancellationToken,
			Parameter("$RouteId", routeId), Parameter("$UpdatedUtc", Format(utcNow)), Parameter("$UserId", userId));
		if (updated == 0)
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO UserWorkspacePreferences (UserId, DefaultLandingRouteId, UpdatedUtc, Version) VALUES ($UserId, $RouteId, $UpdatedUtc, 1);",
				cancellationToken,
				Parameter("$UserId", userId), Parameter("$RouteId", routeId), Parameter("$UpdatedUtc", Format(utcNow)));
		}
	}

	private static UserWorkspacePreferenceRecord ReadPreference(DbDataReader reader) => new(
		reader.GetInt64(0),
		reader.IsDBNull(1) ? null : reader.GetString(1),
		DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
		reader.GetInt64(3));

	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

public sealed record UserWorkspacePreferenceRecord(long UserId, string? DefaultLandingRouteId, DateTime UpdatedUtc, long Version);
