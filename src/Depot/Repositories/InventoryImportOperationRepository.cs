// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

namespace Depot.Repositories;

internal static class InventoryImportOperationRepository
{
	internal static async Task<bool> IsCompletedAsync(DatabaseTransactionContext transaction, Guid operationId, string requestHash, CancellationToken cancellationToken)
	{
		var parameters = new[] { new DatabaseParameter("$OperationId", operationId.ToString("D")) };
		// The unique operation key participates in the provider's serializable write transaction.
		await transaction.Session.ExecuteAsync("UPDATE WorkflowOperations SET Workflow = Workflow WHERE OperationId = $OperationId;", cancellationToken, parameters);
		var completed = await transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Workflow, EntityId FROM WorkflowOperations WHERE OperationId = $OperationId;",
			reader => new { Workflow = reader.GetString(0), InventoryId = reader.GetInt64(1) }, cancellationToken, parameters);
		if (completed is null) return false;
		if (!string.Equals(completed.Workflow, Workflow(requestHash), StringComparison.Ordinal) || completed.InventoryId <= 0)
			throw new InvalidOperationException("This import operation ID is already assigned to different content.");
		return true;
	}

	internal static Task<int> CompleteAsync(DatabaseTransactionContext transaction, Guid operationId, string requestHash, long firstInventoryId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"INSERT INTO WorkflowOperations (OperationId, Workflow, EntityId, CompletedAtUtc) VALUES ($OperationId, $Workflow, $InventoryId, $CompletedAtUtc);",
			cancellationToken,
			new DatabaseParameter("$OperationId", operationId.ToString("D")),
			new DatabaseParameter("$Workflow", Workflow(requestHash)),
			new DatabaseParameter("$InventoryId", firstInventoryId),
			new DatabaseParameter("$CompletedAtUtc", DateTime.UtcNow));

	private static string Workflow(string requestHash) => "Inventory.Import.v1:" + requestHash;
}
