// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Diagnostics;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed record UserSessionClientInfo(Guid ClientInstanceId, string? MachineName, string? AppVersion);

/// <summary>Manages the current authenticated Depot session and its presence heartbeat.</summary>
public sealed class SessionService : IDisposable
{
	private static readonly TimeSpan ActivityUpdateGranularity = TimeSpan.FromSeconds(2);
	private readonly AuthorizationService _authorizationService;
	private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
	private readonly object _stateGate = new();
	private IDatabaseTransactionRunner? _transactions;
	private UserSessionRepository? _repository;
	private SecurityEventService? _securityEvents;
	private UserSessionClientInfo? _clientInfo;
	private TimeProvider _timeProvider = TimeProvider.System;
	private UserSessionPresenceOptions _options = UserSessionPresenceOptions.Default;
	private CancellationTokenSource? _heartbeatCancellation;
	private Task? _heartbeatTask;
	private Guid? _currentSessionId;
	private DateTime? _lastActivityUtc;
	private bool _disposed;

	public SessionService(AuthorizationService authorizationService) => _authorizationService = authorizationService;

	public event EventHandler? SessionRevoked;
	public bool LogoutRequestedByUser { get; private set; }
	public bool ReauthenticationRequested { get; private set; }
	public bool RestartLoginRequested => LogoutRequestedByUser || ReauthenticationRequested;
	public Guid? CurrentSessionId { get { lock (_stateGate) return _currentSessionId; } }
	public Guid? CurrentClientInstanceId { get { lock (_stateGate) return _clientInfo?.ClientInstanceId; } }
	public string? CurrentMachineName { get { lock (_stateGate) return _clientInfo?.MachineName; } }

	internal void Configure(UserSessionRepository repository, UserSessionClientInfo clientInfo, TimeProvider? timeProvider = null, UserSessionPresenceOptions? options = null) => ConfigureCore(null, repository, null, clientInfo, timeProvider, options);

	internal void Configure(IDatabaseTransactionRunner transactions, UserSessionRepository repository, SecurityEventService securityEvents, UserSessionClientInfo clientInfo, TimeProvider? timeProvider = null, UserSessionPresenceOptions? options = null) =>
		ConfigureCore(transactions, repository, securityEvents, clientInfo, timeProvider, options);

