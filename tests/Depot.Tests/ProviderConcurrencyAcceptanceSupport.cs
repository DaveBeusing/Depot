// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Runtime.ExceptionServices;

using Depot.Data;
using Depot.Models;

using Xunit;

namespace Depot.Tests;

internal sealed class ProviderTransactionGate(IDatabaseTransactionRunner inner) : IDatabaseTransactionRunner
{
	private int _armed;
	private Exception? _nextFailure;
	private TaskCompletionSource _beforeCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Task BeforeCommit => _beforeCommit.Task;

	public void Arm()
	{
		_beforeCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Interlocked.Exchange(ref _armed, 1);
	}

	public void Release() => _release.TrySetResult();

	public void FailNextCommit(Exception failure) => Interlocked.Exchange(ref _nextFailure, failure);

	public Task<T> ExecuteAsync<T>(Func<DatabaseTransactionContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
		inner.ExecuteAsync(async (transaction, token) =>
		{
			var result = await operation(transaction, token);
			if (Interlocked.Exchange(ref _armed, 0) == 1)
			{
				_beforeCommit.TrySetResult();
				await _release.Task.WaitAsync(token);
			}
			var failure = Interlocked.Exchange(ref _nextFailure, null);
			if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
			return result;
		}, cancellationToken);
}

internal sealed class ObservedProviderConnectionFactory(IDatabaseConnectionFactory inner) : IDatabaseConnectionFactory
{
	private int _observeNext;
	private TaskCompletionSource _attempted = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public DatabaseProvider Provider => inner.Provider;
	public Task WriteTransactionAttempted => _attempted.Task;

	public void ObserveNextWriteTransaction()
	{
		_attempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Interlocked.Exchange(ref _observeNext, 1);
	}

	public DbConnection CreateConnection() => inner.CreateConnection();
	public DbTransaction BeginWriteTransaction(DbConnection connection)
	{
		ObserveAttempt();
		return inner.BeginWriteTransaction(connection);
	}
	public ValueTask<DbTransaction> BeginWriteTransactionAsync(DbConnection connection, CancellationToken cancellationToken)
	{
		ObserveAttempt();
		return inner.BeginWriteTransactionAsync(connection, cancellationToken);
	}

	private void ObserveAttempt()
	{
		// This observes entry into a real write transaction, not arrival at a specific row lock.
		if (Interlocked.Exchange(ref _observeNext, 0) == 1) _attempted.TrySetResult();
	}

	public string GetInventoryLockSql() => inner.GetInventoryLockSql();
	public string GetInventoryBatchLockSql(string parameterList) => inner.GetInventoryBatchLockSql(parameterList);
	public string GetPurchaseOrderLockSql() => inner.GetPurchaseOrderLockSql();
	public string GetStockTransferLockSql() => inner.GetStockTransferLockSql();
	public string GetInventoryCountLockSql() => inner.GetInventoryCountLockSql();
	public string GetInventoryCountInventoryLockSql() => inner.GetInventoryCountInventoryLockSql();
	public string GetMaterialIssueLockSql() => inner.GetMaterialIssueLockSql();
	public string GetMaterialReturnLockSql() => inner.GetMaterialReturnLockSql();
	public string GetSupplierReturnLockSql() => inner.GetSupplierReturnLockSql();
	public string GetPagingClause() => inner.GetPagingClause();
	public string CastToInt64(string expression) => inner.CastToInt64(expression);
}

internal static class ProviderWriteOverlap
{
	public static async Task<Exception?> RunAsync(
		ObservedProviderConnectionFactory factory,
		ProviderTransactionGate gate,
		Func<Task> firstOperation,
		Func<Task> secondOperation)
	{
		gate.Arm();
		var first = Task.Run(firstOperation);
		Task? second = null;
		Exception? secondFailure = null;
		try
		{
			var boundary = await Task.WhenAny(gate.BeforeCommit, first).WaitAsync(TimeSpan.FromSeconds(30));
			if (boundary == first)
			{
				await first;
				throw new InvalidOperationException("The first operation finished without reaching the commit barrier.");
			}
			factory.ObserveNextWriteTransaction();
			second = Task.Run(secondOperation);
			await Task.WhenAny(factory.WriteTransactionAttempted, second).WaitAsync(TimeSpan.FromSeconds(30));
			Assert.True(factory.WriteTransactionAttempted.IsCompleted, "The competing operation must attempt a real write transaction.");
			Assert.False(first.IsCompleted, "The first transaction must remain uncommitted when the competing write starts.");
		}
		finally
		{
			gate.Release();
			try
			{
				await first;
			}
			finally
			{
				// Drain both tasks even if setup, an assertion or a timeout already failed.
				var activeSecond = second;
				if (activeSecond is not null) secondFailure = await Record.ExceptionAsync(() => activeSecond);
			}
		}
		return secondFailure;
	}
}
