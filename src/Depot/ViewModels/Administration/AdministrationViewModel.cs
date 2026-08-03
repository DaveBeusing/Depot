// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Services;
using Depot.ViewModels.MasterData;
using Depot.ViewModels.Shared;
using Depot.ViewModels.Suppliers;
using Depot.ViewModels.Users;
using Depot.ViewModels.Warehouses;

namespace Depot.ViewModels.Administration;

public sealed class AdministrationViewModel : BaseViewModel
{
	private readonly ImportViewModel _importViewModel;
	private readonly MasterDataViewModel _masterDataViewModel;
	private readonly UserViewModel _userViewModel;
	private readonly RoleViewModel _roleViewModel;
	private readonly DatabaseSettingsViewModel _databaseSettingsViewModel;
	private readonly AuditLogViewModel _auditLogViewModel;
	private readonly AboutViewModel _aboutViewModel;
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public AdministrationViewModel(
		ImportViewModel importViewModel,
		ItemService itemService,
		PurposeService purposeService,
		ReasonCodeService reasonCodeService,
		ManufacturerService manufacturerService,
		CategoryService categoryService,
		UnitOfMeasureService unitOfMeasureService,
		PackagingService packagingService,
		SupplierCategoryService supplierCategoryService,
		SupplierService supplierService,
		SupplierItemService supplierItemService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService,
		UserService userService,
		RoleService roleService,
		IAuthorizationService authorization,
		SettingsService settingsService,
		ConnectionStatusService connectionStatusService,
		DatabaseConnectionTester databaseConnectionTester,
		DatabaseManagementService databaseManagementService,
		AuditLogService auditLogService,
		IFileDialogService fileDialogService,
		ApplicationInformationService applicationInformationService)
	{
		_importViewModel = importViewModel;
		_masterDataViewModel = new MasterDataViewModel(purposeService, reasonCodeService, manufacturerService, categoryService, unitOfMeasureService, packagingService, supplierCategoryService, supplierService, supplierItemService, itemService, warehouseService, storageLocationService);
		_userViewModel = new UserViewModel(userService);
		_roleViewModel = new RoleViewModel(roleService);
		_databaseSettingsViewModel = new DatabaseSettingsViewModel(settingsService, connectionStatusService, databaseConnectionTester, databaseManagementService, fileDialogService);
		_aboutViewModel = new AboutViewModel(applicationInformationService);
		_auditLogViewModel = new AuditLogViewModel(auditLogService, fileDialogService);

		AddIf(authorization, ApplicationPermission.MasterDataView, "Master Data", AdministrationSection.MasterData);
		AddIf(authorization, ApplicationPermission.MasterDataView, "Warehouses & Locations", AdministrationSection.Warehouses);
		AddIf(authorization, ApplicationPermission.SuppliersView, "Suppliers", AdministrationSection.Suppliers);
		AddIf(authorization, ApplicationPermission.UsersView, "Users", AdministrationSection.Users);
		AddIf(authorization, ApplicationPermission.RolesView, "Roles", AdministrationSection.Roles);
		AddIf(authorization, ApplicationPermission.ImportManage, "Import", AdministrationSection.Import);
		AddIf(authorization, ApplicationPermission.AuditLogView, "Audit Log", AdministrationSection.AuditLog);
		AddIf(authorization, ApplicationPermission.DatabaseView, "Database", AdministrationSection.Database);
		if (authorization.HasPermission(ApplicationPermission.AdministrationView))
			NavigationItems.Add(new NavigationItem { Name = "About", Section = AdministrationSection.About });
		SelectedNavigationItem = NavigationItems.FirstOrDefault();
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; } = new();
	public NavigationItem? SelectedNavigationItem { get => _selectedNavigationItem; set { _selectedNavigationItem = value; OnPropertyChanged(); UpdateCurrentViewModel(); } }
	public BaseViewModel? CurrentViewModel { get => _currentViewModel; private set { _currentViewModel = value; OnPropertyChanged(); } }

	private void UpdateCurrentViewModel()
	{
		CurrentViewModel = SelectedNavigationItem?.Section is not AdministrationSection section ? null : section switch
		{
			AdministrationSection.Import => _importViewModel,
			AdministrationSection.MasterData => _masterDataViewModel,
			AdministrationSection.Warehouses => _masterDataViewModel.WarehouseStructureViewModel,
			AdministrationSection.Suppliers => _masterDataViewModel.SupplierViewModel,
			AdministrationSection.Users => _userViewModel,
			AdministrationSection.Roles => _roleViewModel,
			AdministrationSection.Database => _databaseSettingsViewModel,
			AdministrationSection.AuditLog => _auditLogViewModel,
			AdministrationSection.About => _aboutViewModel,
			_ => null
		};
		_ = LoadCurrentViewModelAsync();
	}

	public Task LoadAsync(CancellationToken cancellationToken = default) => LoadCurrentViewModelAsync(cancellationToken);

	private Task LoadCurrentViewModelAsync(CancellationToken cancellationToken = default) => CurrentViewModel switch
	{
		MasterDataViewModel masterData => masterData.LoadAsync(cancellationToken),
		WarehouseStructureViewModel warehouses => warehouses.LoadAsync(cancellationToken),
		SupplierViewModel suppliers => suppliers.LoadAsync(cancellationToken),
		UserViewModel users => users.LoadUsersAsync(cancellationToken),
		RoleViewModel roles => roles.LoadAsync(cancellationToken),
		DatabaseSettingsViewModel database => database.LoadAsync(cancellationToken),
		AuditLogViewModel auditLog => auditLog.LoadAsync(cancellationToken),
		_ => Task.CompletedTask
	};

	private void AddIf(IAuthorizationService authorization, ApplicationPermission permission, string name, AdministrationSection section)
	{
		if (authorization.HasPermission(permission)) NavigationItems.Add(new NavigationItem { Name = name, Section = section });
	}
}
