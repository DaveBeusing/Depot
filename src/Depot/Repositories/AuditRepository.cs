// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class AuditRepository : DatabaseRepository
{
	public AuditRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public long Create(AuditEntry entry)
	{
		return Database.Insert(
		"""
		INSERT INTO AuditEntries
		(TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson)
		VALUES
		($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);
		""",
		Parameter("$TimestampUtc", entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
		Parameter("$UserId", entry.UserId),
		Parameter("$UserEmail", entry.UserEmail),
		Parameter("$EntityType", entry.EntityType),
		Parameter("$EntityId", entry.EntityId),
		Parameter("$Action", entry.Action),
		Parameter("$BeforeJson", entry.BeforeJson),
		Parameter("$AfterJson", entry.AfterJson));
	}
}
