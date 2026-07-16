// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class AuditRepository
{
	private readonly IDatabaseConnectionFactory _connectionFactory;

	public AuditRepository(IDatabaseConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public long Create(AuditEntry entry)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		INSERT INTO AuditEntries
		(TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson)
		VALUES
		($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);
		SELECT last_insert_rowid();
		""";
		command.Parameters.AddWithValue("$TimestampUtc", entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
		command.Parameters.AddWithValue("$UserId", (object?)entry.UserId ?? DBNull.Value);
		command.Parameters.AddWithValue("$UserEmail", entry.UserEmail);
		command.Parameters.AddWithValue("$EntityType", entry.EntityType);
		command.Parameters.AddWithValue("$EntityId", entry.EntityId);
		command.Parameters.AddWithValue("$Action", entry.Action);
		command.Parameters.AddWithValue("$BeforeJson", (object?)entry.BeforeJson ?? DBNull.Value);
		command.Parameters.AddWithValue("$AfterJson", (object?)entry.AfterJson ?? DBNull.Value);
		return (long)command.ExecuteScalar()!;
	}
}
