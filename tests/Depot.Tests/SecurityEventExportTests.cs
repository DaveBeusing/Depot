// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SecurityEventExportTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-security-export-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task ExportIsAscendingBoundedAndCheckpointedAgainstOneSnapshot()
	{
		var context = CreateContext();
		var first = await context.CreateAsync(SecurityEventType.AuthenticationFailed, SecurityEventSeverity.Information, "First");
		var second = await context.CreateAsync(SecurityEventType.SuspiciousAuthenticationFailures, SecurityEventSeverity.Warning, "Second");
		var third = await context.CreateAsync(SecurityEventType.AuthenticationBlocked, SecurityEventSeverity.Critical, "Third");

		var firstBatch = await context.Export.ReadBatchAsync(new SecurityEventExportFilter(), null, 2, CancellationToken.None);

		Assert.Equal(SecurityEventExportBatch.CurrentFormatVersion, firstBatch.FormatVersion);
		Assert.True(firstBatch.HasMore);
		Assert.Equal(new[] { first, second }, firstBatch.Events.Select(value => value.EventId).ToArray());
		Assert.Equal(second, firstBatch.NextCheckpoint.LastEventId);
		Assert.Equal(third, firstBatch.SnapshotUpperBoundId);

		var secondBatch = await context.Export.ReadBatchAsync(new SecurityEventExportFilter(), firstBatch.NextCheckpoint, 2, CancellationToken.None);
		Assert.False(secondBatch.HasMore);
		Assert.Equal(new[] { third }, secondBatch.Events.Select(value => value.EventId).ToArray());
		Assert.Equal(third, secondBatch.NextCheckpoint.LastEventId);
	}

	[Fact]
	public async Task FilteredSnapshotAdvancesPastExcludedTrailingEventsAndBindsCheckpointToFilter()
	{
		var context = CreateContext();
		await context.CreateAsync(SecurityEventType.AuthenticationSucceeded, SecurityEventSeverity.Information, "Info before");
		var high = await context.CreateAsync(SecurityEventType.AuthenticationSucceededAfterFailures, SecurityEventSeverity.High, "High");
		var latest = await context.CreateAsync(SecurityEventType.SessionExpired, SecurityEventSeverity.Information, "Info after");
		var filter = new SecurityEventExportFilter { MinimumSeverity = SecurityEventSeverity.High };

		var batch = await context.Export.ReadBatchAsync(filter, null, 10, CancellationToken.None);

		Assert.False(batch.HasMore);
		Assert.Equal(new[] { high }, batch.Events.Select(value => value.EventId).ToArray());
		Assert.Equal(latest, batch.NextCheckpoint.LastEventId);
		var empty = await context.Export.ReadBatchAsync(filter, batch.NextCheckpoint, 10, CancellationToken.None);
		Assert.Empty(empty.Events);
		Assert.Equal(batch.NextCheckpoint, empty.NextCheckpoint);

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Export.ReadBatchAsync(
			new SecurityEventExportFilter { MinimumSeverity = SecurityEventSeverity.Information },
			batch.NextCheckpoint,
			10,
			CancellationToken.None));
	}

	[Fact]
	public async Task ReviewMutationDoesNotChangeImmutableExportRecord()
	{
		var context = CreateContext();
		var eventId = await context.CreateAsync(SecurityEventType.AuthenticationBlocked, SecurityEventSeverity.Critical, "Review me");
		var filter = new SecurityEventExportFilter();
		var before = await context.Export.ReadBatchAsync(filter, null, 10, CancellationToken.None);
		var original = Assert.Single(before.Events);

		Assert.True(await context.SecurityEvents.MarkReviewedAsync(eventId, 1, 1, DateTime.UtcNow, CancellationToken.None));
		var after = await context.Export.ReadBatchAsync(filter, null, 10, CancellationToken.None);
		var reviewed = Assert.Single(after.Events);

		Assert.Equal(original, reviewed);
	}

	[Fact]
	public void FilterFingerprintIsCanonicalAcrossOrderingAndDuplicates()
	{
		var first = new SecurityEventExportFilter
		{
			MinimumSeverity = SecurityEventSeverity.Warning,
			EventTypes = [SecurityEventType.SessionExpired, SecurityEventType.AuthenticationFailed, SecurityEventType.SessionExpired]
		};
		var second = new SecurityEventExportFilter
		{
			MinimumSeverity = SecurityEventSeverity.Warning,
			EventTypes = [SecurityEventType.AuthenticationFailed, SecurityEventType.SessionExpired]
		};

		Assert.Equal(
			SecurityEventExportService.ComputeFilterSha256(first),
			SecurityEventExportService.ComputeFilterSha256(second));
	}

	[Fact]
	public async Task InvalidCursorAndBatchSizeFailClosed()
	{
		var context = CreateContext();
		var filter = new SecurityEventExportFilter();
		var fingerprint = SecurityEventExportService.ComputeFilterSha256(filter);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.Export.ReadBatchAsync(filter, new SecurityEventExportCheckpoint(-1, fingerprint), 10, CancellationToken.None));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.Export.ReadBatchAsync(filter, null, SecurityEventExportService.MaximumBatchSize + 1, CancellationToken.None));
	}

	private TestContext CreateContext()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		SecurityEventSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var securityEvents = new SecurityEventRepository(access);
		var export = new SecurityEventExportService(new SecurityEventExportRepository(access));
		return new TestContext(securityEvents, export);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(SecurityEventRepository SecurityEvents, SecurityEventExportService Export)
	{
		public Task<long> CreateAsync(SecurityEventType type, SecurityEventSeverity severity, string summary) =>
			SecurityEvents.CreateAsync(new SecurityEvent
			{
				TimestampUtc = DateTime.UtcNow,
				EventType = type,
				Severity = severity,
				Summary = summary,
				Details = $"{summary} details"
			}, CancellationToken.None);
	}
}
