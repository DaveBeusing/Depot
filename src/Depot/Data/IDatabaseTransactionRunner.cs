// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

public interface IDatabaseTransactionRunner
{
	Task<T> ExecuteAsync<T>(
		Func<DatabaseTransactionContext, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken = default);
}

public sealed class DatabaseTransactionRunner : IDatabaseTransactionRunner
{
	private readonly DatabaseAccess _database;

	public DatabaseTransactionRunner(DatabaseAccess database)
	{
		_database = database;
	}

	public Task<T> ExecuteAsync<T>(
		Func<DatabaseTransactionContext, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken = default) =>
		_database.ExecuteInWriteTransactionAsync(
			(session, token) => operation(new DatabaseTransactionContext(session), token),
			cancellationToken);
}

public sealed class DatabaseTransactionContext
{
	internal DatabaseTransactionContext(DatabaseSession session)
	{
		Session = session;
	}

	internal DatabaseSession Session { get; }
}
