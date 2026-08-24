// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class PurchaseOverviewViewModel : BaseViewModel
{
	private readonly PurchaseOrderService _orders;
	private readonly LatestRequest _request = new();
	private long _openOrdersCount;
	private long _pendingApprovalCount;
	private long _approvedToOrderCount;
	private long _awaitingReceiptCount;
	private long _partiallyReceivedCount;
	private long _receivedOrdersCount;

	public PurchaseOverviewViewModel(PurchaseOrderService orders) => _orders = orders;

	public long OpenOrdersCount { get => _openOrdersCount; private set => SetMetric(ref _openOrdersCount, value); }
	public long PendingApprovalCount { get => _pendingApprovalCount; private set => SetMetric(ref _pendingApprovalCount, value); }
	public long ApprovedToOrderCount { get => _approvedToOrderCount; private set => SetMetric(ref _approvedToOrderCount, value); }
	public long AwaitingReceiptCount { get => _awaitingReceiptCount; private set => SetMetric(ref _awaitingReceiptCount, value); }
	public long PartiallyReceivedCount { get => _partiallyReceivedCount; private set => SetMetric(ref _partiallyReceivedCount, value); }
	public long ReceivedOrdersCount { get => _receivedOrdersCount; private set => SetMetric(ref _receivedOrdersCount, value); }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _request.Begin(cancellationToken);
		BeginOperation("Loading purchasing overview");
		try
		{
			var draftTask = CountAsync(PurchaseOrderStatus.Draft, request.Token);
			var pendingTask = CountAsync(PurchaseOrderStatus.PendingApproval, request.Token);
			var approvedTask = CountAsync(PurchaseOrderStatus.Approved, request.Token);
			var orderedTask = CountAsync(PurchaseOrderStatus.Ordered, request.Token);
			var partialTask = CountAsync(PurchaseOrderStatus.PartiallyReceived, request.Token);
			var receivedTask = CountAsync(PurchaseOrderStatus.Received, request.Token);

			await Task.WhenAll(draftTask, pendingTask, approvedTask, orderedTask, partialTask, receivedTask);
			if (!request.IsCurrent) return;

			var draft = await draftTask;
			var pending = await pendingTask;
			var approved = await approvedTask;
			var ordered = await orderedTask;
			var partial = await partialTask;
			var received = await receivedTask;

			OpenOrdersCount = draft + pending + approved + ordered + partial;
			PendingApprovalCount = pending;
			ApprovedToOrderCount = approved;
			AwaitingReceiptCount = ordered + partial;
			PartiallyReceivedCount = partial;
			ReceivedOrdersCount = received;
			CompleteOperation(false, "Purchasing overview loaded");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Purchasing overview could not be loaded"); }
	}

	private async Task<long> CountAsync(PurchaseOrderStatus status, CancellationToken cancellationToken) =>
		(await _orders.SearchAsync(null, status, 1, 1, cancellationToken)).TotalCount;

	private void SetMetric(ref long field, long value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		if (field == value) return;
		field = value;
		OnPropertyChanged(propertyName);
	}
}
