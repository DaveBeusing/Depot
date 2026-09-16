// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

internal sealed class SecurityEventDeliveryRepository : DatabaseRepository
{
	private const string TargetColumns = "Id,Code,SinkCode,EndpointUri,MinimumSeverity,EventTypesCsv,BatchSize,IsEnabled,CreatedUtc,UpdatedUtc,Version";
	private const string StateColumns = "TargetId,LastEventId,FilterSha256,PendingSnapshotUpperBoundId,PendingStartedUtc,ConsecutiveFailures,NextAttemptUtc,LastAttemptUtc,LastSuccessUtc,LastFailureKind,LastFailureCode,LastFailureMessage,IsSuspended,LeaseToken,LeaseUntilUtc,UpdatedUtc,Version";

	public SecurityEventDeliveryRepository(DatabaseAccess database) : base(database) { }

	public Task<SecurityEventExportTarget?> GetTargetAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {TargetColumns} FROM SecurityEventExportTargets WHERE Id=$Id;", ReadTarget, cancellationToken, Parameter("$Id", id));

	public Task<SecurityEventDeliveryState?> GetStateAsync(long targetId, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {StateColumns} FROM SecurityEventExportDeliveryState WHERE TargetId=$Id;", ReadState, cancellationToken, Parameter("$Id", targetId));

	public async Task<SecurityEventDeliveryStatus?> GetStatusAsync(long targetId, CancellationToken cancellationToken)
	{
		var target = await GetTargetAsync(targetId, cancellationToken);
		if (target is null) return null;
		var state = await GetStateAsync(targetId, cancellationToken) ?? throw new InvalidOperationException("Security-event export target has no delivery state.");
		return new SecurityEventDeliveryStatus(target, state);
	}

	public async Task<IReadOnlyList<SecurityEventDeliveryStatus>> ListStatusesAsync(CancellationToken cancellationToken)
	{
		var targets = await Database.QueryAsync($"SELECT {TargetColumns} FROM SecurityEventExportTargets ORDER BY Code;", ReadTarget, cancellationToken);
		var result = new List<SecurityEventDeliveryStatus>(targets.Count);
		foreach (var target in targets)
		{
			var state = await GetStateAsync(target.Id, cancellationToken) ?? throw new InvalidOperationException($"Security-event export target {target.Id} has no delivery state.");
			result.Add(new SecurityEventDeliveryStatus(target, state));
		}
		return result;
	}

	public async Task<IReadOnlyList<SecurityEventDeliveryStatus>> ListDueAsync(DateTime nowUtc, int count, CancellationToken cancellationToken)
	{
		if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
		var aliasedColumns = "t." + TargetColumns.Replace(",", ",t.", StringComparison.Ordinal);
		var targets = await Database.QuerySliceAsync(
			$"SELECT {aliasedColumns} FROM SecurityEventExportTargets t JOIN SecurityEventExportDeliveryState s ON s.TargetId=t.Id WHERE t.IsEnabled=1 AND s.IsSuspended=0 AND (s.NextAttemptUtc IS NULL OR s.NextAttemptUtc <= $Now) AND (s.LeaseUntilUtc IS NULL OR s.LeaseUntilUtc <= $Now) ORDER BY COALESCE(s.NextAttemptUtc,'1970-01-01T00:00:00.0000000Z'),t.Id",
			ReadTarget,
			0,
			count,
			cancellationToken,
			Parameter("$Now", Format(nowUtc)));
		var result = new List<SecurityEventDeliveryStatus>(targets.Count);
		foreach (var target in targets)
		{
			var state = await GetStateAsync(target.Id, cancellationToken);
			if (state is not null) result.Add(new SecurityEventDeliveryStatus(target, state));
		}
		return result;
	}

	public async Task<long?> GetRetentionCheckpointAsync(CancellationToken cancellationToken)
	{
		var value = await Database.ExecuteScalarAsync("SELECT MIN(s.LastEventId) FROM SecurityEventExportTargets t JOIN SecurityEventExportDeliveryState s ON s.TargetId=t.Id WHERE t.IsEnabled=1;", cancellationToken);
		return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
	}

	public static async Task<long> CreateAsync(DatabaseTransactionContext transaction, SecurityEventExportTarget target, string filterSha256, CancellationToken cancellationToken)
	{
		var id = await transaction.Session.InsertAsync(
			"INSERT INTO SecurityEventExportTargets (Code,SinkCode,EndpointUri,MinimumSeverity,EventTypesCsv,BatchSize,IsEnabled,CreatedUtc,UpdatedUtc,Version) VALUES ($Code,$Sink,$Endpoint,$Severity,$Types,$Batch,$Enabled,$Created,$Updated,1);",
			cancellationToken,
			TargetParameters(target));
		await transaction.Session.ExecuteAsync(
			"INSERT INTO SecurityEventExportDeliveryState (TargetId,LastEventId,FilterSha256,PendingSnapshotUpperBoundId,PendingStartedUtc,ConsecutiveFailures,NextAttemptUtc,LastAttemptUtc,LastSuccessUtc,LastFailureKind,LastFailureCode,LastFailureMessage,IsSuspended,LeaseToken,LeaseUntilUtc,UpdatedUtc,Version) VALUES ($Id,0,$Hash,NULL,NULL,0,NULL,NULL,NULL,NULL,NULL,NULL,0,NULL,NULL,$Updated,1);",
			cancellationToken,
			Parameter("$Id", id), Parameter("$Hash", filterSha256), Parameter("$Updated", Format(target.UpdatedUtc)));
		return id;
	}

	public static async Task<bool> UpdateTargetAsync(DatabaseTransactionContext transaction, SecurityEventExportTarget target, long expectedVersion, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE SecurityEventExportTargets SET SinkCode=$Sink,EndpointUri=$Endpoint,MinimumSeverity=$Severity,EventTypesCsv=$Types,BatchSize=$Batch,IsEnabled=$Enabled,UpdatedUtc=$Updated,Version=Version+1 WHERE Id=$Id AND Version=$Version;",
			cancellationToken,
			Parameter("$Sink", target.SinkCode), Parameter("$Endpoint", target.EndpointUri), Parameter("$Severity", (int)target.MinimumSeverity), Parameter("$Types", SerializeTypes(target.EventTypes)), Parameter("$Batch", target.BatchSize), Parameter("$Enabled", target.IsEnabled ? 1 : 0), Parameter("$Updated", Format(target.UpdatedUtc)), Parameter("$Id", target.Id), Parameter("$Version", expectedVersion)) == 1;

	public static async Task ResetStateAsync(DatabaseTransactionContext transaction, long targetId, string filterSha256, DateTime nowUtc, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync(
			"UPDATE SecurityEventExportDeliveryState SET LastEventId=0,FilterSha256=$Hash,PendingSnapshotUpperBoundId=NULL,PendingStartedUtc=NULL,ConsecutiveFailures=0,NextAttemptUtc=NULL,LastAttemptUtc=NULL,LastSuccessUtc=NULL,LastFailureKind=NULL,LastFailureCode=NULL,LastFailureMessage=NULL,IsSuspended=0,LeaseToken=NULL,LeaseUntilUtc=NULL,UpdatedUtc=$Updated,Version=Version+1 WHERE TargetId=$Id;",
			cancellationToken,
			Parameter("$Hash", filterSha256), Parameter("$Updated", Format(nowUtc)), Parameter("$Id", targetId));
	}

	public async Task<bool> TryAcquireLeaseAsync(long targetId, long expectedVersion, Guid leaseToken, DateTime nowUtc, DateTime leaseUntilUtc, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE SecurityEventExportDeliveryState SET LeaseToken=$Token,LeaseUntilUtc=$Until,UpdatedUtc=$Now,Version=Version+1 WHERE TargetId=$Id AND Version=$Version AND IsSuspended=0 AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= $Now);",
			cancellationToken,
			Parameter("$Token", leaseToken.ToString("D", CultureInfo.InvariantCulture)), Parameter("$Until", Format(leaseUntilUtc)), Parameter("$Now", Format(nowUtc)), Parameter("$Id", targetId), Parameter("$Version", expectedVersion)) == 1;

