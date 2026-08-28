// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;
using Depot.ViewModels;
using Depot.ViewModels.Login;

namespace Depot.Composition;

internal sealed class ViewModelFactory
{
	private const string FinanceIcon="M 3,5 L 17,5 M 4,9 L 16,9 M 5,13 L 15,13 M 7,2 L 13,2 L 15,5 L 5,5 Z M 6,17 L 14,17";
	private readonly DatabaseComposition _database;
	private readonly ServiceComposition _services;
	private readonly IFileDialogService _fileDialogs;
	private readonly ApplicationInformationService _applicationInformation;

	public ViewModelFactory(DatabaseComposition database, ServiceComposition services, IFileDialogService fileDialogs, ApplicationInformationService applicationInformation)
	{
		_database=database; _services=services; _fileDialogs=fileDialogs; _applicationInformation=applicationInformation;
	}

	public LoginViewModel CreateLogin()=>new(_services.Authentication,_database.ConnectionStatus);

	public MainViewModel CreateMain()
	{
		var main=new MainViewModel(
			_services.Items,
			_services.Stock,
			_services.Dashboard,
			_services.Movements,
			_services.Reports,
			_services.AccountsReceivable,
			_services.AccountsPayable,
			_services.InventoryAccounting,
			_services.InventoryCosting,
			_services.InventoryMovementAccounting,
			_services.Banking,
			_services.FinancialReporting,
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
			_services.PurchaseOrderHistory,
			_services.GoodsReceipts,
			_services.StockTransfers,
			_services.InventoryCounts,
			_services.MaterialIssues,
			_services.MaterialReturns,
			_services.SupplierReturns,
			_services.Sales,
			_services.Warehouses,
			_services.StorageLocations,
			_services.Users,
			_services.Roles,
			_services.Authorization,
			_services.Session,
			_services.Import,
			_fileDialogs,
			_database.Settings,
			_database.ConnectionStatus,
			_database.ConnectionTester,
			_database.Management,
			_services.AuditLog,
			_applicationInformation,
			_services.Help,
			_services.HelpRenderer,
			_services.Notifications,
			_services.NotificationNavigation);
		AddLocalizationPage(main);
		return main;
	}

	private void AddLocalizationPage(MainViewModel main)
	{
		if(!_services.Authorization.HasPermission(ApplicationPermission.FinanceLocalizationView))return;
		var page=new SecondaryNavigationItem("Localization",()=>new FinanceLocalizationViewModel(_services.Localization),(viewModel,token)=>((FinanceLocalizationViewModel)viewModel).LoadAsync(token),"finance.localization");
		var financeItem=main.NavigationItems.FirstOrDefault(item=>item.Name=="Finance");
		if(financeItem?.Content is ShellModuleViewModel module)
		{
			module.Pages.Add(page);
			return;
		}
		var standaloneModule=new ShellModuleViewModel("Finance","Manage controlled Finance localization and effective compliance references.",[page]);
		main.NavigationItems.Add(new ShellNavigationItem("Finance",FinanceIcon,()=>standaloneModule,(viewModel,token)=>((ShellModuleViewModel)viewModel).ActivateAsync(token),"finance.localization",refreshAsync:(viewModel,token)=>((ShellModuleViewModel)viewModel).RefreshAsync(token)));
	}
}
