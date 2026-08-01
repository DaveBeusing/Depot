// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;
using Depot.ViewModels;
using Depot.ViewModels.Login;

namespace Depot.Composition;

internal sealed class ViewModelFactory
{
	private readonly DatabaseComposition _database;
	private readonly ServiceComposition _services;
	private readonly IFileDialogService _fileDialogs;
	private readonly ApplicationInformationService _applicationInformation;

	public ViewModelFactory(
		DatabaseComposition database,
		ServiceComposition services,
		IFileDialogService fileDialogs,
		ApplicationInformationService applicationInformation)
	{
		_database = database;
		_services = services;
		_fileDialogs = fileDialogs;
		_applicationInformation = applicationInformation;
	}

	public LoginViewModel CreateLogin() =>
		new(_services.Authentication, _database.ConnectionStatus);

	public MainViewModel CreateMain() =>
		new(
			_services.Items,
			_services.Stock,
			_services.Movements,
			_services.Reports,
			_services.Purposes,
			_services.ReasonCodes,
			_services.Manufacturers,
			_services.Categories,
			_services.UnitsOfMeasure,
			_services.Packagings,
			_services.SupplierCategories,
			_services.Suppliers,
			_services.SupplierItems,
			_services.PurchaseOrders,
			_services.PurchaseOrderApprovals,
			_services.GoodsReceipts,
			_services.StockTransfers,
			_services.InventoryCounts,
			_services.MaterialIssues,
			_services.MaterialReturns,
			_services.Warehouses,
			_services.StorageLocations,
			_services.Users,
			_services.Authorization,
			_services.Session,
			_services.Import,
			_fileDialogs,
			_database.Settings,
			_database.ConnectionStatus,
			_database.ConnectionTester,
			_database.Management,
			_services.AuditLog,
			_applicationInformation);
}
