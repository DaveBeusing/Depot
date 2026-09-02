// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public sealed class UserSessionPresenceOptions
{
	public static UserSessionPresenceOptions Default { get; } = new();

	public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);
	public TimeSpan PresenceTimeout { get; init; } = TimeSpan.FromSeconds(90);
	public TimeSpan AdministrationRefreshInterval { get; init; } = TimeSpan.FromSeconds(20);
	public TimeSpan ShutdownWriteTimeout { get; init; } = TimeSpan.FromSeconds(3);
}
