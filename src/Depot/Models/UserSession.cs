// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum UserSessionEndReason
{
	LoggedOut = 1,
	ApplicationClosed = 2,
	Revoked = 3,
	Expired = 4,
	Superseded = 5,
	AdministrativeLogout = 6
}

public sealed class UserSession
{
	public long Id { get; set; }
	public Guid SessionId { get; set; }
	public long UserId { get; set; }
	public DateTime StartedUtc { get; set; }
	public DateTime LastSeenUtc { get; set; }
	public DateTime? LastActivityUtc { get; set; }
	public DateTime? EndedUtc { get; set; }
	public UserSessionEndReason? EndReason { get; set; }
	public Guid ClientInstanceId { get; set; }
	public string? MachineName { get; set; }
	public string? AppVersion { get; set; }
	public long Version { get; set; } = 1;
}

public sealed class UserSessionPolicy
{
	public const int DefaultIdleTimeoutMinutes = 30;
	public const int DefaultMaximumSessionAgeHours = 12;
	public const int MinimumIdleTimeoutMinutes = 5;
	public const int MaximumIdleTimeoutMinutes = 480;
	public const int MinimumMaximumSessionAgeHours = 1;
	public const int MaximumMaximumSessionAgeHours = 168;

	public long Id { get; set; } = 1;
	public int IdleTimeoutMinutes { get; set; } = DefaultIdleTimeoutMinutes;
	public int MaximumSessionAgeHours { get; set; } = DefaultMaximumSessionAgeHours;
	public DateTime UpdatedUtc { get; set; }
	public long Version { get; set; } = 1;

	public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);
	public TimeSpan MaximumSessionAge => TimeSpan.FromHours(MaximumSessionAgeHours);
}

public sealed record ActiveUserSession(
	long Id,
	Guid SessionId,
	long UserId,
	string UserEmail,
	string UserDisplayName,
	DateTime StartedUtc,
	DateTime LastSeenUtc,
	DateTime? LastActivityUtc,
	Guid ClientInstanceId,
	string? MachineName,
	string? AppVersion,
	long Version);

public sealed record EndedUserSession(
	long Id,
	Guid SessionId,
	long UserId,
	string UserEmail,
	string UserDisplayName,
	DateTime StartedUtc,
	DateTime LastSeenUtc,
	DateTime EndedUtc,
	UserSessionEndReason EndReason,
	Guid ClientInstanceId,
	string? MachineName,
	string? AppVersion,
	long Version);

public sealed record UserSessionPresenceMetrics(long OnlineUsers, long ActiveSessions);
