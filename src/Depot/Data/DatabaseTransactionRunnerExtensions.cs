// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class DatabaseTransactionRunnerExtensions
{
	internal static async Task ExecuteAsync(
		this IDatabaseTransactionRunner runner,
		Func<DatabaseTransactionContext, CancellationToken, Task> action,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(runner);
		ArgumentNullException.ThrowIfNull(action);
		await runner.ExecuteAsync(async (transaction, token) =>
		{
			await action(transaction, token);
			return true;
		}, cancellationToken);
	}
}
