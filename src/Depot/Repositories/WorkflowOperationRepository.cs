// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public static class WorkflowOperationRepository
{
	public static async Task<bool> IsCompletedAsync(
		DatabaseSession session,
		WorkflowOperation operation,
		CancellationToken cancellationToken)
	{
		Validate(operation);
		var existing = await session.QuerySingleOrDefaultAsync(
			"SELECT Workflow, EntityId FROM WorkflowOperations WHERE OperationId = $OperationId;",
			reader => new { Workflow = reader.GetString(0), EntityId = reader.GetInt64(1) },
			cancellationToken,
			new DatabaseParameter("$OperationId", operation.OperationId.ToString("D")));
		if (existing is null) return false;
		if (!string.Equals(existing.Workflow, operation.Workflow, StringComparison.Ordinal) || existing.EntityId != operation.EntityId)
			throw new InvalidOperationException("The operation ID is already assigned to a different workflow operation.");
		return true;
	}

	public static Task<int> CompleteAsync(
		DatabaseSession session,
		WorkflowOperation operation,
		CancellationToken cancellationToken)
	{
		Validate(operation);
		return session.ExecuteAsync(
			"INSERT INTO WorkflowOperations (OperationId, Workflow, EntityId, CompletedAtUtc) VALUES ($OperationId, $Workflow, $EntityId, $CompletedAtUtc);",
			cancellationToken,
			new DatabaseParameter("$OperationId", operation.OperationId.ToString("D")),
			new DatabaseParameter("$Workflow", operation.Workflow),
			new DatabaseParameter("$EntityId", operation.EntityId),
			new DatabaseParameter("$CompletedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
	}

	private static void Validate(WorkflowOperation operation)
	{
		if (operation.OperationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operation));
		if (string.IsNullOrWhiteSpace(operation.Workflow)) throw new ArgumentException("A workflow name is required.", nameof(operation));
		if (operation.EntityId <= 0) throw new ArgumentException("A workflow entity ID is required.", nameof(operation));
	}
}
