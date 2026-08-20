// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public abstract class SalesSectionPageViewModel : BaseViewModel, IDisposable
{
	protected SalesSectionPageViewModel(SalesViewModel workspace, SalesSection section)
	{
		Workspace = workspace;
		Section = section;
		Workspace.Section = section;
	}

	public SalesViewModel Workspace { get; }
	public SalesSection Section { get; }

	public Task LoadAsync(CancellationToken cancellationToken = default)
	{
		Workspace.Section = Section;
		return Workspace.LoadAsync(cancellationToken);
	}

	public Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		Workspace.Section = Section;
		return Workspace.LoadAsync(cancellationToken);
	}

	public bool HasUnsavedChanges()
	{
		Workspace.Section = Section;
		return Workspace.HasUnsavedChanges();
	}

	public void DiscardUnsavedChanges()
	{
		Workspace.Section = Section;
		Workspace.DiscardUnsavedChanges();
	}

	public void Dispose() => Workspace.Dispose();
}

public sealed class SalesOverviewViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Overview);
public sealed class CustomersViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Customers);
public sealed class SalesOrdersViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.SalesOrders);
public sealed class SalesApprovalsViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Approvals);
public sealed class ShippingViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Shipping);
public sealed class SalesInvoicesViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Invoices);