	public async Task<bool> SetPendingSnapshotAsync(long targetId, Guid leaseToken, long snapshotUpperBoundId, DateTime nowUtc, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE SecurityEventExportDeliveryState SET PendingSnapshotUpperBoundId=$Snapshot,PendingStartedUtc=COALESCE(PendingStartedUtc,$Now),LastAttemptUtc=$Now,UpdatedUtc=$Now,Version=Version+1 WHERE TargetId=$Id AND LeaseToken=$Token;",
			cancellationToken,
			Parameter("$Snapshot", snapshotUpperBoundId), Parameter("$Now", Format(nowUtc)), Parameter("$Id", targetId), Parameter("$Token", leaseToken.ToString("D", CultureInfo.InvariantCulture))) == 1;

	public async Task<bool> CompleteSuccessAsync(long targetId, Guid leaseToken, long checkpointEventId, DateTime nowUtc, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE SecurityEventExportDeliveryState SET LastEventId=$Checkpoint,PendingSnapshotUpperBoundId=NULL,PendingStartedUtc=NULL,ConsecutiveFailures=0,NextAttemptUtc=NULL,LastAttemptUtc=$Now,LastSuccessUtc=$Now,LastFailureKind=NULL,LastFailureCode=NULL,LastFailureMessage=NULL,IsSuspended=0,LeaseToken=NULL,LeaseUntilUtc=NULL,UpdatedUtc=$Now,Version=Version+1 WHERE TargetId=$Id AND LeaseToken=$Token;",
			cancellationToken,
			Parameter("$Checkpoint", checkpointEventId), Parameter("$Now", Format(nowUtc)), Parameter("$Id", targetId), Parameter("$Token", leaseToken.ToString("D", CultureInfo.InvariantCulture))) == 1;

