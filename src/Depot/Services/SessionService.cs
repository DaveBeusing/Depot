// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Diagnostics;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed record UserSessionClientInfo(Guid ClientInstanceId, string? MachineName, string? AppVersion);

/// <summary>
/// Manages the current authenticated Depot session and its presence heartbeat.
/// </summary>
public sealed class SessionService : IDisposable
{
	private readonly AuthorizationService _authorizationService;
	private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
	private readonly object _stateGate = new();
	private UserSessionRepository? _repository;
	private UserSessionClientInfo? _clientInfo;
	private TimeProvider _timeProvider = TimeProvider.System;
	private UserSessionPresenceOptions _options = UserSessionPresenceOptions.Default;
	private CancellationTokenSource? _heartbeatCancellation;
	private Task? _heartbeatTask;
	private Guid? _currentSessionId;
	private bool _disposed;

	public SessionService(AuthorizationService authorizationService)
	{
		_authorizationService = authorizationService;
	}

	public bool LogoutRequestedByUser { get; private set; }
	public Guid? CurrentSessionId
	{
		get { lock (_stateGate) return _currentSessionId; }
	}

	internal void Configure(
		UserSessionRepository repository,
		UserSessionClientInfo clientInfo,
		TimeProvider? timeProvider = null,
		UserSessionPresenceOptions? options = null)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(repository);
		ArgumentNullException.ThrowIfNull(clientInfo);
		lock (_stateGate)
		{
			if (_repository is not null) throw new InvalidOperationException("User session persistence is already configured.");
			_repository = repository;
			_clientInfo = clientInfo;
			_timeProvider = timeProvider ?? TimeProvider.System;
			_options = options ?? UserSessionPresenceOptions.Default;
		}
	}

	public async Task StartAuthenticatedSessionAsync(long userId, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		UserSessionRepository? repository;
		UserSessionClientInfo? clientInfo;
		lock (_stateGate)
		{
			repository = _repository;
			clientInfo = _clientInfo;
		}
		if (repository is null || clientInfo is null) return;

		await _lifecycleGate.WaitAsync(cancellationToken);
		try
		{
			lock (_stateGate)
			{
				if (_currentSessionId is not null) throw new InvalidOperationException("An authenticated user session is already active.");
			}

			var now = _timeProvider.GetUtcNow().UtcDateTime;
			var session = new UserSession
			{
				SessionId = Guid.NewGuid(),
				UserId = userId,
				StartedUtc = now,
				LastSeenUtc = now,
				ClientInstanceId = clientInfo.ClientInstanceId,
				MachineName = clientInfo.MachineName,
				AppVersion = clientInfo.AppVersion
			};
			await repository.CreateAsync(session, cancellationToken);

			var heartbeatCancellation = new CancellationTokenSource();
			lock (_stateGate)
			{
				_currentSessionId = session.SessionId;
				_heartbeatCancellation = heartbeatCancellation;
				_heartbeatTask = RunHeartbeatAsync(repository, session.SessionId, heartbeatCancellation.Token);
			}
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async Task<bool> TrySendHeartbeatAsync(CancellationToken cancellationToken = default)
	{
		UserSessionRepository? repository;
		Guid? sessionId;
		lock (_stateGate)
		{
			repository = _repository;
			sessionId = _currentSessionId;
		}
		if (repository is null || sessionId is null) return false;
		try
		{
			return await repository.UpdateHeartbeatAsync(sessionId.Value, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.LogException(exception);
			return false;
		}
	}

	public async Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		LogoutRequestedByUser = true;
		try
		{
			await EndCurrentSessionAsync(UserSessionEndReason.LoggedOut, cancellationToken);
		}
		finally
		{
			_authorizationService.SignOut();
		}
	}

	public void Logout()
	{
		using var cancellation = new CancellationTokenSource(_options.ShutdownWriteTimeout);
		try
		{
			LogoutAsync(cancellation.Token).GetAwaiter().GetResult();
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			_authorizationService.SignOut();
		}
		catch (Exception exception)
		{
			StartupDiagnostics.LogException(exception);
			_authorizationService.SignOut();
		}
	}

	public async Task CloseApplicationAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await EndCurrentSessionAsync(UserSessionEndReason.ApplicationClosed, cancellationToken);
		}
		finally
		{
			_authorizationService.SignOut();
		}
	}

	public void CloseApplication()
	{
		using var cancellation = new CancellationTokenSource(_options.ShutdownWriteTimeout);
		try
		{
			CloseApplicationAsync(cancellation.Token).GetAwaiter().GetResult();
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			_authorizationService.SignOut();
		}
		catch (Exception exception)
		{
			StartupDiagnostics.LogException(exception);
			_authorizationService.SignOut();
		}
	}

	public void Reset()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		LogoutRequestedByUser = false;
	}

	private async Task EndCurrentSessionAsync(UserSessionEndReason reason, CancellationToken cancellationToken)
	{
		UserSessionRepository? repository;
		Guid? sessionId;
		CancellationTokenSource? heartbeatCancellation;
		Task? heartbeatTask;

		await _lifecycleGate.WaitAsync(cancellationToken);
		try
		{
			lock (_stateGate)
			{
				repository = _repository;
				sessionId = _currentSessionId;
				heartbeatCancellation = _heartbeatCancellation;
				heartbeatTask = _heartbeatTask;
				_currentSessionId = null;
				_heartbeatCancellation = null;
				_heartbeatTask = null;
			}
			heartbeatCancellation?.Cancel();
		}
		finally
		{
			_lifecycleGate.Release();
		}

		try
		{
			if (repository is not null && sessionId is not null)
				await repository.EndAsync(sessionId.Value, _timeProvider.GetUtcNow().UtcDateTime, reason, cancellationToken);
		}
		finally
		{
			if (heartbeatTask is not null)
			{
				try { await heartbeatTask; }
				catch (OperationCanceledException) when (heartbeatCancellation?.IsCancellationRequested == true) { }
			}
			heartbeatCancellation?.Dispose();
		}
	}

	private async Task RunHeartbeatAsync(UserSessionRepository repository, Guid sessionId, CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_options.HeartbeatInterval, _timeProvider);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				try
				{
					await repository.UpdateHeartbeatAsync(sessionId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception exception)
				{
					StartupDiagnostics.LogException(exception);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		CancellationTokenSource? cancellation;
		lock (_stateGate)
		{
			cancellation = _heartbeatCancellation;
			_heartbeatCancellation = null;
			_heartbeatTask = null;
			_currentSessionId = null;
		}
		cancellation?.Cancel();
		cancellation?.Dispose();
		_lifecycleGate.Dispose();
	}
}
