// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class SalesReturnIntegrityProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqliteSerializesReturnsAndShipmentReversal()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-return-integrity-{Guid.NewGuid():N}.db");
		try
		{
			await VerifyAsync(new SqliteConnectionFactory(path));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
				if (File.Exists(path + suffix)) File.Delete(path + suffix);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerSerializesReturnsAndShipmentReversal() => VerifyAsync(
		new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbSerializesReturnsAndShipmentReversal() => VerifyAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlSerializesReturnsAndShipmentReversal() => VerifyAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory innerFactory)
	{
		DatabaseProvisioningService.Initialize(innerFactory);
		var factory = new ObservedConnectionFactory(innerFactory);
		CommitGateRunner? gate = null;
		var fixture = await SalesProviderAcceptanceTests.SalesFixture.CreateAsync(factory,
			data => gate = new CommitGateRunner(new DatabaseTransactionRunner(data)));
		var transactions = gate ?? throw new InvalidOperationException("The fixture did not configure its transaction runner.");

		await VerifyStaleQuantityAsync(fixture);
		await VerifyContendedReturnsAsync(fixture, factory, transactions);
		await VerifyReturnBeforeReversalAsync(fixture, factory, transactions);
		await VerifyReversalBeforeReturnAsync(fixture, factory, transactions);
	}

	private static async Task VerifyStaleQuantityAsync(SalesProviderAcceptanceTests.SalesFixture fixture)
	{
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var first = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "First partial return");
		var stale = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Competing full return");
		await fixture.Database.ExecuteAsync("UPDATE CustomerReturnLines SET Quantity=2 WHERE CustomerReturnId=$Id;",
			CancellationToken.None, new DatabaseParameter("$Id", first.Id));
		await fixture.Shipments.PostCustomerReturnAsync(first.Id, first.Version);
		Assert.Equal(17L, await fixture.CurrentStockAsync());
		var before = await SnapshotAsync(fixture, shipment.Id, stale.Id);

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.PostCustomerReturnAsync(stale.Id, stale.Version));
		Assert.Equal(before, await SnapshotAsync(fixture, shipment.Id, stale.Id));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Already partly returned"));
		Assert.Equal(before, await SnapshotAsync(fixture, shipment.Id, stale.Id));

		var remainder = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Remaining quantity");
		Assert.Equal(3, Assert.Single(remainder.Lines).Quantity);
		await fixture.Shipments.PostCustomerReturnAsync(remainder.Id, remainder.Version);
		Assert.Equal(20L, await fixture.CurrentStockAsync());
	}

	private static async Task VerifyContendedReturnsAsync(SalesProviderAcceptanceTests.SalesFixture fixture, ObservedConnectionFactory factory, CommitGateRunner gate)
	{
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var first = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "First overlapping return");
		var second = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Second overlapping return");
		var before = await SnapshotAsync(fixture, shipment.Id, second.Id);
		await RunOverlappingAsync(factory, gate,
			() => fixture.Shipments.PostCustomerReturnAsync(first.Id, first.Version),
			() => fixture.Shipments.PostCustomerReturnAsync(second.Id, second.Version));
		var after = await SnapshotAsync(fixture, shipment.Id, second.Id);

		Assert.Equal(20L, after.Stock);
		Assert.Equal(before.Movements + 1, after.Movements);
		Assert.Equal(before.ReturnVersion, after.ReturnVersion);
		Assert.Equal(CustomerReturnStatus.Draft, after.ReturnStatus);
		Assert.Equal(before.ShipmentVersion, after.ShipmentVersion);
		Assert.Equal(ShipmentStatus.Posted, after.ShipmentStatus);
		Assert.Equal(before.OrderVersion, after.OrderVersion);
		Assert.Equal(before.LineVersion, after.LineVersion);
		Assert.Equal(0, after.ReservedQuantity);
		Assert.Equal(5, after.ShippedQuantity);
		Assert.Equal(0, after.ActiveReservationQuantity);
		var postedReturn = await fixture.Shipments.GetCustomerReturnByIdAsync(first.Id) ?? throw new InvalidOperationException("Posted return missing.");
		Assert.Equal(CustomerReturnStatus.Posted, postedReturn.Status);
		Assert.Equal(1L, await ScalarAsync(fixture, "SELECT COUNT(*) FROM CustomerReturns WHERE ShipmentId=$Id AND Status=2;", shipment.Id));
		Assert.Equal(before.Journals, after.Journals);
		Assert.Equal(before.Tracking, after.Tracking);
		Assert.Equal(before.Audits + 1, after.Audits);
	}

	private static async Task VerifyReturnBeforeReversalAsync(SalesProviderAcceptanceTests.SalesFixture fixture, ObservedConnectionFactory factory, CommitGateRunner gate)
	{
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var value = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Return wins");
		var before = await SnapshotAsync(fixture, shipment.Id, value.Id);
		await RunOverlappingAsync(factory, gate,
			() => fixture.Shipments.PostCustomerReturnAsync(value.Id, value.Version),
			() => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Competing reversal"));
		var after = await SnapshotAsync(fixture, shipment.Id, value.Id);

		Assert.Equal(20L, after.Stock);
		Assert.Equal(before.Movements + 1, after.Movements);
		Assert.Equal(before.ShipmentVersion, after.ShipmentVersion);
		Assert.Equal(ShipmentStatus.Posted, after.ShipmentStatus);
		Assert.Equal(before.OrderVersion, after.OrderVersion);
		Assert.Equal(before.LineVersion, after.LineVersion);
		var retainedShipment = await fixture.Shipments.GetByIdAsync(shipment.Id) ?? throw new InvalidOperationException("Shipment missing.");
		Assert.Null(retainedShipment.ReversedAtUtc);
		Assert.Equal(CustomerReturnStatus.Posted, after.ReturnStatus);
		Assert.Equal(0, after.ActiveReservationQuantity);
		Assert.Equal(0, after.ReservedQuantity);
		Assert.Equal(5, after.ShippedQuantity);
		Assert.Equal(before.Journals, after.Journals);
		Assert.Equal(before.Tracking, after.Tracking);
		Assert.Equal(before.Audits + 1, after.Audits);
	}

	private static async Task VerifyReversalBeforeReturnAsync(SalesProviderAcceptanceTests.SalesFixture fixture, ObservedConnectionFactory factory, CommitGateRunner gate)
	{
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var value = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Stale after reversal");
		var before = await SnapshotAsync(fixture, shipment.Id, value.Id);
		await RunOverlappingAsync(factory, gate,
			() => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Reversal wins"),
			() => fixture.Shipments.PostCustomerReturnAsync(value.Id, value.Version));
		var after = await SnapshotAsync(fixture, shipment.Id, value.Id);

		Assert.Equal(20L, after.Stock);
		Assert.Equal(before.Movements + 1, after.Movements);
		Assert.Equal(before.ReturnVersion, after.ReturnVersion);
		Assert.Equal(CustomerReturnStatus.Draft, after.ReturnStatus);
		Assert.Equal(ShipmentStatus.Cancelled, after.ShipmentStatus);
		Assert.Equal(SalesOrderStatus.Released, after.OrderStatus);
		var reversedShipment = await fixture.Shipments.GetByIdAsync(shipment.Id) ?? throw new InvalidOperationException("Shipment missing.");
		Assert.NotNull(reversedShipment.ReversedAtUtc);
		Assert.Equal(5, after.ActiveReservationQuantity);
		Assert.Equal(5, after.ReservedQuantity);
		Assert.Equal(0, after.ShippedQuantity);
		Assert.Equal(0L, await ScalarAsync(fixture, "SELECT COUNT(*) FROM CustomerReturns WHERE ShipmentId=$Id AND Status=2;", shipment.Id));
		Assert.Equal(before.Journals, after.Journals);
		Assert.Equal(before.Tracking, after.Tracking);
		Assert.Equal(before.Audits + 1, after.Audits);
	}

	private static async Task RunOverlappingAsync(ObservedConnectionFactory factory, CommitGateRunner gate, Func<Task> firstOperation, Func<Task> secondOperation)
	{
		gate.Arm();
		var first = Task.Run(firstOperation);
		Task? second = null;
		Exception? competingFailure = null;
		try
		{
			await gate.BeforeCommit.WaitAsync(TimeSpan.FromSeconds(30));
			factory.ObserveNextWriteTransaction();
			second = Task.Run(secondOperation);
			await factory.WriteTransactionAttempted.WaitAsync(TimeSpan.FromSeconds(30));
			Assert.False(first.IsCompleted, "The winning transaction must still be uncommitted when the competing write starts.");
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
				// Both operations must finish before another case can use the shared provider database,
				// including when an overlap assertion or timeout has already failed.
				var activeSecond = second;
				if (activeSecond is not null) competingFailure = await Record.ExceptionAsync(() => activeSecond);
			}
		}
		Assert.IsType<InvalidOperationException>(competingFailure);
	}

	private static async Task<CorrectionSnapshot> SnapshotAsync(SalesProviderAcceptanceTests.SalesFixture fixture, long shipmentId, long returnId)
	{
		var shipment = await fixture.Shipments.GetByIdAsync(shipmentId) ?? throw new InvalidOperationException("Shipment missing.");
		var customerReturn = await fixture.Shipments.GetCustomerReturnByIdAsync(returnId) ?? throw new InvalidOperationException("Return missing.");
		var order = await fixture.Orders.GetByIdAsync(shipment.SalesOrderId) ?? throw new InvalidOperationException("Order missing.");
		var line = Assert.Single(order.Lines);
		var reservations = await fixture.Orders.GetReservationsAsync(order.Id);
		return new CorrectionSnapshot(await fixture.CurrentStockAsync(),
			await ScalarAsync(fixture, "SELECT COUNT(*) FROM StockMovements WHERE InventoryId=$Id;", fixture.InventoryId),
			customerReturn.Version, customerReturn.Status, shipment.Version, shipment.Status, order.Version, order.Status, line.Version,
			line.ReservedQuantity, line.ShippedQuantity, reservations.Where(value => value.Status == InventoryReservationStatus.Active).Sum(value => value.Quantity),
			await ScalarAsync(fixture, "SELECT COUNT(*) FROM AuditEntries;"),
			await ScalarAsync(fixture, "SELECT COUNT(*) FROM FinanceJournalEntries;"),
			await ScalarAsync(fixture, "SELECT COUNT(*) FROM StockMovementTracking;"));
	}

	private static async Task<long> ScalarAsync(SalesProviderAcceptanceTests.SalesFixture fixture, string sql, long? id = null) =>
		Convert.ToInt64(await fixture.Database.ExecuteScalarAsync(sql, CancellationToken.None,
			id.HasValue ? [new DatabaseParameter("$Id", id.Value)] : []));

	private sealed record CorrectionSnapshot(long Stock, long Movements, long ReturnVersion, CustomerReturnStatus ReturnStatus,
		long ShipmentVersion, ShipmentStatus ShipmentStatus, long OrderVersion, SalesOrderStatus OrderStatus, long LineVersion,
		int ReservedQuantity, int ShippedQuantity, int ActiveReservationQuantity,
		long Audits, long Journals, long Tracking);

	private sealed class CommitGateRunner(IDatabaseTransactionRunner inner) : IDatabaseTransactionRunner
	{
		private int _armed;
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
		public Task<T> ExecuteAsync<T>(Func<DatabaseTransactionContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
			inner.ExecuteAsync(async (transaction, token) =>
			{
				var result = await operation(transaction, token);
				if (Interlocked.Exchange(ref _armed, 0) == 1)
				{
					_beforeCommit.TrySetResult();
					await _release.Task.WaitAsync(token);
				}
				return result;
			}, cancellationToken);
	}

	private sealed class ObservedConnectionFactory(IDatabaseConnectionFactory inner) : IDatabaseConnectionFactory
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
}
