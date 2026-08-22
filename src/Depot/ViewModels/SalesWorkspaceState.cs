// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

using Depot.Models;

namespace Depot.ViewModels;

public sealed class SalesWorkspaceState
{
	private static readonly ConditionalWeakTable<SalesViewModel, SalesWorkspaceState> States = new();
	private readonly SalesViewModel _workspace;

	private SalesWorkspaceState(SalesViewModel workspace) => _workspace = workspace;

	public static SalesWorkspaceState For(SalesViewModel workspace)
	{
		ArgumentNullException.ThrowIfNull(workspace);
		return States.GetValue(workspace, static value => new SalesWorkspaceState(value));
	}

	public SalesViewModel Workspace => _workspace;
	public SalesSection Section { get => _workspace.Section; set => _workspace.Section = value; }
	public string SearchText { get => _workspace.SearchText; set => _workspace.SearchText = value; }
	public Customer? SelectedCustomer { get => _workspace.SelectedCustomer; set => _workspace.SelectedCustomer = value; }
	public SalesOrder? SelectedOrder { get => _workspace.SelectedOrder; set => _workspace.SelectedOrder = value; }
	public Shipment? SelectedShipment { get => _workspace.SelectedShipment; set => _workspace.SelectedShipment = value; }
	public SalesInvoice? SelectedInvoice { get => _workspace.SelectedInvoice; set => _workspace.SelectedInvoice = value; }
	public CustomerReturn? SelectedCustomerReturn { get => _workspace.SelectedCustomerReturn; set => _workspace.SelectedCustomerReturn = value; }
	public SalesCreditNote? SelectedCreditNote { get => _workspace.SelectedCreditNote; set => _workspace.SelectedCreditNote = value; }

	public void ClearSelection()
	{
		SelectedCustomer = null;
		SelectedOrder = null;
		SelectedShipment = null;
		SelectedInvoice = null;
		SelectedCustomerReturn = null;
		SelectedCreditNote = null;
	}
}
