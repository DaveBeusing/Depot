// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

internal interface ISecurityEventExportSource
{
	Task<SecurityEventExportBatch> ReadBatchAsync(
		SecurityEventExportFilter filter,
		SecurityEventExportCheckpoint? checkpoint,
		int batchSize,
		CancellationToken cancellationToken);
}

internal interface ISecurityEventExportSink
{
	string SinkCode { get; }
	Task WriteAsync(SecurityEventExportBatch batch, CancellationToken cancellationToken);
}

internal sealed class SecurityEventExportService : ISecurityEventExportSource
{
	public const int MaximumBatchSize = 500;
	private readonly SecurityEventExportRepository _repository;

	public SecurityEventExportService(SecurityEventExportRepository repository)
	{
		_repository = repository;
	}

	public Task<SecurityEventExportBatch> ReadBatchAsync(
		SecurityEventExportFilter filter,
		SecurityEventExportCheckpoint? checkpoint,
		int batchSize,
		CancellationToken cancellationToken) =>
		ReadBatchAsync(filter, checkpoint, batchSize, null, cancellationToken);

	internal async Task<SecurityEventExportBatch> ReadBatchAsync(
		SecurityEventExportFilter filter,
		SecurityEventExportCheckpoint? checkpoint,
		int batchSize,
		long? fixedSnapshotUpperBoundId,
		CancellationToken cancellationToken)
	{
		if (batchSize is < 1 or > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
		var normalized = NormalizeFilter(filter);
		var fingerprint = ComputeFilterSha256(normalized);
		var start = checkpoint ?? new SecurityEventExportCheckpoint(0, fingerprint);
		ValidateCheckpoint(start, fingerprint);

		var latestId = await _repository.GetLatestIdAsync(cancellationToken);
		var snapshotUpperBoundId = fixedSnapshotUpperBoundId ?? latestId;
		if (snapshotUpperBoundId < 0 || snapshotUpperBoundId > latestId)
			throw new InvalidOperationException("Security-event export snapshot upper bound is outside the available source range.");
		if (snapshotUpperBoundId <= start.LastEventId)
			return new SecurityEventExportBatch(
				SecurityEventExportBatch.CurrentFormatVersion,
				snapshotUpperBoundId,
				start,
				start,
				[],
				false);

		var slice = await _repository.ReadSliceAsync(
			start.LastEventId,
			snapshotUpperBoundId,
			normalized,
			batchSize + 1,
			cancellationToken);
		var hasMore = slice.Count > batchSize;
		var events = hasMore ? slice.Take(batchSize).ToArray() : slice.ToArray();
		var nextEventId = hasMore
			? events[^1].EventId
			: snapshotUpperBoundId;
		var next = new SecurityEventExportCheckpoint(nextEventId, fingerprint);

		return new SecurityEventExportBatch(
			SecurityEventExportBatch.CurrentFormatVersion,
			snapshotUpperBoundId,
			start,
			next,
			events,
			hasMore);
	}

	internal static SecurityEventExportFilter NormalizeFilter(SecurityEventExportFilter filter)
	{
		ArgumentNullException.ThrowIfNull(filter);
		if (!Enum.IsDefined(filter.MinimumSeverity))
			throw new ArgumentOutOfRangeException(nameof(filter), "A supported minimum security-event severity is required.");

		var eventTypes = (filter.EventTypes ?? [])
			.Distinct()
			.OrderBy(value => (int)value)
			.ToArray();
		if (eventTypes.Any(value => !Enum.IsDefined(value)))
			throw new ArgumentOutOfRangeException(nameof(filter), "All security-event export types must be supported values.");
		return new SecurityEventExportFilter
		{
			MinimumSeverity = filter.MinimumSeverity,
			EventTypes = eventTypes
		};
	}

	internal static string ComputeFilterSha256(SecurityEventExportFilter filter)
	{
		var normalized = NormalizeFilter(filter);
		var material = $"v1|min={(int)normalized.MinimumSeverity}|types={string.Join(",", normalized.EventTypes.Select(value => (int)value))}";
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
	}

	private static void ValidateCheckpoint(SecurityEventExportCheckpoint checkpoint, string expectedFilterSha256)
	{
		if (checkpoint.LastEventId < 0) throw new ArgumentOutOfRangeException(nameof(checkpoint), "Security-event export checkpoint cannot be negative.");
		if (!string.Equals(checkpoint.FilterSha256, expectedFilterSha256, StringComparison.Ordinal))
			throw new InvalidOperationException("Security-event export checkpoint does not belong to the requested filter contract.");
	}
}
