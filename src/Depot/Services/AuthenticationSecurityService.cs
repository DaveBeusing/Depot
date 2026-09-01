// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class AuthenticationSecurityService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly AuthenticationSecurityRepository _repository;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly SecurityEventService _securityEvents;
	private readonly IAuthorizationService _authorization;
	private readonly TimeProvider _timeProvider;

	public AuthenticationSecurityService(
		IDatabaseTransactionRunner transactions,
		AuthenticationSecurityRepository repository,
		AuditRepository auditEntries,
		AuditService audit,
		SecurityEventService securityEvents,
		IAuthorizationService authorization,
		TimeProvider? timeProvider = null)
	{
		_transactions = transactions;
		_repository = repository;
		_auditEntries = auditEntries;
		_audit = audit;
		_securityEvents = securityEvents;
		_authorization = authorization;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public bool CanManagePolicy => _authorization.HasPermission(ApplicationPermission.SettingsManage);

	public Task<AuthenticationSecurityPolicy> GetPolicyAsync(CancellationToken cancellationToken) => _repository.GetPolicyAsync(cancellationToken);

	public Task<AuthenticationThrottleSnapshot> GetStatusAsync(string accountKey, CancellationToken cancellationToken) =>
		_transactions.ExecuteAsync(async (transaction, token) =>
		{
			await AuthenticationSecurityRepository.AcquirePolicyLockAsync(transaction, token);
			var policy = await AuthenticationSecurityRepository.GetPolicyAsync(transaction, token);
			var state = await AuthenticationSecurityRepository.GetThrottleAsync(transaction, Normalize(accountKey), token);
			return await NormalizeStateAsync(transaction, state, policy, _timeProvider.GetUtcNow().UtcDateTime, token);
		}, cancellationToken);

	public Task<LoginAttemptStatus> RecordFailureAsync(string accountKey, CancellationToken cancellationToken) =>
		_transactions.ExecuteAsync(async (transaction, token) =>
		{
			await AuthenticationSecurityRepository.AcquirePolicyLockAsync(transaction, token);
			var policy = await AuthenticationSecurityRepository.GetPolicyAsync(transaction, token);
			var key = Normalize(accountKey);
			var now = _timeProvider.GetUtcNow().UtcDateTime;
			var state = await AuthenticationSecurityRepository.GetThrottleAsync(transaction, key, token);
			var current = await NormalizeStateAsync(transaction, state, policy, now, token);
			if (current.IsBlocked) return new LoginAttemptStatus(current.FailureCount, true, current.RetryAfter);

			var firstFailure = current.FailureCount == 0 ? now : state!.FirstFailureUtc;
			var failures = current.FailureCount + 1;
			var blockedUntil = failures >= policy.LockoutThreshold ? now + policy.LockoutDuration : (DateTime?)null;
			await AuthenticationSecurityRepository.UpsertThrottleAsync(transaction, new AuthenticationThrottleState
			{
				AccountKey = key,
				FirstFailureUtc = firstFailure,
				FailureCount = failures,
				BlockedUntilUtc = blockedUntil,
				UpdatedUtc = now
			}, token);
			var retryAfter = blockedUntil is null ? TimeSpan.Zero : blockedUntil.Value - now;
			return new LoginAttemptStatus(failures, blockedUntil is not null, retryAfter);
		}, cancellationToken);

	public Task<int> RecordSuccessAsync(string accountKey, CancellationToken cancellationToken) =>
		_transactions.ExecuteAsync(async (transaction, token) =>
		{
			await AuthenticationSecurityRepository.AcquirePolicyLockAsync(transaction, token);
			var policy = await AuthenticationSecurityRepository.GetPolicyAsync(transaction, token);
			var key = Normalize(accountKey);
			var state = await AuthenticationSecurityRepository.GetThrottleAsync(transaction, key, token);
			var snapshot = await NormalizeStateAsync(transaction, state, policy, _timeProvider.GetUtcNow().UtcDateTime, token);
			await AuthenticationSecurityRepository.DeleteThrottleAsync(transaction, key, token);
			return snapshot.FailureCount;
		}, cancellationToken);

	public async Task<AuthenticationSecurityPolicy> SavePolicyAsync(
		int failureWindowMinutes,
		int lockoutThreshold,
		int lockoutDurationMinutes,
		int securityEventRetentionDays,
		long expectedVersion,
		CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SettingsManage);
		ValidatePolicy(failureWindowMinutes, lockoutThreshold, lockoutDurationMinutes, securityEventRetentionDays);
		var result = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await AuthenticationSecurityRepository.AcquirePolicyLockAsync(transaction, token);
			var before = await AuthenticationSecurityRepository.GetPolicyAsync(transaction, token);
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("authentication security policy");
			var after = new AuthenticationSecurityPolicy
			{
				Id = before.Id,
				FailureWindowMinutes = failureWindowMinutes,
				LockoutThreshold = lockoutThreshold,
				LockoutDurationMinutes = lockoutDurationMinutes,
				SecurityEventRetentionDays = securityEventRetentionDays,
				UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
				Version = before.Version + 1
			};
			if (!await AuthenticationSecurityRepository.UpdatePolicyAsync(transaction, after, expectedVersion, token))
				throw new ConcurrencyConflictException("authentication security policy");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(after.Id, "UpdateAuthenticationSecurityPolicy", before, after), token);
			var securityEvent = _securityEvents.CreateAuthenticationPolicyChangedEvent(before, after);
			securityEvent.Id = await SecurityEventRepository.CreateAsync(transaction, securityEvent, token);
			return (Policy: after, Event: securityEvent);
		}, cancellationToken);
		await _securityEvents.NotifyPersistedAsync(result.Event, cancellationToken);
		return result.Policy;
	}

	private static async Task<AuthenticationThrottleSnapshot> NormalizeStateAsync(
		DatabaseTransactionContext transaction,
		AuthenticationThrottleState? state,
		AuthenticationSecurityPolicy policy,
		DateTime now,
		CancellationToken cancellationToken)
	{
		if (state is null) return new AuthenticationThrottleSnapshot(0, false, TimeSpan.Zero);
		if (state.BlockedUntilUtc is { } blockedUntil && blockedUntil > now)
			return new AuthenticationThrottleSnapshot(state.FailureCount, true, blockedUntil - now);
		if (state.BlockedUntilUtc is not null || now - state.FirstFailureUtc >= policy.FailureWindow)
		{
			await AuthenticationSecurityRepository.DeleteThrottleAsync(transaction, state.AccountKey, cancellationToken);
			return new AuthenticationThrottleSnapshot(0, false, TimeSpan.Zero);
		}
		return new AuthenticationThrottleSnapshot(state.FailureCount, false, TimeSpan.Zero);
	}

	private static void ValidatePolicy(int failureWindowMinutes, int lockoutThreshold, int lockoutDurationMinutes, int retentionDays)
	{
		if (failureWindowMinutes is < AuthenticationSecurityPolicy.MinimumFailureWindowMinutes or > AuthenticationSecurityPolicy.MaximumFailureWindowMinutes)
			throw new ArgumentOutOfRangeException(nameof(failureWindowMinutes));
		if (lockoutThreshold is < AuthenticationSecurityPolicy.MinimumLockoutThreshold or > AuthenticationSecurityPolicy.MaximumLockoutThreshold)
			throw new ArgumentOutOfRangeException(nameof(lockoutThreshold));
		if (lockoutDurationMinutes is < AuthenticationSecurityPolicy.MinimumLockoutDurationMinutes or > AuthenticationSecurityPolicy.MaximumLockoutDurationMinutes)
			throw new ArgumentOutOfRangeException(nameof(lockoutDurationMinutes));
		if (retentionDays is < AuthenticationSecurityPolicy.MinimumSecurityEventRetentionDays or > AuthenticationSecurityPolicy.MaximumSecurityEventRetentionDays)
			throw new ArgumentOutOfRangeException(nameof(retentionDays));
	}

	private static string Normalize(string accountKey) => accountKey.Trim().ToLowerInvariant();
}