	private void ConfigureCore(IDatabaseTransactionRunner? transactions, UserSessionRepository repository, SecurityEventService? securityEvents, UserSessionClientInfo clientInfo, TimeProvider? timeProvider, UserSessionPresenceOptions? options)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(repository);
		ArgumentNullException.ThrowIfNull(clientInfo);
		lock (_stateGate)
		{
			if (_repository is not null) throw new InvalidOperationException("User session persistence is already configured.");
			_transactions = transactions; _repository = repository; _securityEvents = securityEvents; _clientInfo = clientInfo;
			_timeProvider = timeProvider ?? TimeProvider.System; _options = options ?? UserSessionPresenceOptions.Default;
		}
	}

	public async Task StartAuthenticatedSessionAsync(long userId, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		UserSessionRepository? repository; UserSessionClientInfo? clientInfo; IDatabaseTransactionRunner? transactions; SecurityEventService? securityEvents;
		lock (_stateGate) { repository = _repository; clientInfo = _clientInfo; transactions = _transactions; securityEvents = _securityEvents; }
		if (repository is null || clientInfo is null) return;
		await _lifecycleGate.WaitAsync(cancellationToken);
		try
		{
			lock (_stateGate) if (_currentSessionId is not null) throw new InvalidOperationException("An authenticated user session is already active.");
			var now = _timeProvider.GetUtcNow().UtcDateTime;
			var session = new UserSession { SessionId=Guid.NewGuid(), UserId=userId, StartedUtc=now, LastSeenUtc=now, LastActivityUtc=now, ClientInstanceId=clientInfo.ClientInstanceId, MachineName=clientInfo.MachineName, AppVersion=clientInfo.AppVersion };
			SecurityEvent[] supersededEvents = [];
			if (transactions is null)
			{
				await repository.CreateAsync(session, cancellationToken);
			}
			else
			{
				supersededEvents = await transactions.ExecuteAsync(async (transaction, token) =>
				{
					await UserSessionRepository.AcquirePolicyLockAsync(transaction, token);
					var policy = await UserSessionRepository.GetPolicyAsync(transaction, token);
					var openSessions = await UserSessionRepository.GetOpenSessionsForUserAsync(transaction, userId, token);
					var events = new List<SecurityEvent>();
					if (policy.ConcurrentSessionMode != ConcurrentSessionMode.Unlimited)
					{
						var maximum = policy.EffectiveMaximumConcurrentSessions;
						if (openSessions.Count >= maximum)
						{
							if (policy.ConcurrentSessionLimitAction == ConcurrentSessionLimitAction.RejectNewSession) throw new SessionLimitExceededException();
							var toSupersede = openSessions.OrderBy(value => value.StartedUtc).ThenBy(value => value.Id).Take(openSessions.Count - maximum + 1).ToArray();
							foreach (var existing in toSupersede)
							{
								if (!await UserSessionRepository.EndAsync(transaction, existing.SessionId, now, UserSessionEndReason.Superseded, token)) throw new ConcurrencyConflictException("user session");
								if (securityEvents is not null)
								{
									var securityEvent = securityEvents.CreateSessionEvent(existing, SecurityEventType.SessionSuperseded, SecurityEventSeverity.Warning, "Session superseded by concurrent-session policy", $"A new login exceeded the configured limit of {maximum} session(s).");
									securityEvent.Id = await SecurityEventRepository.CreateAsync(transaction, securityEvent, token); events.Add(securityEvent);
								}
							}
						}
					}
					session.Id = await UserSessionRepository.CreateAsync(transaction, session, token);
					return events.ToArray();
				}, cancellationToken);
			}
			if (securityEvents is not null) foreach (var securityEvent in supersededEvents) await securityEvents.NotifyPersistedAsync(securityEvent, cancellationToken);
			var heartbeatCancellation = new CancellationTokenSource();
			lock (_stateGate) { _currentSessionId=session.SessionId; _lastActivityUtc=now; _heartbeatCancellation=heartbeatCancellation; _heartbeatTask=RunHeartbeatAsync(repository,session.SessionId,heartbeatCancellation.Token); }
		}
		finally { _lifecycleGate.Release(); }
	}

	public void RecordActivity()
	{
		if (_disposed) return;
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		lock (_stateGate) { if (_currentSessionId is null) return; if (_lastActivityUtc is { } last && now-last<ActivityUpdateGranularity) return; _lastActivityUtc=now; }
	}

	public async Task<bool> TrySendHeartbeatAsync(CancellationToken cancellationToken=default)
	{
		UserSessionRepository? repository; Guid? sessionId;
		lock(_stateGate){repository=_repository;sessionId=_currentSessionId;}
		if(repository is null||sessionId is null)return false;
		try{return await SendHeartbeatAsync(repository,sessionId.Value,cancellationToken).ConfigureAwait(false);}catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return false;}catch(Exception exception){StartupDiagnostics.LogException(exception);return false;}
	}

	public async Task LogoutAsync(CancellationToken cancellationToken=default){LogoutRequestedByUser=true;try{await EndCurrentSessionAsync(UserSessionEndReason.LoggedOut,cancellationToken);}finally{_authorizationService.SignOut();}}
	public void Logout(){LogoutRequestedByUser=true;using var cancellation=new CancellationTokenSource(_options.ShutdownWriteTimeout);try{Task.Run(()=>EndCurrentSessionAsync(UserSessionEndReason.LoggedOut,cancellation.Token),cancellation.Token).GetAwaiter().GetResult();}catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}catch(Exception exception){StartupDiagnostics.LogException(exception);}finally{_authorizationService.SignOut();}}
	public async Task CloseApplicationAsync(CancellationToken cancellationToken=default){try{await EndCurrentSessionAsync(UserSessionEndReason.ApplicationClosed,cancellationToken);}finally{_authorizationService.SignOut();}}
	public void CloseApplication(){using var cancellation=new CancellationTokenSource(_options.ShutdownWriteTimeout);try{Task.Run(()=>EndCurrentSessionAsync(UserSessionEndReason.ApplicationClosed,cancellation.Token),cancellation.Token).GetAwaiter().GetResult();}catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}catch(Exception exception){StartupDiagnostics.LogException(exception);}finally{_authorizationService.SignOut();}}
	public void Reset(){ObjectDisposedException.ThrowIf(_disposed,this);LogoutRequestedByUser=false;ReauthenticationRequested=false;}

	private async Task<bool> SendHeartbeatAsync(UserSessionRepository repository,Guid sessionId,CancellationToken cancellationToken)
	{
		DateTime? lastActivityUtc;lock(_stateGate){if(_currentSessionId!=sessionId)return false;lastActivityUtc=_lastActivityUtc;}
		var now=_timeProvider.GetUtcNow().UtcDateTime;
		var active=await repository.UpdateHeartbeatAsync(sessionId,now,lastActivityUtc,cancellationToken).ConfigureAwait(false);
		if(!active){HandleRemoteTermination(sessionId,false);return false;}
		var policy=await repository.GetPolicyAsync(cancellationToken).ConfigureAwait(false);
		if(await repository.ExpireSessionIfPolicyExceededAsync(sessionId,now,policy,cancellationToken).ConfigureAwait(false))
		{
			if(_securityEvents is not null)
			{
				var ended=await repository.GetBySessionIdAsync(sessionId,cancellationToken).ConfigureAwait(false);
				if(ended is not null)await _securityEvents.RecordSessionEventAsync(ended,SecurityEventType.SessionExpired,SecurityEventSeverity.Warning,"Session expired under the active session policy",null,cancellationToken).ConfigureAwait(false);
			}
			HandleRemoteTermination(sessionId,true);return false;
		}
		return true;
	}

	private async Task EndCurrentSessionAsync(UserSessionEndReason reason,CancellationToken cancellationToken)
	{
		UserSessionRepository? repository;Guid? sessionId;CancellationTokenSource? heartbeatCancellation;Task? heartbeatTask;
		await _lifecycleGate.WaitAsync(cancellationToken);
		try{lock(_stateGate){repository=_repository;sessionId=_currentSessionId;heartbeatCancellation=_heartbeatCancellation;heartbeatTask=_heartbeatTask;_currentSessionId=null;_lastActivityUtc=null;_heartbeatCancellation=null;_heartbeatTask=null;}heartbeatCancellation?.Cancel();}finally{_lifecycleGate.Release();}
		try{if(repository is not null&&sessionId is not null)await repository.EndAsync(sessionId.Value,_timeProvider.GetUtcNow().UtcDateTime,reason,cancellationToken);}finally{if(heartbeatTask is not null){try{await heartbeatTask.ConfigureAwait(false);}catch(OperationCanceledException)when(heartbeatCancellation?.IsCancellationRequested==true){}}heartbeatCancellation?.Dispose();}
	}

	private async Task RunHeartbeatAsync(UserSessionRepository repository,Guid sessionId,CancellationToken cancellationToken)
	{
		using var timer=new PeriodicTimer(_options.HeartbeatInterval,_timeProvider);
		try{while(await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)){try{if(!await SendHeartbeatAsync(repository,sessionId,cancellationToken).ConfigureAwait(false))break;}catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){break;}catch(Exception exception){StartupDiagnostics.LogException(exception);}}}catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){}
	}

	private void HandleRemoteTermination(Guid sessionId,bool expired)
	{
		CancellationTokenSource? cancellation;lock(_stateGate){if(_currentSessionId!=sessionId)return;_currentSessionId=null;_lastActivityUtc=null;cancellation=_heartbeatCancellation;_heartbeatCancellation=null;_heartbeatTask=null;ReauthenticationRequested=true;}
		cancellation?.Cancel();cancellation?.Dispose();_authorizationService.SignOut();StartupDiagnostics.Log(expired?"Authenticated session expired under the active session policy. Returning to login.":"Authenticated session was revoked remotely. Returning to login.");SessionRevoked?.Invoke(this,EventArgs.Empty);
	}

	public void Dispose(){if(_disposed)return;_disposed=true;CancellationTokenSource? cancellation;lock(_stateGate){cancellation=_heartbeatCancellation;_heartbeatCancellation=null;_heartbeatTask=null;_currentSessionId=null;_lastActivityUtc=null;}cancellation?.Cancel();cancellation?.Dispose();_lifecycleGate.Dispose();}
}
