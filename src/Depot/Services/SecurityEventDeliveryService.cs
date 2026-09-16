// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Diagnostics;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

internal sealed class SecurityEventDeliveryService : IDisposable
{
	private const int MaximumTargetsPerRun = 10;
	private static readonly TimeSpan DeliveryInterval = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly SecurityEventDeliveryRepository _repository;
	private readonly SecurityEventExportService _export;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly ISecurityEventExportSinkFactory _sinkFactory;
	private readonly TimeProvider _timeProvider;
	private readonly SemaphoreSlim _runGate = new(1, 1);
	private CancellationTokenSource? _cancellation;
	private Task? _loop;
	private bool _disposed;

	public SecurityEventDeliveryService(
		IDatabaseTransactionRunner transactions,
		SecurityEventDeliveryRepository repository,
		SecurityEventExportService export,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization,
		ISecurityEventExportSinkFactory sinkFactory,
		TimeProvider? timeProvider = null)
	{
		_transactions = transactions;
		_repository = repository;
		_export = export;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_sinkFactory = sinkFactory;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_loop is not null) return;
		_cancellation = new CancellationTokenSource();
		_loop = RunLoopAsync(_cancellation.Token);
	}

	public Task<IReadOnlyList<SecurityEventDeliveryStatus>> ListAsync(CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SecurityEventsView);
		return _repository.ListStatusesAsync(cancellationToken);
	}

	public async Task<SecurityEventExportTarget> CreateTargetAsync(SecurityEventExportTarget value, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SecurityEventsManage);
		var now = UtcNow();
		var normalized = NormalizeTarget(value with { Id = 0, Version = 1, CreatedUtc = now, UpdatedUtc = now });
		var fingerprint = SecurityEventExportService.ComputeFilterSha256(normalized.Filter);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await SecurityEventDeliveryRepository.CreateAsync(transaction, normalized, fingerprint, token);
			var created = normalized with { Id = id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<SecurityEventExportTarget> UpdateTargetAsync(SecurityEventExportTarget value, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SecurityEventsManage);
		if (value.Id <= 0) throw new ArgumentOutOfRangeException(nameof(value));
		var before = await _repository.GetTargetAsync(value.Id, cancellationToken) ?? throw new InvalidOperationException("Security-event export target was not found.");
		if (before.Version != value.Version) throw new ConcurrencyConflictException("security-event export target");
		if (!string.Equals(before.Code, value.Code.Trim(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Security-event export target code cannot be changed after creation.");
		var after = NormalizeTarget(value with { Code = before.Code, CreatedUtc = before.CreatedUtc, UpdatedUtc = UtcNow() });
		var beforeFingerprint = SecurityEventExportService.ComputeFilterSha256(before.Filter);
		var afterFingerprint = SecurityEventExportService.ComputeFilterSha256(after.Filter);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await SecurityEventDeliveryRepository.UpdateTargetAsync(transaction, after, before.Version, token)) throw new ConcurrencyConflictException("security-event export target");
			if (!string.Equals(beforeFingerprint, afterFingerprint, StringComparison.Ordinal))
				await SecurityEventDeliveryRepository.ResetStateAsync(transaction, after.Id, afterFingerprint, after.UpdatedUtc, token);
			var saved = after with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(saved.Id, before, saved), token);
			return saved;
		}, cancellationToken);
	}

	public async Task ResumeAsync(long targetId, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SecurityEventsManage);
		var target = await _repository.GetTargetAsync(targetId, cancellationToken) ?? throw new InvalidOperationException("Security-event export target was not found.");
		await _repository.ResumeAsync(targetId, UtcNow(), cancellationToken);
		await _audit.RecordActionAsync(targetId, "DeliveryResumed", target, target, cancellationToken);
	}

	public async Task<IReadOnlyList<SecurityEventDeliveryRunResult>> RunOnceAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		await _runGate.WaitAsync(cancellationToken);
		try
		{
			var due = await _repository.ListDueAsync(UtcNow(), MaximumTargetsPerRun, cancellationToken);
			var results = new List<SecurityEventDeliveryRunResult>(due.Count);
			foreach (var status in due)
			{
				cancellationToken.ThrowIfCancellationRequested();
				results.Add(await DeliverAsync(status, cancellationToken));
			}
			return results;
		}
		finally { _runGate.Release(); }
	}

	private async Task<SecurityEventDeliveryRunResult> DeliverAsync(SecurityEventDeliveryStatus candidate, CancellationToken cancellationToken)
	{
		var now = UtcNow();
		var leaseToken = Guid.NewGuid();
		if (!await _repository.TryAcquireLeaseAsync(candidate.Target.Id, candidate.State.Version, leaseToken, now, now + LeaseDuration, cancellationToken))
			return new(candidate.Target.Id, candidate.Target.Code, 0, candidate.State.LastEventId, false, true, candidate.State.IsSuspended, null, null);

		var current = await _repository.GetStatusAsync(candidate.Target.Id, cancellationToken) ?? throw new InvalidOperationException("Security-event export target disappeared after lease acquisition.");
		var checkpoint = new SecurityEventExportCheckpoint(current.State.LastEventId, current.State.FilterSha256);
		try
		{
			var batch = await _export.ReadBatchAsync(current.Target.Filter, checkpoint, current.Target.BatchSize, current.State.PendingSnapshotUpperBoundId, cancellationToken);
			if (batch.Events.Count == 0)
			{
				if (!await _repository.CompleteSuccessAsync(current.Target.Id, leaseToken, batch.NextCheckpoint.LastEventId, UtcNow(), cancellationToken)) throw new ConcurrencyConflictException("security-event export delivery state");
				return new(current.Target.Id, current.Target.Code, 0, batch.NextCheckpoint.LastEventId, true, false, false, null, null);
			}

			if (current.State.PendingSnapshotUpperBoundId is null && !await _repository.SetPendingSnapshotAsync(current.Target.Id, leaseToken, batch.SnapshotUpperBoundId, UtcNow(), cancellationToken))
				throw new ConcurrencyConflictException("security-event export delivery state");

			var sink = _sinkFactory.Create(current.Target);
			await sink.WriteAsync(batch, cancellationToken);
			if (!await _repository.CompleteSuccessAsync(current.Target.Id, leaseToken, batch.NextCheckpoint.LastEventId, UtcNow(), cancellationToken)) throw new ConcurrencyConflictException("security-event export delivery state");
			return new(current.Target.Id, current.Target.Code, batch.Events.Count, batch.NextCheckpoint.LastEventId, true, false, false, null, null);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (SecurityEventSinkException exception)
		{
			return await CompleteFailureAsync(current, leaseToken, exception.Kind, exception.Code, exception.Message, cancellationToken);
		}
		catch (Exception exception) when (exception is not ConcurrencyConflictException)
		{
			return await CompleteFailureAsync(current, leaseToken, SecurityEventDeliveryFailureKind.Transient, "UNEXPECTED", exception.Message, cancellationToken);
		}
	}

	private async Task<SecurityEventDeliveryRunResult> CompleteFailureAsync(SecurityEventDeliveryStatus status, Guid leaseToken, SecurityEventDeliveryFailureKind kind, string code, string message, CancellationToken cancellationToken)
	{
		var now = UtcNow();
		var failures = checked(status.State.ConsecutiveFailures + 1);
		var next = kind == SecurityEventDeliveryFailureKind.Transient ? now + RetryDelay(failures) : (DateTime?)null;
		if (!await _repository.CompleteFailureAsync(status.Target.Id, leaseToken, failures, kind, code, message, now, next, cancellationToken)) throw new ConcurrencyConflictException("security-event export delivery state");
		return new(status.Target.Id, status.Target.Code, 0, status.State.LastEventId, false, false, kind == SecurityEventDeliveryFailureKind.Permanent, kind, code);
	}

	private static SecurityEventExportTarget NormalizeTarget(SecurityEventExportTarget source)
	{
		ArgumentNullException.ThrowIfNull(source);
		var code = source.Code.Trim().ToUpperInvariant();
		if (code.Length is < 2 or > 100 || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')) throw new ArgumentException("Security-event export target code must contain 2-100 letters, numbers, underscores, or hyphens.", nameof(source));
		var sink = source.SinkCode.Trim().ToLowerInvariant();
		if (!string.Equals(sink, HttpJsonSecurityEventExportSinkFactory.Code, StringComparison.Ordinal)) throw new ArgumentException("Only the http-json-v1 security-event sink is supported in this package.", nameof(source));
		if (!Uri.TryCreate(source.EndpointUri.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo)) throw new ArgumentException("Security-event export endpoint must be an absolute HTTPS URI without embedded credentials.", nameof(source));
		if (source.BatchSize is < 1 or > SecurityEventExportService.MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(source));
		var filter = SecurityEventExportService.NormalizeFilter(source.Filter);
		return source with { Code = code, SinkCode = sink, EndpointUri = endpoint.AbsoluteUri, MinimumSeverity = filter.MinimumSeverity, EventTypes = filter.EventTypes };
	}

	private static TimeSpan RetryDelay(int failureCount) => failureCount switch
	{
		<= 1 => TimeSpan.FromSeconds(30),
		2 => TimeSpan.FromMinutes(2),
		3 => TimeSpan.FromMinutes(10),
		4 => TimeSpan.FromMinutes(30),
		_ => TimeSpan.FromHours(2)
	};

	private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

	private async Task RunLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			await RunSafelyAsync(cancellationToken);
			using var timer = new PeriodicTimer(DeliveryInterval, _timeProvider);
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
		_sinkFactory.Dispose();
		_runGate.Dispose();
	}
}
