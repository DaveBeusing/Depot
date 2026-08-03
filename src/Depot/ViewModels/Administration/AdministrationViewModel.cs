// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Services;
using Depot.ViewModels.MasterData;
using Depot.ViewModels.Shared;
using Depot.ViewModels.Users;

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

		AddIf(authorization, ApplicationPermission.ImportManage, "Import", AdministrationSection.Import);
		AddIf(authorization, ApplicationPermission.MasterDataView, "Master Data", AdministrationSection.MasterData);
		AddIf(authorization, ApplicationPermission.UsersView, "Users", AdministrationSection.Users);
		AddIf(authorization, ApplicationPermission.RolesView, "Roles", AdministrationSection.Roles);
		AddIf(authorization, ApplicationPermission.DatabaseView, "Database", AdministrationSection.Database);
		AddIf(authorization, ApplicationPermission.AuditLogView, "Audit Log", AdministrationSection.AuditLog);
		AddIf(authorization, ApplicationPermission.SettingsView, "Settings", AdministrationSection.Settings);
		NavigationItems.Add(new NavigationItem { Name = "About", Section = AdministrationSection.About });
		SelectedNavigationItem = NavigationItems[0];
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
			AdministrationSection.Users => _userViewModel,
			AdministrationSection.Roles => _roleViewModel,
			AdministrationSection.Database => _databaseSettingsViewModel,
			AdministrationSection.AuditLog => _auditLogViewModel,
			AdministrationSection.Settings => new PlaceholderViewModel("Settings", "Application settings will be available in a future release."),
			AdministrationSection.About => _aboutViewModel,
			_ => null
		};
		_ = LoadCurrentViewModelAsync();
	}

	private Task LoadCurrentViewModelAsync() => CurrentViewModel switch
	{
		MasterDataViewModel masterData => masterData.LoadAsync(),
		UserViewModel users => users.LoadUsersAsync(),
		RoleViewModel roles => roles.LoadAsync(),
		DatabaseSettingsViewModel database => database.LoadAsync(),
		AuditLogViewModel auditLog => auditLog.LoadAsync(),
		_ => Task.CompletedTask
	};

	private void AddIf(IAuthorizationService authorization, ApplicationPermission permission, string name, AdministrationSection section)
	{
		if (authorization.HasPermission(permission)) NavigationItems.Add(new NavigationItem { Name = name, Section = section });
	}
}
