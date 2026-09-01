// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Diagnostics;
using Depot.Repositories;

namespace Depot.Services;

public sealed record SecurityMaintenanceResult(int SessionsDeleted, int SecurityEventsDeleted, int ThrottleEntriesDeleted);

public sealed class SecurityMaintenanceService : IDisposable
{
	private const int BatchSize = 250;
	private const int MaximumBatchesPerRun = 4;
	private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(6);
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly UserSessionRepository _sessions;
	private readonly SecurityEventRepository _securityEvents;
	private readonly AuthenticationSecurityRepository _authenticationSecurity;
	private readonly TimeProvider _timeProvider;
	private readonly SemaphoreSlim _runGate = new(1, 1);
	private CancellationTokenSource? _cancellation;
	private Task? _loop;
	private bool _disposed;

	public SecurityMaintenanceService(IDatabaseTransactionRunner transactions, UserSessionRepository sessions, SecurityEventRepository securityEvents, AuthenticationSecurityRepository authenticationSecurity, TimeProvider? timeProvider = null)
	{
		_transactions = transactions;
		_sessions = sessions;
		_securityEvents = securityEvents;
		_authenticationSecurity = authenticationSecurity;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_loop is not null) return;
		_cancellation = new CancellationTokenSource();
		_loop = RunLoopAsync(_cancellation.Token);
	}

	public async Task<SecurityMaintenanceResult> RunOnceAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		await _runGate.WaitAsync(cancellationToken);
		try
		{
			var now = _timeProvider.GetUtcNow().UtcDateTime;
			var sessionPolicy = await _sessions.GetPolicyAsync(cancellationToken);
			var authenticationPolicy = await _authenticationSecurity.GetPolicyAsync(cancellationToken);
			var sessionsDeleted = await PurgeSessionsAsync(now.AddDays(-sessionPolicy.SessionHistoryRetentionDays), cancellationToken);
			var eventsDeleted = await PurgeSecurityEventsAsync(now.AddDays(-authenticationPolicy.SecurityEventRetentionDays), cancellationToken);
			var throttleHorizon = TimeSpan.FromMinutes(Math.Max(authenticationPolicy.FailureWindowMinutes, authenticationPolicy.LockoutDurationMinutes));
			var throttlesDeleted = await PurgeThrottlesAsync(now - throttleHorizon, cancellationToken);
			return new SecurityMaintenanceResult(sessionsDeleted, eventsDeleted, throttlesDeleted);
		}
		finally { _runGate.Release(); }
	}

	private async Task<int> PurgeSessionsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
	{
		var total = 0;
		for (var batch = 0; batch < MaximumBatchesPerRun; batch++)
		{
			var ids = await _sessions.GetEndedSessionIdsBeforeAsync(cutoffUtc, BatchSize, cancellationToken);
			if (ids.Count == 0) break;
			var deleted = await _transactions.ExecuteAsync(async (transaction, token) =>
			{
				await UserSessionRepository.AcquirePolicyLockAsync(transaction, token);
				return await UserSessionRepository.DeleteEndedSessionsByIdsBeforeAsync(transaction, ids, cutoffUtc, token);
			}, cancellationToken);
			total += deleted;
			if (ids.Count < BatchSize || deleted == 0) break;
		}
		return total;
	}

	private async Task<int> PurgeSecurityEventsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
	{
		var total = 0;
		for (var batch = 0; batch < MaximumBatchesPerRun; batch++)
		{
			var ids = await _securityEvents.GetIdsBeforeAsync(cutoffUtc, BatchSize, cancellationToken);
			if (ids.Count == 0) break;
			var deleted = await _transactions.ExecuteAsync(async (transaction, token) =>
			{
				await AuthenticationSecurityRepository.AcquirePolicyLockAsync(transaction, token);
				return await SecurityEventRepository.DeleteByIdsBeforeAsync(transaction, ids, cutoffUtc, token);
			}, cancellationToken);
			total += deleted;
			if (ids.Count < BatchSize || deleted == 0) break;
		}
		return total;
	}

	private async Task<int> PurgeThrottlesAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
	{
		var total = 0;
		for (var batch = 0; batch < MaximumBatchesPerRun; batch++)
		{
			var keys = await _authenticationSecurity.GetThrottleKeysBeforeAsync(cutoffUtc, BatchSize, cancellationToken);
			if (keys.Count == 0) break;
			var deleted = await _transactions.ExecuteAsync(async (transaction, token) =>
			{
				await AuthenticationSecurityRepository.AcquirePolicyLockAsync(transaction, token);
				return await AuthenticationSecurityRepository.DeleteThrottleKeysBeforeAsync(transaction, keys, cutoffUtc, token);
			}, cancellationToken);
			total += deleted;
			if (keys.Count < BatchSize || deleted == 0) break;
		}
		return total;
	}

	private async Task RunLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			await RunSafelyAsync(cancellationToken);
			using var timer = new PeriodicTimer(MaintenanceInterval, _timeProvider);
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) await RunSafelyAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
	}

	private async Task RunSafelyAsync(CancellationToken cancellationToken)
	{
		try { await RunOnceAsync(cancellationToken).ConfigureAwait(false); }
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (Exception exception) { StartupDiagnostics.LogException(exception); }
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_cancellation?.Cancel();
		_cancellation?.Dispose();
		_cancellation = null;
		_loop = null;
		_runGate.Dispose();
	}
}
