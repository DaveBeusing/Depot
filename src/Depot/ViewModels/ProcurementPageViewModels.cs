// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class PurchaseOrdersPageViewModel : BaseViewModel
{
	public PurchaseOrdersPageViewModel(ProcurementViewModel workflow) => Workflow = workflow;

	public ProcurementViewModel Workflow { get; }

	public Task LoadAsync(CancellationToken cancellationToken = default)
	{
		Workflow.Section = ProcurementSection.PurchaseOrders;
		return Workflow.LoadAsync(cancellationToken);
	}
}

public sealed class GoodsReceiptsPageViewModel : BaseViewModel
{
	public GoodsReceiptsPageViewModel(ProcurementViewModel workflow) => Workflow = workflow;

	public ProcurementViewModel Workflow { get; }

	public Task LoadAsync(CancellationToken cancellationToken = default)
	{
		Workflow.Section = ProcurementSection.GoodsReceipts;
		return Workflow.LoadAsync(cancellationToken);
	}
}
