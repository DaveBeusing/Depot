// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

internal sealed class SecurityEventExportRepository : DatabaseRepository
{
	private const string ExportColumns = "Id, TimestampUtc, EventType, Severity, UserId, AccountIdentifier, SessionId, ClientInstanceId, MachineName, Summary, Details";

	public SecurityEventExportRepository(DatabaseAccess database) : base(database)
	{
	}

	public async Task<long> GetLatestIdAsync(CancellationToken cancellationToken)
	{
		var rows = await Database.QuerySliceAsync(
			"SELECT Id FROM SecurityEvents ORDER BY Id DESC",
			reader => reader.GetInt64(0),
			0,
			1,
			cancellationToken);
		return rows.Count == 0 ? 0 : rows[0];
	}

	public Task<IReadOnlyList<SecurityEventExportRecord>> ReadSliceAsync(
		long afterId,
		long upperBoundId,
		SecurityEventExportFilter filter,
		int count,
		CancellationToken cancellationToken)
	{
		if (afterId < 0) throw new ArgumentOutOfRangeException(nameof(afterId));
		if (upperBoundId < 0) throw new ArgumentOutOfRangeException(nameof(upperBoundId));
		if (count is < 1 or > 501) throw new ArgumentOutOfRangeException(nameof(count));
		ArgumentNullException.ThrowIfNull(filter);

		var predicates = new List<string>
		{
			"Id > $AfterId",
			"Id <= $UpperBoundId",
			"Severity >= $MinimumSeverity"
		};
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$AfterId", afterId),
			Parameter("$UpperBoundId", upperBoundId),
			Parameter("$MinimumSeverity", (int)filter.MinimumSeverity)
		};

		if (filter.EventTypes.Count > 0)
		{
			var names = new List<string>(filter.EventTypes.Count);
			for (var index = 0; index < filter.EventTypes.Count; index++)
			{
				var name = $"$EventType{index}";
				names.Add(name);
				parameters.Add(Parameter(name, (int)filter.EventTypes[index]));
			}
			predicates.Add($"EventType IN ({string.Join(",", names)})");
		}

		return Database.QuerySliceAsync(
			$"SELECT {ExportColumns} FROM SecurityEvents WHERE {string.Join(" AND ", predicates)} ORDER BY Id ASC",
			ReadExportRecord,
			0,
			count,
			cancellationToken,
			parameters.ToArray());
	}

	private static SecurityEventExportRecord ReadExportRecord(DbDataReader reader) => new(
		reader.GetInt64(0),
		ReadUtc(reader, 1),
		(SecurityEventType)reader.GetInt32(2),
		(SecurityEventSeverity)reader.GetInt32(3),
		NullableInt64(reader, 4),
		NullableString(reader, 5),
		NullableGuid(reader, 6),
		NullableGuid(reader, 7),
		NullableString(reader, 8),
		reader.GetString(9),
		NullableString(reader, 10));

	private static DateTime ReadUtc(DbDataReader reader, int ordinal) => DateTime.Parse(
		Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty,
		CultureInfo.InvariantCulture,
		DateTimeStyles.RoundtripKind).ToUniversalTime();

	private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
	private static long? NullableInt64(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
	private static Guid? NullableGuid(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
}
