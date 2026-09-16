// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Net;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SecurityEventDeliveryTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-security-delivery-{Guid.NewGuid():N}.db");
	private readonly MutableTimeProvider _clock = new() { UtcNow = new DateTime(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc) };

	[Fact]
	public async Task TransientFailureRetainsCheckpointAndRetriesExactSnapshot()
	{
		var context = CreateContext();
		var targetId = await CreateTargetAsync(context, batchSize: 10);
		var firstId = await CreateEventAsync(context.Events, "first");
		context.Sink.Failures.Enqueue(new SecurityEventSinkException(SecurityEventDeliveryFailureKind.Transient, "HTTP_503", "temporary"));

		var failed = Assert.Single(await context.Delivery.RunOnceAsync());
		Assert.False(failed.Succeeded);
		Assert.Equal(SecurityEventDeliveryFailureKind.Transient, failed.FailureKind);
		var afterFailure = (await context.Repository.GetStatusAsync(targetId, CancellationToken.None))!.State;
		Assert.Equal(0, afterFailure.LastEventId);
		Assert.Equal(firstId, afterFailure.PendingSnapshotUpperBoundId);
		Assert.NotNull(afterFailure.NextAttemptUtc);

		var secondId = await CreateEventAsync(context.Events, "second");
		_clock.UtcNow = afterFailure.NextAttemptUtc!.Value.AddSeconds(1);
		var retried = Assert.Single(await context.Delivery.RunOnceAsync());
		Assert.True(retried.Succeeded);
		Assert.Equal(firstId, retried.CheckpointEventId);
		Assert.Equal(2, context.Sink.Batches.Count);
		Assert.Equal(context.Sink.Batches[0].SnapshotUpperBoundId, context.Sink.Batches[1].SnapshotUpperBoundId);
		Assert.Equal(context.Sink.Batches[0].Events.Select(value => value.EventId), context.Sink.Batches[1].Events.Select(value => value.EventId));
		Assert.DoesNotContain(context.Sink.Batches[1].Events, value => value.EventId == secondId);

		var next = Assert.Single(await context.Delivery.RunOnceAsync());
		Assert.True(next.Succeeded);
		Assert.Equal(secondId, next.CheckpointEventId);
	}

	[Fact]
	public async Task PermanentFailureSuspendsAutomaticDeliveryWithoutAdvancingCheckpoint()
	{
		var context = CreateContext();
		var targetId = await CreateTargetAsync(context, batchSize: 10);
		var eventId = await CreateEventAsync(context.Events, "permanent");
		context.Sink.Failures.Enqueue(new SecurityEventSinkException(SecurityEventDeliveryFailureKind.Permanent, "HTTP_400", "invalid request"));

		var result = Assert.Single(await context.Delivery.RunOnceAsync());
		Assert.True(result.Suspended);
		var state = (await context.Repository.GetStatusAsync(targetId, CancellationToken.None))!.State;
		Assert.True(state.IsSuspended);
		Assert.Equal(0, state.LastEventId);
		Assert.Equal(eventId, state.PendingSnapshotUpperBoundId);

		_clock.UtcNow = _clock.UtcNow.AddDays(1);
		Assert.Empty(await context.Delivery.RunOnceAsync());
	}

	[Theory]
	[InlineData(429, SecurityEventDeliveryFailureKind.Transient)]
	[InlineData(503, SecurityEventDeliveryFailureKind.Transient)]
	[InlineData(400, SecurityEventDeliveryFailureKind.Permanent)]
	public async Task HttpSinkClassifiesResponseFailures(int statusCode, SecurityEventDeliveryFailureKind expectedKind)
	{
		using var client = new HttpClient(new StaticStatusHandler((HttpStatusCode)statusCode));
		using var factory = new HttpJsonSecurityEventExportSinkFactory(client);
		var target = Target(0, 10);
		var sink = factory.Create(target);
		var filterHash = SecurityEventExportService.ComputeFilterSha256(target.Filter);
		var checkpoint = new SecurityEventExportCheckpoint(0, filterHash);
		var batch = new SecurityEventExportBatch(1, 1, checkpoint, checkpoint with { LastEventId = 1 }, [new SecurityEventExportRecord(1, _clock.UtcNow, SecurityEventType.AuthenticationFailed, SecurityEventSeverity.Warning, null, null, null, null, null, "failure", null)], false);

		var exception = await Assert.ThrowsAsync<SecurityEventSinkException>(() => sink.WriteAsync(batch, CancellationToken.None));
		Assert.Equal(expectedKind, exception.Kind);
	}

	[Fact]
	public async Task FilterChangeResetsCheckpointInsteadOfSilentlySkippingHistoricalEvents()
	{
		var context = CreateContext();
		var targetId = await CreateTargetAsync(context, batchSize: 10);
		await CreateEventAsync(context.Events, "delivered");
		Assert.True(Assert.Single(await context.Delivery.RunOnceAsync()).Succeeded);
		var status = (await context.Repository.GetStatusAsync(targetId, CancellationToken.None))!;
		Assert.True(status.State.LastEventId > 0);

		context.Authorization.SignIn(new User { Id = 1, Email = "admin@test.local", DisplayName = "Admin", IsActive = true }, new HashSet<ApplicationPermission> { ApplicationPermission.SecurityEventsManage });
		var updated = status.Target with { MinimumSeverity = SecurityEventSeverity.Critical };
		await context.Delivery.UpdateTargetAsync(updated, CancellationToken.None);
		var reset = (await context.Repository.GetStatusAsync(targetId, CancellationToken.None))!.State;
		Assert.Equal(0, reset.LastEventId);
		Assert.Equal(SecurityEventExportService.ComputeFilterSha256(updated.Filter), reset.FilterSha256);
	}

	private TestContext CreateContext()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		SecurityEventSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(access);
		var repository = new SecurityEventDeliveryRepository(access);
		var events = new SecurityEventRepository(access);
		var authorization = new AuthorizationService();
		var audit = new AuditRepository(access);
		var sink = new FakeSink();
		var sinkFactory = new FakeSinkFactory(sink);
		var delivery = new SecurityEventDeliveryService(transactions, repository, new SecurityEventExportService(new SecurityEventExportRepository(access)), audit, new AuditService(audit, authorization), authorization, sinkFactory, _clock);
		return new TestContext(transactions, repository, events, authorization, sink, delivery);
	}

	private async Task<long> CreateTargetAsync(TestContext context, int batchSize)
	{
		var target = Target(0, batchSize) with { CreatedUtc = _clock.UtcNow, UpdatedUtc = _clock.UtcNow };
		var hash = SecurityEventExportService.ComputeFilterSha256(target.Filter);
		return await context.Transactions.ExecuteAsync((transaction, token) => SecurityEventDeliveryRepository.CreateAsync(transaction, target, hash, token), CancellationToken.None);
	}

	private async Task<long> CreateEventAsync(SecurityEventRepository repository, string summary) =>
		await repository.CreateAsync(new SecurityEvent { TimestampUtc = _clock.UtcNow, EventType = SecurityEventType.AuthenticationFailed, Severity = SecurityEventSeverity.Warning, Summary = summary }, CancellationToken.None);

	private static SecurityEventExportTarget Target(long id, int batchSize) => new()
	{
		Id = id,
		Code = "TEST_TARGET",
		SinkCode = HttpJsonSecurityEventExportSinkFactory.Code,
		EndpointUri = "https://security.example.test/events",
		MinimumSeverity = SecurityEventSeverity.Information,
		EventTypes = [],
		BatchSize = batchSize,
		IsEnabled = true,
		Version = 1
	};

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(DatabaseTransactionRunner Transactions, SecurityEventDeliveryRepository Repository, SecurityEventRepository Events, AuthorizationService Authorization, FakeSink Sink, SecurityEventDeliveryService Delivery);

	private sealed class MutableTimeProvider : TimeProvider
	{
		public DateTime UtcNow { get; set; }
		public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
	}

	private sealed class FakeSinkFactory(FakeSink sink) : ISecurityEventExportSinkFactory
	{
		public ISecurityEventExportSink Create(SecurityEventExportTarget target) => sink;
		public void Dispose() { }
	}

	private sealed class FakeSink : ISecurityEventExportSink
	{
		public string SinkCode => HttpJsonSecurityEventExportSinkFactory.Code;
		public Queue<SecurityEventSinkException> Failures { get; } = new();
		public List<SecurityEventExportBatch> Batches { get; } = [];
		public Task WriteAsync(SecurityEventExportBatch batch, CancellationToken cancellationToken)
		{
			Batches.Add(batch);
			if (Failures.Count > 0) throw Failures.Dequeue();
			return Task.CompletedTask;
		}
	}

	private sealed class StaticStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode));
	}
}