	public async Task<bool> CompleteFailureAsync(long targetId, Guid leaseToken, int failures, SecurityEventDeliveryFailureKind kind, string code, string message, DateTime nowUtc, DateTime? nextAttemptUtc, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE SecurityEventExportDeliveryState SET ConsecutiveFailures=$Failures,NextAttemptUtc=$Next,LastAttemptUtc=$Now,LastFailureKind=$Kind,LastFailureCode=$Code,LastFailureMessage=$Message,IsSuspended=$Suspended,LeaseToken=NULL,LeaseUntilUtc=NULL,UpdatedUtc=$Now,Version=Version+1 WHERE TargetId=$Id AND LeaseToken=$Token;",
			cancellationToken,
			Parameter("$Failures", failures), Parameter("$Next", nextAttemptUtc is null ? null : Format(nextAttemptUtc.Value)), Parameter("$Now", Format(nowUtc)), Parameter("$Kind", (int)kind), Parameter("$Code", code), Parameter("$Message", Trim(message, 1000)), Parameter("$Suspended", kind == SecurityEventDeliveryFailureKind.Permanent ? 1 : 0), Parameter("$Id", targetId), Parameter("$Token", leaseToken.ToString("D", CultureInfo.InvariantCulture))) == 1;

	public async Task ResumeAsync(long targetId, DateTime nowUtc, CancellationToken cancellationToken)
	{
		await Database.ExecuteAsync(
			"UPDATE SecurityEventExportDeliveryState SET IsSuspended=0,NextAttemptUtc=NULL,ConsecutiveFailures=0,LastFailureKind=NULL,LastFailureCode=NULL,LastFailureMessage=NULL,LeaseToken=NULL,LeaseUntilUtc=NULL,UpdatedUtc=$Now,Version=Version+1 WHERE TargetId=$Id;",
			cancellationToken,
			Parameter("$Now", Format(nowUtc)), Parameter("$Id", targetId));
	}

	private static DatabaseParameter[] TargetParameters(SecurityEventExportTarget target) =>
	[
		Parameter("$Code", target.Code), Parameter("$Sink", target.SinkCode), Parameter("$Endpoint", target.EndpointUri), Parameter("$Severity", (int)target.MinimumSeverity), Parameter("$Types", SerializeTypes(target.EventTypes)), Parameter("$Batch", target.BatchSize), Parameter("$Enabled", target.IsEnabled ? 1 : 0), Parameter("$Created", Format(target.CreatedUtc)), Parameter("$Updated", Format(target.UpdatedUtc))
	];

	private static SecurityEventExportTarget ReadTarget(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), Code = reader.GetString(1), SinkCode = reader.GetString(2), EndpointUri = reader.GetString(3), MinimumSeverity = (SecurityEventSeverity)reader.GetInt32(4), EventTypes = ParseTypes(reader.GetString(5)), BatchSize = reader.GetInt32(6), IsEnabled = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture) != 0, CreatedUtc = ReadUtc(reader, 8), UpdatedUtc = ReadUtc(reader, 9), Version = reader.GetInt64(10)
	};

	private static SecurityEventDeliveryState ReadState(DbDataReader reader) => new()
	{
		TargetId = reader.GetInt64(0), LastEventId = reader.GetInt64(1), FilterSha256 = reader.GetString(2), PendingSnapshotUpperBoundId = NullableInt64(reader, 3), PendingStartedUtc = ReadNullableUtc(reader, 4), ConsecutiveFailures = reader.GetInt32(5), NextAttemptUtc = ReadNullableUtc(reader, 6), LastAttemptUtc = ReadNullableUtc(reader, 7), LastSuccessUtc = ReadNullableUtc(reader, 8), LastFailureKind = reader.IsDBNull(9) ? null : (SecurityEventDeliveryFailureKind)reader.GetInt32(9), LastFailureCode = NullableString(reader, 10), LastFailureMessage = NullableString(reader, 11), IsSuspended = Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture) != 0, LeaseToken = NullableGuid(reader, 13), LeaseUntilUtc = ReadNullableUtc(reader, 14), UpdatedUtc = ReadUtc(reader, 15), Version = reader.GetInt64(16)
	};

	private static string SerializeTypes(IReadOnlyList<SecurityEventType> values) => string.Join(',', values.Distinct().OrderBy(value => (int)value).Select(value => ((int)value).ToString(CultureInfo.InvariantCulture)));
	private static IReadOnlyList<SecurityEventType> ParseTypes(string value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(part => (SecurityEventType)int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ReadUtc(DbDataReader reader, int ordinal) => DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static DateTime? ReadNullableUtc(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);
	private static long? NullableInt64(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
	private static Guid? NullableGuid(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
	private static string Trim(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
