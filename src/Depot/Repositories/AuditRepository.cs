// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class AuditRepository : DatabaseRepository
{
	private const string InsertSql =
		"""
		INSERT INTO AuditEntries
		(TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson)
		VALUES
		($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);
		""";

	public AuditRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<long> CreateAsync(AuditEntry entry, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			InsertSql,
			cancellationToken,
			Parameters(entry));

	internal static Task<int> CreateAsync(
		DatabaseSession session,
		AuditEntry entry,
		CancellationToken cancellationToken) =>
		session.ExecuteAsync(InsertSql, cancellationToken, Parameters(entry));

	public long Create(AuditEntry entry)
	{
		return Database.Insert(
			InsertSql,
			Parameters(entry));
	}

	private static DatabaseParameter[] Parameters(AuditEntry entry) =>
	[
		Parameter("$TimestampUtc", entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
		Parameter("$UserId", entry.UserId),
		Parameter("$UserEmail", entry.UserEmail),
		Parameter("$EntityType", entry.EntityType),
		Parameter("$EntityId", entry.EntityId),
		Parameter("$Action", entry.Action),
		Parameter("$BeforeJson", entry.BeforeJson),
		Parameter("$AfterJson", entry.AfterJson)
	];
}
